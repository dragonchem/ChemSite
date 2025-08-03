using ChemSite.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using Microsoft.Extensions.Hosting.Internal;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Reflection;
using ImageMagick;
using System.Runtime.Versioning;
using static System.Net.Mime.MediaTypeNames;
using System.Text.Json;
using FFMpegCore;

namespace ChemSite.Controllers
{
    [SupportedOSPlatform("windows")]
    public class NsfwGalleryController : Controller
    {
        private readonly string artBasePath;
        private readonly string thumbBasePath;
        private readonly string ffmpegBasePath;
        private readonly string galleryThumbBasePath;
        private readonly string folderThumbBasePath;
        private readonly string artistsBasePath;
        private static readonly List<string> ImageExtensions = new List<string> { ".JPG", ".JPEG", ".JPE", ".BMP", ".GIF", ".PNG" };
        private static readonly List<string> VideoExtensions = new List<string> { ".MP4", ".MOV", ".WEBM" };
        private readonly IWebHostEnvironment _webHostEnvironment;

        public NsfwGalleryController(IWebHostEnvironment webHostEnvironment)
        {
            _webHostEnvironment = webHostEnvironment;
            artBasePath = Path.Combine(webHostEnvironment.WebRootPath, "art");
            thumbBasePath = Path.Combine(webHostEnvironment.WebRootPath, "thumb");
            ffmpegBasePath = Path.Combine(webHostEnvironment.WebRootPath, "ffmpeg");
            galleryThumbBasePath = Path.Combine(webHostEnvironment.WebRootPath, "thumb", "gallery");
            folderThumbBasePath = Path.Combine(webHostEnvironment.WebRootPath, "thumb", "folder");
            artistsBasePath = Path.Combine(webHostEnvironment.WebRootPath, "artists");
        }

        public IActionResult Index(string path)
        {
            if (path == null) path = "";

            if (path.StartsWith("/")) path = path.Substring(1);
            if (path.EndsWith("/")) path = path.Substring(0, path.Length - 1);

            if (IsFile(path))
            {
                return RenderImage(path);
            }
            else
            {
                return RenderFolder(path);
            }
        }

        private IActionResult RenderFolder(string path)
        {
            GalleryFolderViewModel folderViewModel = new GalleryFolderViewModel();

            folderViewModel.Path = path;
            folderViewModel.PathParts = path.Split('/');

            string fullPath = Path.Combine(artBasePath, path);

            if (!Directory.Exists(fullPath)) return NotFound();

            string[] directories = Directory.GetDirectories(Path.Combine(artBasePath, path));
            string[] files = new DirectoryInfo(Path.Combine(artBasePath, path))
                .GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => Path.Combine(artBasePath, path, f.Name))
                .Where(x =>
                    ImageExtensions.Contains(Path.GetExtension(x).ToUpper()) ||
                    VideoExtensions.Contains(Path.GetExtension(x).ToUpper())
                )
                .ToArray();

