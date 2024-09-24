using ChemSite.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Diagnostics;
using Microsoft.Extensions.Hosting.Internal;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using static System.Net.Mime.MediaTypeNames;

namespace ChemSite.Controllers
{
    [SupportedOSPlatform("windows")]
    public class NsfwGalleryController : Controller
    {
        private readonly string artBasePath;
        private readonly string thumbBasePath;
        private static readonly List<string> ImageExtensions = new List<string> { ".JPG", ".JPEG", ".JPE", ".BMP", ".GIF", ".PNG" };
        private readonly IWebHostEnvironment _webHostEnvironment;

        public NsfwGalleryController(IWebHostEnvironment webHostEnvironment)
        {
            _webHostEnvironment = webHostEnvironment;
            artBasePath = Path.Combine(webHostEnvironment.WebRootPath, "art");
            thumbBasePath = Path.Combine(webHostEnvironment.WebRootPath, "thumb");
        }

        public IActionResult Index(string path)
        {
            if (path == null) path = "";
            if (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }

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
            string[] files = Directory.GetFiles(Path.Combine(artBasePath, path)).Where(x => ImageExtensions.Contains(Path.GetExtension(x).ToUpper())).ToArray();

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

                folderViewModel.Images.Add(new GalleryImageContent
                {
                    Title = file.Split('\\').Last(),
                    ImageUrl = RemoveFilePath(thumb),
                    Path = RemoveFilePath(file)
                });
            }

            return View("Folder", folderViewModel);
        }

        private string FindDirectoryImage(string dir, int depth)
        {
            if (depth > 5) return "";
            if (!Directory.Exists(dir)) return "";

            string[] files = Directory.GetFiles(dir);
            List<string> filteredFiles = files.Where(x => ImageExtensions.Contains(Path.GetExtension(x).ToUpper())).ToList();
            if (filteredFiles.Count > 0) return filteredFiles[0];

            string[] dirs = Directory.GetDirectories(dir);
            if (dirs.Length > 0) return FindDirectoryImage(dirs[0], depth + 1);

            return "";
        }

        private string GetDirectoryImage(string dir)
        {
            string image = RemoveFilePath(FindDirectoryImage(dir, 0));
            if (image == "") return "";

            string thumbPath = Path.Combine(thumbBasePath, image);
            if (!System.IO.File.Exists(thumbPath))
            {
                GenerateThumbnailImage(Path.Combine(artBasePath, image), Path.Combine(thumbBasePath, image), thumbPath.Replace(thumbPath.Split("\\").Last(), ""));
            }

            return thumbPath;
        }

        private void GenerateThumbnailImage(string originalPath, string thumbPath, string thumbDir)
        {
            Directory.CreateDirectory(thumbDir);

            System.Drawing.Image image = ReadImageFromFile(originalPath);

            // Figure out the ratio
            double ratioX = (double)200 / (double)image.Width;
            double ratioY = (double)200 / (double)image.Height;
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
            string filtered = path.Replace(artBasePath, "").Replace(thumbBasePath, "");
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

            if (!System.IO.File.Exists(thumb))
            {
                GenerateThumbnailImage(file, thumb, Path.GetDirectoryName(thumb)!);
            }

            folderViewModel.Image = new GalleryImageContent
            {
                Title = file.Split('\\').Last(),
                ImageUrl = RemoveFilePath(thumb),
                Path = RemoveFilePath(file)
            };

            return View("Image", folderViewModel);
        }

        private bool IsFile(string path)
        {
            return System.IO.File.Exists(Path.Combine(artBasePath, path));
        }
    }
}
