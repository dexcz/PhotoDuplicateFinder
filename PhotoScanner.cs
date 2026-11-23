using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Threading;

namespace PhotoDuplicateFinder
{
    internal sealed class Matches
    {
        public string Digest;
        public long Size;
        public List<string> Paths;
        public Dictionary<string, VisualPhoto> Versions;
        public long SizeOf(string path) { return Versions == null ? Size : Versions[path].Size; }
        public string DigestOf(string path) { return Versions == null ? Digest : Versions[path].Digest; }
    }

    internal sealed class ScanResult
    {
        public List<Matches> Groups = new List<Matches>();
        public int Photos;
        public int Skipped;
    }

    internal static class PhotoScanner
    {
        private static readonly HashSet<string> Extensions = new HashSet<string>(
            ".jpg .jpeg .jfif .png .webp .bmp .gif .tif .tiff .heic .heif .avif .dng .cr2 .cr3 .nef .arw .raf .orf .rw2".Split(' '),
            StringComparer.OrdinalIgnoreCase);

        private static bool IsFileError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException;
        }

        public static ScanResult Scan(string root, bool recursive, CancellationToken token)
        {
            return ScanFolder(root, recursive, false, token);
        }

        public static ScanResult ScanFolder(string root, bool recursive, bool visual, CancellationToken token, ScanProgress progress = null)
        {
            progress = progress ?? new ScanProgress();
            var discovery = progress.Current;
            var result = new ScanResult();
            var bySize = new Dictionary<long, List<string>>();
            var folders = new Stack<DirectoryInfo>();
            var selected = new DirectoryInfo(root);
            CheckOrdinaryPath(selected.FullName);
            folders.Push(selected);
            while (folders.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                var folder = folders.Pop();
                try
                {
                    foreach (var entry in folder.EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { result.Skipped++; continue; }
                            var child = entry as DirectoryInfo;
                            if (child != null)
                            {
                                if (recursive) folders.Push(child);
                                continue;
                            }
                            if (!Extensions.Contains(entry.Extension)) continue;
                            long size = ((FileInfo)entry).Length;
                            if (size == 0) continue;
                            result.Photos++;
                            discovery.Advance();
                            List<string> candidates;
                            if (!bySize.TryGetValue(size, out candidates))
                                bySize.Add(size, candidates = new List<string>());
                            candidates.Add(entry.FullName);
                        }
                        catch (Exception ex) when (IsFileError(ex)) { result.Skipped++; }
                    }
                }
                catch (Exception ex) when (IsFileError(ex) && folder != selected) { result.Skipped++; }
            }

            var buffer = new byte[128 * 1024];
            if (visual)
            {
                var reading = progress.Begin("Reading images (1/2)", result.Photos);
                var photos = new List<VisualPhoto>();
                foreach (string path in bySize.Values.SelectMany(paths => paths))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using (var stream = File.OpenRead(path))
                        {
                            var photo = VisualPhoto.Read(path, stream);
                            stream.Position = 0;
                            photo.Digest = Hash(stream, buffer, token);
                            photos.Add(photo);
                        }
                    }
                    catch (Exception ex) when (IsFileError(ex) || ex is FormatException || ex is NotSupportedException ||
                        ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException ||
                        ex is System.Runtime.InteropServices.COMException) { result.Skipped++; }
                    reading.Advance();
                }
                result.Groups = VisualMatcher.Group(photos, token, progress.Begin("Comparing images (2/2)", photos.Count));
                return result;
            }
            var buckets = bySize.Where(p => p.Value.Count > 1).ToList();
            var hashing = progress.Begin("Checking exact copies", buckets.Sum(p => p.Value.Count));
            foreach (var bucket in buckets)
            {
                var byDigest = new Dictionary<string, List<string>>();
                foreach (string path in bucket.Value)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using (var stream = File.OpenRead(path))
                        {
                            if (stream.Length != bucket.Key) { result.Skipped++; continue; }
                            string digest = Hash(stream, buffer, token);
                            List<string> copies;
                            if (!byDigest.TryGetValue(digest, out copies))
                                byDigest.Add(digest, copies = new List<string>());
                            copies.Add(path);
                        }
                    }
                    catch (Exception ex) when (IsFileError(ex)) { result.Skipped++; }
                    finally { hashing.Advance(); }
                }
                foreach (var pair in byDigest.Where(p => p.Value.Count > 1))
                {
                    pair.Value.Sort(StringComparer.Ordinal);
                    result.Groups.Add(new Matches { Digest = pair.Key, Size = bucket.Key, Paths = pair.Value });
                }
            }
            result.Groups = result.Groups.OrderByDescending(g => g.Size * (g.Paths.Count - 1))
                .ThenBy(g => g.Paths[0], StringComparer.Ordinal).ToList();
            return result;
        }

        private static string Hash(Stream stream, byte[] buffer, CancellationToken token)
        {
            using (var hash = SHA256.Create())
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    hash.TransformBlock(buffer, 0, read, buffer, 0);
                }
                hash.TransformFinalBlock(buffer, 0, 0);
                return Convert.ToBase64String(hash.Hash);
            }
        }

        private static void CheckOrdinaryPath(string path)
        {
            for (string part = path; part != null; part = Path.GetDirectoryName(part))
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("This path contains a link or a cloud placeholder.");
        }

        public static void RecycleCopy(Matches group, string keeper, string copy)
        {
            if (keeper == copy || !group.Paths.Contains(keeper) || !group.Paths.Contains(copy))
                throw new InvalidOperationException("Choose an unchecked copy to keep in this group.");
            CheckOrdinaryPath(keeper);
            CheckOrdinaryPath(copy);
            var buffer = new byte[128 * 1024];
            // Deny writes to both files and keep the survivor in place through the shell operation.
            using (var survivor = File.OpenRead(keeper))
            using (var candidate = File.Open(copy, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (survivor.Length != group.SizeOf(keeper) || candidate.Length != group.SizeOf(copy) ||
                    Hash(survivor, buffer, CancellationToken.None) != group.DigestOf(keeper) ||
                    Hash(candidate, buffer, CancellationToken.None) != group.DigestOf(copy))
                    throw new IOException("A file has changed since the scan. Scan again.");
                RecycleBin.Move(copy);
            }
        }
    }
}