            var videoFiles = files.Where(x => VideoExtensions.Contains(Path.GetExtension(x).ToUpper())).ToList();
            for (int i = 0; i < videoFiles.Count; i++)
            {
                string file = videoFiles[i];

                string thumb = file.Replace(artBasePath, thumbBasePath) + ".png";
                string ffmpegPath = file.Replace(artBasePath, ffmpegBasePath) + ".png";

                if (!System.IO.File.Exists(ffmpegPath))
                {
                    GenerateThumbnailVideo(file, ffmpegPath, Path.GetDirectoryName(ffmpegPath)!);
                }

                videoFiles[i] = ffmpegPath;
                var index = files.ToList().FindIndex(x => x == file);
                files[index] = ffmpegPath;

                if (!System.IO.File.Exists(thumb))
                {
                    GenerateThumbnailImage(ffmpegPath, thumb, Path.GetDirectoryName(thumb)!);
                }
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

            foreach (var file in files)
            {
                string thumb = file.Replace(artBasePath, thumbBasePath);

                if (!System.IO.File.Exists(thumb))
                {
                    GenerateThumbnailImage(file, thumb, Path.GetDirectoryName(thumb)!);
                }

                ImageInfo? imageInfo = null;
                string imageInfoPath = Path.ChangeExtension(file, "json");
                if (System.IO.File.Exists(imageInfoPath))
                {
                    string fileData = System.IO.File.ReadAllText(imageInfoPath);
                    imageInfo = JsonSerializer.Deserialize<ImageInfo>(fileData);

                    if (imageInfo == null) continue;

                    foreach (string artist in imageInfo.Artists)
                    {
                        string artistPath = Path.Combine(artistsBasePath, artist + ".json");
                        if (System.IO.File.Exists(artistPath))
                        {
                            string artistData = System.IO.File.ReadAllText(artistPath);
                            ArtistInfo? artistInfo = JsonSerializer.Deserialize<ArtistInfo>(artistData);
                            if (artistInfo == null) continue;

                            imageInfo.ArtistsInfo.Add(artistInfo);
                        }
                    }
                }

                folderViewModel.Images.Add(new GalleryImageContent
                {
                    Title = file.Split('\\').Last(),
                    ImageUrl = RemoveFilePath(thumb),
                    Path = RemoveFilePath(file),
                    ImageInfo = imageInfo,
                    Image = VideoExtensions.FindIndex(x => file.ToUpper().Contains(x)) == -1
                });
            }

            folderViewModel.Images = folderViewModel.Images.OrderByDescending(x => x.ImageInfo?.CreationDate).ToList();

            var amount = files.Length;
            if (files.Length >= 9) amount = 9;
            var galleryFiles = files.Take(amount).ToList();

            if (amount < 9)
            {
                foreach (var directory in directories)
                {
                    if (directory.ToLower().Contains("nsfw") && !path.ToLower().Contains("nsfw")) continue;
                    var images = FindDirectoryImages(directory, 0);

                    foreach (var image in images)
                    {
                        galleryFiles.Add(image);
                        if (galleryFiles.Count >= 9)
                        {
                            break;
                        }
                    }
                }
            }

            var galleryfile = GetFirstDirectoryImage(Path.Combine(artBasePath, path));
            string relativeDir = path.Replace(artBasePath, "");
            if (relativeDir.StartsWith("\\")) relativeDir = relativeDir.Substring(1);
            folderViewModel.ImageUrl = GenerateGalleryImage(galleryFiles.Take(9).ToArray(), Path.Combine(galleryThumbBasePath, relativeDir, Path.GetFileName(galleryfile)));

            return View("Folder", folderViewModel);
        }

        private List<string> FindDirectoryImages(string dir, int depth, List<string>? images = null)
        {
            if (images == null) images = new List<string>();
            if (!Directory.Exists(dir)) return images;

            string[] files = new DirectoryInfo(Path.Combine(artBasePath, dir))
                .GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => Path.Combine(artBasePath, dir, f.Name))
                .Where(x =>
                    ImageExtensions.Contains(Path.GetExtension(x).ToUpper()) ||
                    VideoExtensions.Contains(Path.GetExtension(x).ToUpper())
                )
                .ToArray();
            foreach (string file in files)
            {
                images.Add(file);

                if (images.Count >= 9) return images;
            }

            string[] dirs = Directory.GetDirectories(dir);
            foreach (string directory in dirs)
            {
                if (directory.ToLower().Contains("nsfw") && !dir.ToLower().Contains("nsfw")) continue;
                images = FindDirectoryImages(directory, depth, images);

                if (images.Count >= 9) return images;
            }

            return images;
        }

        private string FindDirectoryImage(string dir, int depth)
        {
            if (depth > 5) return "";
            if (!Directory.Exists(dir)) return "";

            string[] files = new DirectoryInfo(Path.Combine(artBasePath, dir))
                .GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => Path.Combine(artBasePath, dir, f.Name))
                .Where(x => ImageExtensions.Contains(Path.GetExtension(x).ToUpper()))
                .ToArray();
            List<string> filteredFiles = files.Where(x => ImageExtensions.Contains(Path.GetExtension(x).ToUpper())).ToList();
            if (filteredFiles.Count > 0) return filteredFiles[0];

            string[] dirs = Directory.GetDirectories(dir);
            if (dirs.Length > 0) return FindDirectoryImage(dirs[0], depth + 1);

