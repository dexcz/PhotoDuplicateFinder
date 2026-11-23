using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoDuplicateFinder
{
    internal sealed class VisualPhoto
    {
        private const int Side = 32;
        public string Path, Digest;
        public long Size;
        public int Width, Height;
        public string Resolution { get { return Width + " × " + Height; } }
        public int Red, Green, Blue;
        private byte[] pixels;
        private double contrast;

        public static int ReadOrientation(BitmapFrame frame)
        {
            try
            {
                var metadata = frame.Metadata as BitmapMetadata;
                if (metadata != null)
                    foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
                    {
                        object value = metadata.GetQuery(query);
                        if (value is ushort) return (ushort)value;
                    }
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException ||
                ex is System.Runtime.InteropServices.COMException) { }
            return 1;
        }

        public static BitmapSource Orient(BitmapSource source, int orientation)
        {
            Matrix matrix;
            switch (orientation)
            {
                case 2: matrix = new Matrix(-1, 0, 0, 1, 0, 0); break;
                case 3: matrix = new Matrix(-1, 0, 0, -1, 0, 0); break;
                case 4: matrix = new Matrix(1, 0, 0, -1, 0, 0); break;
                case 5: matrix = new Matrix(0, 1, 1, 0, 0, 0); break;
                case 6: matrix = new Matrix(0, 1, -1, 0, 0, 0); break;
                case 7: matrix = new Matrix(0, -1, -1, 0, 0, 0); break;
                case 8: matrix = new Matrix(0, -1, 1, 0, 0, 0); break;
                default: return source;
            }
            return new TransformedBitmap(source, new MatrixTransform(matrix));
        }

        public static VisualPhoto Read(string path, Stream stream)
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count != 1) throw new NotSupportedException("Multi-frame image.");
            var frame = decoder.Frames[0];
            int orientation = ReadOrientation(frame);
            bool swap = orientation >= 5 && orientation <= 8;
            var photo = new VisualPhoto { Path = path, Size = stream.Length,
                Width = swap ? frame.PixelHeight : frame.PixelWidth, Height = swap ? frame.PixelWidth : frame.PixelHeight };
            stream.Position = 0;
            var thumb = new BitmapImage();
            thumb.BeginInit();
            thumb.StreamSource = stream;
            thumb.CacheOption = BitmapCacheOption.OnLoad;
            thumb.DecodePixelWidth = Side;
            thumb.DecodePixelHeight = Side;
            thumb.EndInit();
            var normalized = new FormatConvertedBitmap(Orient(thumb, orientation), PixelFormats.Pbgra32, null, 0);
            if (normalized.PixelWidth != Side || normalized.PixelHeight != Side)
                throw new NotSupportedException("The image could not be normalized.");
            photo.pixels = new byte[Side * Side * 4];
            normalized.CopyPixels(photo.pixels, Side * 4, 0);
            const int count = Side * Side;
            double sumSquares = 0;
            for (int i = 0; i < photo.pixels.Length; i += 4)
            {
                int b = photo.pixels[i], g = photo.pixels[i + 1], r = photo.pixels[i + 2];
                photo.Blue += b; photo.Green += g; photo.Red += r;
                sumSquares += b * b + g * g + r * r;
            }
            double meanSquares = ((double)photo.Blue * photo.Blue + (double)photo.Green * photo.Green +
                (double)photo.Red * photo.Red) / count / count;
            photo.contrast = Math.Sqrt(Math.Max(0, (sumSquares / count - meanSquares) / 3));
            photo.Blue /= count; photo.Green /= count; photo.Red /= count;
            return photo;
        }

        public bool SimilarTo(VisualPhoto other)
        {
            if (Digest == other.Digest) return true;
            double ratio = ((double)Width * other.Height) / ((double)Height * other.Width);
            if (Math.Min(ratio, 1 / ratio) < 0.975 || Math.Abs(Red - other.Red) > 12 ||
                Math.Abs(Green - other.Green) > 12 || Math.Abs(Blue - other.Blue) > 12) return false;
            double tolerance = Math.Min(15, 0.28 * Math.Min(contrast, other.contrast));
            double colorLimit = Side * Side * 3 * tolerance * tolerance;
            int colorError = 0, alphaError = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int b = pixels[i] - other.pixels[i], g = pixels[i + 1] - other.pixels[i + 1];
                int r = pixels[i + 2] - other.pixels[i + 2], a = pixels[i + 3] - other.pixels[i + 3];
                colorError += r * r + g * g + b * b;
                alphaError += a * a;
                if (colorError > colorLimit || alphaError > Side * Side * 36) return false;
            }
            return true;
        }
    }

    internal static class VisualMatcher
    {
        private static int Key(int r, int g, int b) { return (r * 16 + g) * 16 + b; }

        public static List<Matches> Group(List<VisualPhoto> photos, CancellationToken token, ScanProgress.Phase progress)
        {
            // Index group representatives by average color. Adjacent bins cover the per-channel tolerance.
            var bins = new Dictionary<int, List<List<VisualPhoto>>>();
            foreach (var photo in photos.OrderByDescending(p => (long)p.Width * p.Height)
                .ThenByDescending(p => p.Size).ThenBy(p => p.Path, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                int red = photo.Red / 16, green = photo.Green / 16, blue = photo.Blue / 16;
                List<VisualPhoto> group = null;
                for (int r = Math.Max(0, red - 1); r <= Math.Min(15, red + 1) && group == null; r++)
                    for (int g = Math.Max(0, green - 1); g <= Math.Min(15, green + 1) && group == null; g++)
                        for (int b = Math.Max(0, blue - 1); b <= Math.Min(15, blue + 1) && group == null; b++)
                        {
                            List<List<VisualPhoto>> nearby;
                            if (!bins.TryGetValue(Key(r, g, b), out nearby)) continue;
                            foreach (var candidate in nearby)
                            {
                                token.ThrowIfCancellationRequested();
                                if (candidate.All(photo.SimilarTo)) { group = candidate; break; }
                            }
                        }
                if (group == null)
                {
                    List<List<VisualPhoto>> local;
                    int key = Key(red, green, blue);
                    if (!bins.TryGetValue(key, out local)) bins.Add(key, local = new List<List<VisualPhoto>>());
                    group = new List<VisualPhoto>();
                    local.Add(group);
                }
                group.Add(photo);
                progress.Advance();
            }
            return bins.Values.SelectMany(bin => bin).Where(g => g.Count > 1)
                .Select(g => new Matches { Paths = g.Select(p => p.Path).ToList(), Versions = g.ToDictionary(p => p.Path) })
                .OrderByDescending(g => g.Paths.Count).ThenBy(g => g.Paths[0], StringComparer.Ordinal).ToList();
        }
    }
}
