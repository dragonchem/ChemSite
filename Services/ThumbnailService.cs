using ImageMagick;
using FFMpegCore;
using System.Runtime.Versioning;

namespace ChemSite.Services
{
    public static class RebuildState
    {
        public static volatile bool IsRunning;
        public static volatile bool IsComplete;
        public static int Total;
        public static volatile int Done;
        public static volatile string CurrentFile = "";
        public static volatile string Error = "";
    }

    [SupportedOSPlatform("windows")]
    public static class ThumbnailService
    {
        public static readonly List<string> ImageExtensions = new() { ".JPG", ".JPEG", ".JPE", ".BMP", ".GIF", ".PNG" };
        public static readonly List<string> VideoExtensions = new() { ".MP4", ".MOV", ".WEBM" };

        public static void GenerateThumbnailImage(string originalPath, string thumbPath, string thumbDir)
        {
            Directory.CreateDirectory(thumbDir);
            using var image = new MagickImage(originalPath);
            image.Resize(new MagickGeometry(400, 400) { IgnoreAspectRatio = false });
            image.Write(thumbPath);
        }

        public static void GenerateThumbnailVideo(string originalPath, string thumbPath, string thumbDir)
        {
            Directory.CreateDirectory(thumbDir);
            var mediaInfo = FFProbe.Analyse(originalPath);
            if (mediaInfo.PrimaryVideoStream == null) return;
            FFMpegArguments
                .FromFileInput(originalPath, false, args => args
                    .WithCustomArgument("-hwaccel auto"))
                .OutputToFile(thumbPath, true, args => args
                    .WithFrameOutputCount(1)
                    .Seek(mediaInfo.Duration / 2)
                    .ForceFormat("image2"))
                .ProcessSynchronously();
        }

        public static MagickGeometry GetTileGeometry(int count) => count switch
        {
            1 => new MagickGeometry(0, 0, 1u, 1u),
            2 => new MagickGeometry(0, 0, 2u, 1u),
            3 or 4 => new MagickGeometry(0, 0, 2u, 2u),
            5 or 6 => new MagickGeometry(0, 0, 3u, 2u),
            _ => new MagickGeometry(0, 0, 3u, 3u)
        };

        public static void GenerateCompositeImage(string[] sourcePaths, string destPath, uint tilePixels)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var collection = new MagickImageCollection();
            foreach (var p in sourcePaths)
            {
                var image = new MagickImage(p);
                var size = Math.Min(image.Width, image.Height);
                image.Resize(new MagickGeometry { Width = size, Height = size, FillArea = true });
                image.Crop(new MagickGeometry { Height = size, Width = size }, Gravity.Center);
                collection.Add(image);
            }
            using var result = collection.Montage(new MontageSettings
            {
                BackgroundColor = MagickColors.None,
                Geometry = new MagickGeometry(4, 4, tilePixels, tilePixels),
                TileGeometry = GetTileGeometry(sourcePaths.Length)
            });
            result.Write(destPath);
        }