            return "";
        }

        private string GetDirectoryImage(string dir)
        {
            string file = GetFirstDirectoryImage(dir);
            string relativeDir = dir.Replace(artBasePath, "");
            if (relativeDir.StartsWith("\\")) relativeDir = relativeDir.Substring(1);
            string thumbPath = Path.Combine(folderThumbBasePath, relativeDir, Path.GetFileName(file));
            if (System.IO.File.Exists(thumbPath)) return thumbPath;
            List<string> images = FindDirectoryImages(dir, 0).ToList();
            if (images.Count == 0) return "";

            GenerateFolderImage(images.ToArray(), thumbPath, thumbPath.Replace(thumbPath.Split("\\").Last(), ""));
            return thumbPath;
        }

        private string GetFirstDirectoryImage(string dir)
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
                string path = GetFirstDirectoryImage(directory);
                FileInfo pathInfo = new FileInfo(path);

                if (fileInfo == null)
                {
                    fileInfo = new FileInfo(path);
                    result = path;
                    continue;
                }

                if (fileInfo.LastWriteTime < pathInfo.LastWriteTime)
                {
                    fileInfo = pathInfo;
                    result = path;
                }
            }

            if (result == null) result = "";
            return result;
        }

        private string GenerateGalleryImage(string[] originalPath, string thumbDir)
        {
            if (System.IO.File.Exists(thumbDir)) return thumbDir.Replace(galleryThumbBasePath, "");
            Directory.CreateDirectory(thumbDir.Replace(Path.GetFileName(thumbDir), ""));
            using (MagickImageCollection collection = new MagickImageCollection())
            {
                for (int i = 0; i < originalPath.Length; i++)
                {
                    MagickImage image = new MagickImage(originalPath[i]);

                    var resize = image.Width;
                    if (image.Height < resize) resize = image.Height;

                    image.Resize(new MagickGeometry
                    {
                        Width = resize,
                        Height = resize,
                        FillArea = true,
                    });
                    image.Crop(new MagickGeometry()
                    {
                        Height = resize,
                        Width = resize,
                    }, Gravity.Center);

                    string? dir = Path.GetDirectoryName(originalPath[i]);
                    collection.Add(image);
                }

                MagickGeometry tileGeometry;
                switch (originalPath.Length)
                {
                    case 1:
                        tileGeometry = new MagickGeometry(0, 0, 1, 1);
                        break;
                    case 2:
                        tileGeometry = new MagickGeometry(0, 0, 2, 1);
                        break;
                    case 3:
                        tileGeometry = new MagickGeometry(0, 0, 2, 2);
                        break;
                    case 4:
                        tileGeometry = new MagickGeometry(0, 0, 2, 2);
                        break;
                    case 5:
                        tileGeometry = new MagickGeometry(0, 0, 3, 2);
                        break;
                    case 6:
                        tileGeometry = new MagickGeometry(0, 0, 3, 2);
                        break;
                    case 7:
                        tileGeometry = new MagickGeometry(0, 0, 3, 3);
                        break;
                    case 8:
                        tileGeometry = new MagickGeometry(0, 0, 3, 3);
                        break;
                    default:
                        tileGeometry = new MagickGeometry(0, 0, 3, 3);
                        break;
                }

                using (IMagickImage result = collection.Montage(new MontageSettings
                {
                    BackgroundColor = MagickColors.None,
                    Geometry = new MagickGeometry(10, 10, 1000, 1000), // -geometry +5+5
                    TileGeometry = tileGeometry
                }))
                {
                    result.Write(thumbDir);
                    return thumbDir.Replace(galleryThumbBasePath, "");
                }
            }
        }

        private void GenerateFolderImage(string[] originalPath, string thumbPath, string thumbDir)
        {
            Directory.CreateDirectory(thumbDir);
            using (MagickImageCollection collection = new MagickImageCollection())
            {
                for (int i = 0; i < originalPath.Length; i++)
                {
                    MagickImage image = new MagickImage(originalPath[i]);

                    var resize = image.Width;
                    if (image.Height > resize) resize = image.Height;

                    image.Resize(new MagickGeometry
                    {
                        Width = resize,
                        Height = resize,
                        FillArea = true,
                    });
                    image.Crop(new MagickGeometry()
                    {
                        Height = resize,
                        Width = resize,
                    }, Gravity.Center);

                    string? dir = Path.GetDirectoryName(originalPath[i]);
                    if (dir != null && (dir.ToLower().EndsWith("nsfw") || dir.ToLower().EndsWith("kinky"))) image.Blur(25, 25);
                    collection.Add(image);
                }

                MagickGeometry tileGeometry;
                switch(originalPath.Length)
                {
                    case 1:
                        tileGeometry = new MagickGeometry(0, 0, 1, 1);
                        break;
                    case 2:
                        tileGeometry = new MagickGeometry(0, 0, 2, 1);
                        break;
                    case 3:
                        tileGeometry = new MagickGeometry(0, 0, 2, 2);
                        break;
                    case 4:
                        tileGeometry = new MagickGeometry(0, 0, 2, 2);
                        break;
                    case 5:
                        tileGeometry = new MagickGeometry(0, 0, 3, 2);
                        break;
                    case 6:
                        tileGeometry = new MagickGeometry(0, 0, 3, 2);
                        break;
                    case 7:
                        tileGeometry = new MagickGeometry(0, 0, 3, 3);
                        break;
                    case 8:
                        tileGeometry = new MagickGeometry(0, 0, 3, 3);
                        break;
                    default:
                        tileGeometry = new MagickGeometry(0, 0, 3, 3);
                        break;
                }

                using (IMagickImage result = collection.Montage(new MontageSettings
                {
                    BackgroundColor = MagickColors.None,
                    Geometry = new MagickGeometry(2, 2, 200, 200), // -geometry +5+5
                    TileGeometry = tileGeometry
                }))
                {
                    result.Write(thumbPath);
                }
            }
        }

        private void GenerateThumbnailVideo(string originalPath, string thumbPath, string thumbDir)
        {
            Directory.CreateDirectory(thumbDir);

            var mediaInfo = FFProbe.Analyse(originalPath);

            if (mediaInfo.PrimaryVideoStream == null) return;

            FFMpeg.Snapshot(originalPath, thumbPath, new Size(mediaInfo.PrimaryVideoStream.Width, mediaInfo.PrimaryVideoStream.Height), mediaInfo.Duration / 2);
        }

        private void GenerateThumbnailImage(string originalPath, string thumbPath, string thumbDir)
        {
            Directory.CreateDirectory(thumbDir);

            System.Drawing.Image image = ReadImageFromFile(originalPath);

            // Figure out the ratio
            double ratioX = (double)400 / (double)image.Width;
            double ratioY = (double)400 / (double)image.Height;
            // use whichever multiplier is smaller
            double ratio = ratioX < ratioY ? ratioX : ratioY;

            int newHeight = Convert.ToInt32(image.Height * ratio);
            int newWidth = Convert.ToInt32(image.Width * ratio);

            System.Drawing.Image thumb = image.GetThumbnailImage(newWidth, newHeight, () => false, IntPtr.Zero);
            thumb.Save(thumbPath);
            image.Dispose();
            thumb.Dispose();
        }

        private System.Drawing.Image ReadImageFromFile(string path)
        {
            using (FileStream i_Stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (Bitmap i_Bmp = new Bitmap(i_Stream))
                {
                    return new Bitmap(i_Bmp);
                }
            }
        }

        private string RemoveFilePath(string path)
        {
            string filtered = path.Replace(artBasePath, "").Replace(thumbBasePath, "").Replace(ffmpegBasePath, "");
            if (filtered.StartsWith("\\"))
            {
                filtered = filtered.Substring(1);
            }
            return filtered;
        }

        private IActionResult RenderImage(string path)
        {
            GalleryImageViewModel folderViewModel = new GalleryImageViewModel();

            folderViewModel.Path = path;
            folderViewModel.PathParts = path.Split('/');

            string file = Path.Combine(artBasePath, path).Replace("/", "\\");
            if (!System.IO.File.Exists(file))
            {
                return NotFound();
            }

            string thumb = file.Replace(artBasePath, thumbBasePath);

            if (!System.IO.File.Exists(thumb) && VideoExtensions.FindIndex(x => file.ToUpper().Contains(x)) == -1)
            {
                GenerateThumbnailImage(file, thumb, Path.GetDirectoryName(thumb)!);
            }

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

                            imageInfo.ArtistsInfo.Add(artistInfo);
                        }
                    }

                    foreach(string altPath in imageInfo.AltPaths)
                    {
                        string filePath = Path.Combine(artBasePath, Path.ChangeExtension(altPath, "json"));
                        if (System.IO.File.Exists(filePath))
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
                Image = VideoExtensions.FindIndex(x => file.ToUpper().Contains(x)) == -1
            };

            if (folderViewModel.Image.ImageInfo != null)
            {
                folderViewModel.PathParts[folderViewModel.PathParts.Length - 1] = folderViewModel.Image.ImageInfo.Name;
            }

            return View("Image", folderViewModel);
        }

        private bool IsFile(string path)
        {
            return System.IO.File.Exists(Path.Combine(artBasePath, path));
        }
    }
}
