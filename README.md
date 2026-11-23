# Photo Duplicate Finder

A Windows Forms desktop app for finding exact duplicates and visually similar versions of a photograph in a folder.

## Scan and review

1. Choose a folder. Leave **Include subfolders** checked to scan nested folders.
2. Choose **Exact files** to compare file size and SHA-256, or **Similar photos** to find resized and re-encoded versions. Click **Scan**.
3. In **Similar photos**, select a file and click **Compare versions…** to view two images side by side. Each pane shows the resolution, file size and path. Use its dropdown to inspect other versions in the group. The highest pixel count appears first.
4. Check the files you want to remove. Visual matches require manual selection. In exact mode, **Check extra copies** keeps the first unchecked path in each group and checks the rest.
5. Click **Move checked to Recycle Bin…** and confirm. The app verifies the SHA-256 of both the checked file and a retained file against their individual scan results before each move.

Visual matching compares colors and transparency at corresponding positions on a 32 × 32 grid after accounting for EXIF orientation where available. It tolerates resizing, compression and small changes in aspect ratio. Strong crops, edits and different camera angles may be missed. Every pair within a group passes the similarity check. Visual scans need a Windows codec for the image format and skip images the decoder reports as multi-frame.

The scan includes JPEG, PNG, WebP, BMP, GIF, TIFF, HEIC, HEIF, AVIF and common camera RAW extensions. Empty files are ignored. Windows links, junctions and other reparse points are skipped.

## Build and run

Open `PhotoDuplicateFinder.sln` in Visual Studio with the **.NET desktop development** workload and the **.NET Framework 4.8 targeting pack**. Build the **Release** configuration and run `bin\Release\PhotoDuplicateFinder.exe` on Windows 10 or 11 with .NET Framework 4.8 or later.