        // Finds up to `limit` image files recursively under thumbDir (ordered by write time).
        private static List<string> FindThumbImages(string thumbDir, int limit)
        {
            var result = new List<string>();
            if (!Directory.Exists(thumbDir)) return result;

            foreach (var f in new DirectoryInfo(thumbDir).GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => f.FullName))
            {
                if (ImageExtensions.Contains(Path.GetExtension(f).ToUpper()))
                {
                    result.Add(f);
                    if (result.Count >= limit) return result;
                }
            }
            foreach (var sub in Directory.GetDirectories(thumbDir))
            {
                result.AddRange(FindThumbImages(sub, limit - result.Count));
                if (result.Count >= limit) break;
            }
            return result;
        }

        // Returns the filename (not full path) of the most-recently modified image in artDir or its subdirs.
        private static string? FindFirstArtImageName(string artDir)
        {
            if (!Directory.Exists(artDir)) return null;

            var direct = new DirectoryInfo(artDir).GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f.Name).ToUpper()));
            if (direct != null) return direct.Name;

            foreach (var sub in Directory.GetDirectories(artDir))
            {
                var found = FindFirstArtImageName(sub);
                if (found != null) return found;
            }
            return null;
        }

        private static void RebuildDirectoryComposites(
            string artDir, string artBase, string thumbBase,
            string galleryThumbBase, string folderThumbBase)
        {
            var thumbImages = FindThumbImages(Path.Combine(thumbBase, artDir.Substring(artBase.Length).TrimStart('\\')), 9);
            if (thumbImages.Count > 0)
            {
                string? keyName = FindFirstArtImageName(artDir);
                if (keyName != null)
                {
                    string relDir = artDir.Substring(artBase.Length).TrimStart('\\');
                    try
                    {
                        GenerateCompositeImage(thumbImages.ToArray(),
                            Path.Combine(galleryThumbBase, relDir, keyName), 1000u);
                    }
                    catch { }
                    try
                    {
                        GenerateCompositeImage(thumbImages.ToArray(),
                            Path.Combine(folderThumbBase, relDir, keyName), 200u);
                    }
                    catch { }
                }
            }

            foreach (var sub in Directory.GetDirectories(artDir))
                RebuildDirectoryComposites(sub, artBase, thumbBase, galleryThumbBase, folderThumbBase);
        }

        public static void RebuildAll(
            string artBasePath, string thumbBasePath, string ffmpegBasePath,
            string galleryThumbBasePath, string folderThumbBasePath)
        {
            RebuildState.IsRunning = true;
            RebuildState.IsComplete = false;
            RebuildState.Done = 0;
            RebuildState.Error = "";
            RebuildState.CurrentFile = "Clearing old thumbnails...";

            try
            {
                foreach (var dir in new[] { thumbBasePath, ffmpegBasePath })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        File.Delete(file);
                }

                // Phase 1: individual file thumbnails
                var allFiles = Directory.GetFiles(artBasePath, "*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        string ext = Path.GetExtension(f).ToUpper();
                        return ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext);
                    })
                    .ToArray();

                var allDirs = Directory.GetDirectories(artBasePath, "*", SearchOption.AllDirectories);
                RebuildState.Total = allFiles.Length + allDirs.Length;

                foreach (var file in allFiles)
                {
                    string relFile = file.Substring(artBasePath.Length).TrimStart('\\').Replace('\\', '/');
                    string ext = Path.GetExtension(file).ToUpper();
                    try
                    {
                        if (VideoExtensions.Contains(ext))
                        {
                            string ffmpegPath = file.Replace(artBasePath, ffmpegBasePath) + ".png";
                            string thumbPath = file.Replace(artBasePath, thumbBasePath) + ".png";
                            RebuildState.CurrentFile = $"[thumbnail - ffmpeg] {relFile}";
                            GenerateThumbnailVideo(file, ffmpegPath, Path.GetDirectoryName(ffmpegPath)!);
                            RebuildState.CurrentFile = $"[thumbnail - image] {relFile}";
                            GenerateThumbnailImage(ffmpegPath, thumbPath, Path.GetDirectoryName(thumbPath)!);
                        }
                        else
                        {
                            string thumbPath = file.Replace(artBasePath, thumbBasePath);
                            RebuildState.CurrentFile = $"[thumbnail - image] {relFile}";
                            GenerateThumbnailImage(file, thumbPath, Path.GetDirectoryName(thumbPath)!);
                        }
                    }
                    catch { }
                    RebuildState.Done++;
                }

                // Phase 2: composite folder thumbnails
                foreach (var dir in allDirs)
                {
                    string relDir = dir.Substring(artBasePath.Length).TrimStart('\\').Replace('\\', '/');
                    RebuildState.CurrentFile = $"[thumbnail - folder] {relDir}";
                    try
                    {
                        RebuildDirectoryComposites(dir, artBasePath, thumbBasePath,
                            galleryThumbBasePath, folderThumbBasePath);
                    }
                    catch { }
                    RebuildState.Done++;
                }

                RebuildState.CurrentFile = "Done";
                RebuildState.IsComplete = true;
            }
            catch (Exception ex)
            {
                RebuildState.Error = ex.Message;
            }
            finally
            {
                RebuildState.IsRunning = false;
            }
        }
    }
}
