using ChemSite.Models;
using ChemSite.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using ImageMagick;
using FFMpegCore;

namespace ChemSite.Controllers
{
    [SupportedOSPlatform("windows")]
    public abstract class BaseGalleryController : Controller
    {
        protected readonly string artBasePath;
        protected readonly string thumbBasePath;
        protected readonly string ffmpegBasePath;
        protected readonly string galleryThumbBasePath;
        protected readonly string folderThumbBasePath;
        protected readonly string artistsBasePath;
        protected static List<string> ImageExtensions => ThumbnailService.ImageExtensions;
        protected static List<string> VideoExtensions => ThumbnailService.VideoExtensions;
        protected readonly IWebHostEnvironment _webHostEnvironment;

        protected abstract bool IsNsfw { get; }
        protected abstract string GalleryPrefix { get; }

        protected BaseGalleryController(IWebHostEnvironment webHostEnvironment)
        {
            _webHostEnvironment = webHostEnvironment;
            artBasePath = Path.Combine(webHostEnvironment.WebRootPath, "art");
            thumbBasePath = Path.Combine(webHostEnvironment.WebRootPath, "thumb");
            ffmpegBasePath = Path.Combine(webHostEnvironment.WebRootPath, "ffmpeg");
            galleryThumbBasePath = Path.Combine(webHostEnvironment.WebRootPath, "thumb", "gallery");
            folderThumbBasePath = Path.Combine(webHostEnvironment.WebRootPath, "thumb", "folder");
            artistsBasePath = Path.Combine(webHostEnvironment.WebRootPath, "artists");
        }

        protected bool IsAdmin()
        {
            var ip = HttpContext.Connection.RemoteIpAddress;
            if (ip == null) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            return IPAddress.IsLoopback(ip) || IsInPrivateRange(ip);
        }

        private static bool IsInPrivateRange(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            if (b.Length != 4) return false;
            return b[0] == 10 ||
                   (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                   (b[0] == 192 && b[1] == 168);
        }

        protected FolderTheme ResolveTheme(string path)
        {
            string themesPath = Path.Combine(_webHostEnvironment.WebRootPath, "themes.json");
            if (!System.IO.File.Exists(themesPath)) return new FolderTheme();
            var config = JsonSerializer.Deserialize<ThemesConfig>(System.IO.File.ReadAllText(themesPath)) ?? new ThemesConfig();
            var dict = config.Themes
                .Where(t => t.Folder != null)
                .ToDictionary(t => t.Folder!, StringComparer.OrdinalIgnoreCase);

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = segments.Length; i >= 0; i--)
            {
                string key = string.Join("/", segments.Take(i));
                if (dict.TryGetValue(key, out var theme)) return theme;
            }
            return new FolderTheme();
        }

        protected IActionResult RenderFolder(string path, int page = 1)
        {
            ViewData["Theme"] = ResolveTheme(path);
            // True only if a theme is configured directly for this folder (not inherited)
            string themesFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "themes.json");
            ViewData["HasTheme"] = System.IO.File.Exists(themesFilePath) &&
                (JsonSerializer.Deserialize<ThemesConfig>(System.IO.File.ReadAllText(themesFilePath)) ?? new ThemesConfig())
                    .Themes.Any(t => string.Equals(t.Folder, path, StringComparison.OrdinalIgnoreCase));
            var folderViewModel = new GalleryFolderViewModel
            {
                Path = path,
                PathParts = path.Split('/'),
                IsNsfw = IsNsfw,
                GalleryPrefix = GalleryPrefix,
                IsAdmin = IsAdmin(),
                Page = page,
                PageSize = 24
            };

            string fullPath = Path.Combine(artBasePath, path);
            if (!Directory.Exists(fullPath)) return NotFound();
            if (!IsNsfw && ContainsNsfw(path)) return NotFound();

            string[] directories = Directory.GetDirectories(Path.Combine(artBasePath, path));
            if (!IsNsfw) directories = directories.Where(x => !ContainsNsfw(x)).ToArray();

            string[] files = new DirectoryInfo(Path.Combine(artBasePath, path))
                .GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => Path.Combine(artBasePath, path, f.Name))
                .Where(x => ImageExtensions.Contains(Path.GetExtension(x).ToUpper()) || VideoExtensions.Contains(Path.GetExtension(x).ToUpper()))
                .Where(x => IsNsfw || !ContainsNsfw(x))
                .ToArray();

            // Process video files: generate ffmpeg frames
            var videoFiles = files.Where(x => VideoExtensions.Contains(Path.GetExtension(x).ToUpper())).ToList();
            for (int i = 0; i < videoFiles.Count; i++)
            {
                string file = videoFiles[i];
                string thumb = file.Replace(artBasePath, thumbBasePath) + ".png";
                string ffmpegPath = file.Replace(artBasePath, ffmpegBasePath) + ".png";

                if (!System.IO.File.Exists(ffmpegPath))
                    GenerateThumbnailVideo(file, ffmpegPath, Path.GetDirectoryName(ffmpegPath)!);

                videoFiles[i] = ffmpegPath;
                var idx = files.ToList().FindIndex(x => x == file);
                files[idx] = ffmpegPath;

                if (!System.IO.File.Exists(thumb))
                    GenerateThumbnailImage(ffmpegPath, thumb, Path.GetDirectoryName(thumb)!);
            }

            foreach (var directory in directories)
            {
                folderViewModel.Folders.Add(new GalleryFolderContent
                {
                    Title = directory.Split('\\').Last(),
                    ImageUrl = RemoveFilePath(GetDirectoryImage(directory)),
                    Path = RemoveFilePath(directory)
                });
            }

            var allImages = new List<GalleryImageContent>();
            foreach (var file in files)
            {
                string thumb = file.Replace(artBasePath, thumbBasePath);
                if (!System.IO.File.Exists(thumb))
                    GenerateThumbnailImage(file, thumb, Path.GetDirectoryName(thumb)!);

                ImageInfo? imageInfo = LoadImageInfo(file);
                bool pinned = imageInfo?.Pinned == true;

                allImages.Add(new GalleryImageContent
                {
                    Title = file.Split('\\').Last(),
                    ImageUrl = RemoveFilePath(thumb),
                    Path = RemoveFilePath(file),
                    ImageInfo = imageInfo,
                    Image = VideoExtensions.FindIndex(x => file.ToUpper().Contains(x)) == -1,
                    Pinned = pinned
                });
            }

            allImages = allImages.OrderByDescending(x => x.ImageInfo?.CreationDate).ToList();
            folderViewModel.PinnedImages = allImages.Where(x => x.Pinned).ToList();
            folderViewModel.TotalImages = allImages.Count;
            folderViewModel.Images = allImages.Skip((page - 1) * 24).Take(24).ToList();

            // Build gallery composite thumbnail
            var amount = Math.Min(files.Length, 9);
            var galleryFiles = files.Take(amount).ToList();
            if (amount < 9)
            {
                foreach (var directory in directories)
                {
                    if (!IsNsfw && ContainsNsfw(directory)) continue;
                    var images = FindDirectoryImages(directory, 0);
                    foreach (var image in images)
                    {
                        galleryFiles.Add(image);
                        if (galleryFiles.Count >= 9) break;
                    }
                }
            }

            string relativeDir = path.Replace(artBasePath, "");
            if (relativeDir.StartsWith("\\")) relativeDir = relativeDir.Substring(1);
            if (galleryFiles.Count > 0)
            {
                var galleryfile = GetFirstDirectoryImage(Path.Combine(artBasePath, path));
                if (string.IsNullOrEmpty(galleryfile)) galleryfile = galleryFiles[0];
                folderViewModel.ImageUrl = GenerateGalleryImage(
                    galleryFiles.Take(9).ToArray(),
                    Path.Combine(galleryThumbBasePath, relativeDir, Path.GetFileName(galleryfile))
                );
            }

            return View("Folder", folderViewModel);
        }

        protected IActionResult RenderImage(string path)
        {
            ViewData["Theme"] = ResolveTheme(path);
            var folderViewModel = new GalleryImageViewModel
            {
                Path = path,
                PathParts = path.Split('/'),
                IsNsfw = IsNsfw,
                GalleryPrefix = GalleryPrefix,
                IsAdmin = IsAdmin()
            };

            string file = Path.Combine(artBasePath, path).Replace("/", "\\");
            if (!System.IO.File.Exists(file)) return NotFound();

            string thumb = file.Replace(artBasePath, thumbBasePath);
            if (!System.IO.File.Exists(thumb) && VideoExtensions.FindIndex(x => file.ToUpper().Contains(x)) == -1)
                GenerateThumbnailImage(file, thumb, Path.GetDirectoryName(thumb)!);

            ImageInfo? imageInfo = null;
            string imageInfoPath = Path.ChangeExtension(file, "json");
            if (System.IO.File.Exists(imageInfoPath))
            {
                string fileData = System.IO.File.ReadAllText(imageInfoPath);
                imageInfo = JsonSerializer.Deserialize<ImageInfo>(fileData);

                if (imageInfo != null)
                {
                    foreach (string artist in imageInfo.Artists)
                    {
                        string artistPath = Path.Combine(artistsBasePath, artist + ".json");
                        if (System.IO.File.Exists(artistPath))
                        {
                            string artistData = System.IO.File.ReadAllText(artistPath);
                            ArtistInfo? artistInfo = JsonSerializer.Deserialize<ArtistInfo>(artistData);
                            if (artistInfo == null) continue;
                            if (!IsNsfw)
                                artistInfo.PlatformInfo = artistInfo.PlatformInfo.Where(x => !x.Nsfw).ToArray();
                            imageInfo.ArtistsInfo.Add(artistInfo);
                        }
                    }

                    foreach (string altPath in imageInfo.AltPaths)
                    {
                        string filePath = Path.Combine(artBasePath, Path.ChangeExtension(altPath, "json"));
                        if (System.IO.File.Exists(filePath) && (IsNsfw || (!filePath.ToLower().Contains("nsfw") && !filePath.ToLower().Contains("kinky"))))
                        {
                            string altData = System.IO.File.ReadAllText(filePath);
                            ImageInfo? altInfo = JsonSerializer.Deserialize<ImageInfo>(altData);
                            if (altInfo == null) continue;
                            imageInfo.AltInfo.Add(altInfo);
                        }
                    }
                }
            }

            folderViewModel.Image = new GalleryImageContent
            {
                Title = file.Split('\\').Last(),
                ImageUrl = RemoveFilePath(thumb),
                Path = RemoveFilePath(file),
                ImageInfo = imageInfo,
                Image = VideoExtensions.FindIndex(x => file.ToUpper().Contains(x)) == -1,
                Pinned = imageInfo?.Pinned == true
            };

            if (folderViewModel.Image.ImageInfo != null)
                folderViewModel.PathParts[folderViewModel.PathParts.Length - 1] = folderViewModel.Image.ImageInfo.Name;

            return View("Image", folderViewModel);
        }

        protected bool IsFile(string path) => System.IO.File.Exists(Path.Combine(artBasePath, path));

        protected static bool ContainsNsfw(string path) =>
            path.ToLower().Contains("nsfw") || path.ToLower().Contains("kinky");

        private ImageInfo? LoadImageInfo(string file)
        {
            string imageInfoPath;
            if (file.Contains(ffmpegBasePath))
            {
                // ffmpeg path is video.mp4.png — strip appended .png, map back to art dir, get .json
                string artFile = file.Replace(ffmpegBasePath, artBasePath);
                if (artFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    artFile = artFile.Substring(0, artFile.Length - 4);
                imageInfoPath = Path.ChangeExtension(artFile, "json");
            }
            else
            {
                imageInfoPath = Path.ChangeExtension(file, "json");
            }
            if (!System.IO.File.Exists(imageInfoPath)) return null;

            string fileData = System.IO.File.ReadAllText(imageInfoPath);
            ImageInfo? imageInfo = JsonSerializer.Deserialize<ImageInfo>(fileData);
            if (imageInfo == null) return null;

            foreach (string artist in imageInfo.Artists)
            {
                string artistPath = Path.Combine(artistsBasePath, artist + ".json");
                if (System.IO.File.Exists(artistPath))
                {
                    string artistData = System.IO.File.ReadAllText(artistPath);
                    ArtistInfo? artistInfo = JsonSerializer.Deserialize<ArtistInfo>(artistData);
                    if (artistInfo == null) continue;
                    if (!IsNsfw)
                        artistInfo.PlatformInfo = artistInfo.PlatformInfo.Where(x => !x.Nsfw).ToArray();
                    imageInfo.ArtistsInfo.Add(artistInfo);
                }
            }
            return imageInfo;
        }

        protected string GenerateGalleryImage(string[] originalPath, string thumbDir)
        {
            if (!IsThumbnailStale(thumbDir, originalPath))
                return thumbDir.Replace(galleryThumbBasePath, "");
            if (System.IO.File.Exists(thumbDir)) System.IO.File.Delete(thumbDir);
            Directory.CreateDirectory(thumbDir.Replace(Path.GetFileName(thumbDir), ""));

            using var collection = new MagickImageCollection();
            foreach (var p in originalPath)
            {
                var image = new MagickImage(p);
                var resize = Math.Min(image.Width, image.Height);
                image.Resize(new MagickGeometry { Width = resize, Height = resize, FillArea = true });
                image.Crop(new MagickGeometry { Height = resize, Width = resize }, Gravity.Center);
                string? dir = Path.GetDirectoryName(p);
                if (dir != null && (dir.ToLower().EndsWith("nsfw") || dir.ToLower().EndsWith("kinky")))
                    image.Blur(25, 25);
                collection.Add(image);
            }

            using var result = collection.Montage(new MontageSettings
            {
                BackgroundColor = MagickColors.None,
                Geometry = new MagickGeometry(10, 10, 1000, 1000),
                TileGeometry = GetTileGeometry(originalPath.Length)
            });
            result.Write(thumbDir);
            return thumbDir.Replace(galleryThumbBasePath, "");
        }

        protected void GenerateFolderImage(string[] originalPath, string thumbPath, string thumbDir)
        {
            Directory.CreateDirectory(thumbDir);
            using var collection = new MagickImageCollection();
            foreach (var p in originalPath)
            {
                var image = new MagickImage(p);
                var resize = Math.Max(image.Width, image.Height);
                image.Resize(new MagickGeometry { Width = resize, Height = resize, FillArea = true });
                image.Crop(new MagickGeometry { Height = resize, Width = resize }, Gravity.Center);
                string? dir = Path.GetDirectoryName(p);
                if (dir != null && (dir.ToLower().EndsWith("nsfw") || dir.ToLower().EndsWith("kinky")))
                    image.Blur(25, 25);
                collection.Add(image);
            }

            using var result = collection.Montage(new MontageSettings
            {
                BackgroundColor = MagickColors.None,
                Geometry = new MagickGeometry(2, 2, 200, 200),
                TileGeometry = GetTileGeometry(originalPath.Length)
            });
            result.Write(thumbPath);
        }

        private static MagickGeometry GetTileGeometry(int count) =>
            ThumbnailService.GetTileGeometry(count);

        protected static void GenerateThumbnailVideo(string originalPath, string thumbPath, string thumbDir) =>
            ThumbnailService.GenerateThumbnailVideo(originalPath, thumbPath, thumbDir);

        protected static void GenerateThumbnailImage(string originalPath, string thumbPath, string thumbDir) =>
            ThumbnailService.GenerateThumbnailImage(originalPath, thumbPath, thumbDir);

        protected string GetDirectoryImage(string dir)
        {
            string file = GetFirstDirectoryImage(dir);
            string relativeDir = dir.Replace(artBasePath, "");
            if (relativeDir.StartsWith("\\")) relativeDir = relativeDir.Substring(1);
            string thumbPath = Path.Combine(folderThumbBasePath, relativeDir, Path.GetFileName(file));

            List<string> images = FindDirectoryImages(dir, 0).ToList();
            if (images.Count == 0) return "";

            if (IsThumbnailStale(thumbPath, images.ToArray()))
            {
                if (System.IO.File.Exists(thumbPath)) System.IO.File.Delete(thumbPath);
                GenerateFolderImage(images.ToArray(), thumbPath, thumbPath.Replace(thumbPath.Split("\\").Last(), ""));
            }
            return thumbPath;
        }

        protected string GetFirstDirectoryImage(string dir)
        {
            string? file = new DirectoryInfo(dir)
                .GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => Path.Combine(dir, f.Name))
                .Where(x => ImageExtensions.Contains(Path.GetExtension(x).ToUpper()))
                .FirstOrDefault();

            if (file != null) return file;

            string result = "";
            FileInfo? fileInfo = null;
            foreach (string directory in Directory.GetDirectories(dir))
            {
                string p = GetFirstDirectoryImage(directory);
                if (string.IsNullOrEmpty(p)) continue;
                FileInfo pathInfo = new FileInfo(p);
                if (fileInfo == null || fileInfo.LastWriteTime < pathInfo.LastWriteTime)
                {
                    fileInfo = pathInfo;
                    result = p;
                }
            }
            return result ?? "";
        }

        protected List<string> FindDirectoryImages(string dir, int depth, List<string>? images = null)
        {
            if (images == null) images = new List<string>();
            if (!Directory.Exists(dir)) return images;

            string[] files = new DirectoryInfo(Path.Combine(artBasePath, dir))
                .GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => Path.Combine(artBasePath, dir, f.Name))
                .Where(x => ImageExtensions.Contains(Path.GetExtension(x).ToUpper()) || VideoExtensions.Contains(Path.GetExtension(x).ToUpper()))
                .Where(x => IsNsfw || !ContainsNsfw(x))
                .ToArray();

            foreach (string file in files)
            {
                images.Add(file);
                if (images.Count >= 9) return images;
            }

            foreach (string directory in Directory.GetDirectories(dir))
            {
                if (!IsNsfw && ContainsNsfw(directory)) continue;
                images = FindDirectoryImages(directory, depth, images);
                if (images.Count >= 9) return images;
            }
            return images;
        }

        protected string RemoveFilePath(string path)
        {
            string filtered = path.Replace(artBasePath, "").Replace(thumbBasePath, "").Replace(ffmpegBasePath, "");
            if (filtered.StartsWith("\\")) filtered = filtered.Substring(1);
            return filtered;
        }

        public void DeleteThumbnailsForPath(string relativePath)
        {
            string galleryThumb = Path.Combine(galleryThumbBasePath, relativePath);
            string folderThumb = Path.Combine(folderThumbBasePath, relativePath);
            if (Directory.Exists(galleryThumb)) Directory.Delete(galleryThumb, true);
            if (Directory.Exists(folderThumb)) Directory.Delete(folderThumb, true);
            string thumb = Path.Combine(thumbBasePath, relativePath);
            if (System.IO.File.Exists(thumb)) System.IO.File.Delete(thumb);
            string ffmpegThumb = Path.Combine(ffmpegBasePath, relativePath);
            if (System.IO.File.Exists(ffmpegThumb)) System.IO.File.Delete(ffmpegThumb);
        }

        private static bool IsThumbnailStale(string thumbPath, string[] sourceFiles)
        {
            if (!System.IO.File.Exists(thumbPath)) return true;
            var thumbTime = System.IO.File.GetLastWriteTime(thumbPath);
            return sourceFiles.Any(f => System.IO.File.Exists(f) && System.IO.File.GetLastWriteTime(f) > thumbTime);
        }
    }
}
