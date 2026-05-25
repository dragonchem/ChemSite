using ChemSite.Models;
using ChemSite.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;

namespace ChemSite.Controllers
{
    [SupportedOSPlatform("windows")]
    public class AdminController : Controller
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        private readonly IWebHostEnvironment _env;
        private readonly string artBasePath;
        private readonly string thumbBasePath;
        private readonly string ffmpegBasePath;
        private readonly string galleryThumbBasePath;
        private readonly string folderThumbBasePath;
        private readonly string artistsBasePath;
        private readonly string tagsPath;
        private readonly string themesPath;

        public AdminController(IWebHostEnvironment env)
        {
            _env = env;
            artBasePath = Path.Combine(env.WebRootPath, "art");
            thumbBasePath = Path.Combine(env.WebRootPath, "thumb");
            ffmpegBasePath = Path.Combine(env.WebRootPath, "ffmpeg");
            galleryThumbBasePath = Path.Combine(env.WebRootPath, "thumb", "gallery");
            folderThumbBasePath = Path.Combine(env.WebRootPath, "thumb", "folder");
            artistsBasePath = Path.Combine(env.WebRootPath, "artists");
            tagsPath = Path.Combine(env.WebRootPath, "tags.json");
            themesPath = Path.Combine(env.WebRootPath, "themes.json");
        }

        private bool IsAdmin()
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

        private IActionResult? Guard()
        {
            if (!IsAdmin()) return NotFound();
            return null;
        }

        private string GalleryPrefix()
        {
            string path = HttpContext.Request.Path.Value ?? "";
            return path.StartsWith("/nsfwgallery", StringComparison.OrdinalIgnoreCase) ? "nsfwgallery" : "gallery";
        }

        private TagsConfig LoadTags()
        {
            if (!System.IO.File.Exists(tagsPath)) return new TagsConfig();
            return JsonSerializer.Deserialize<TagsConfig>(System.IO.File.ReadAllText(tagsPath)) ?? new TagsConfig();
        }

        private void SaveTags(TagsConfig config) =>
            System.IO.File.WriteAllText(tagsPath, JsonSerializer.Serialize(config, JsonOpts));

        private List<string> GetAllArtistNames() =>
            Directory.GetFiles(artistsBasePath, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .OrderBy(n => n)
                .ToList();

        private string DeriveArtistFileName(string displayName)
        {
            var normalized = new string(displayName.ToLower()
                .Where(c => char.IsLetterOrDigit(c))
                .ToArray());
            if (string.IsNullOrEmpty(normalized)) normalized = "artist";
            string candidate = normalized;
            int i = 1;
            while (System.IO.File.Exists(Path.Combine(artistsBasePath, candidate + ".json")))
                candidate = $"{normalized}{i++}";
            return candidate;
        }

        private string GetSafeFilename(string dir, string filename)
        {
            string name = Path.GetFileNameWithoutExtension(filename);
            string ext = Path.GetExtension(filename);
            string candidate = filename;
            int i = 1;
            while (System.IO.File.Exists(Path.Combine(dir, candidate)))
                candidate = $"{name}_{i++}{ext}";
            return candidate;
        }

        private void DeleteFileThumbnails(string relativePath)
        {
            foreach (var basePath in new[] { thumbBasePath, ffmpegBasePath })
            {
                string p = Path.Combine(basePath, relativePath.Replace("/", "\\"));
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
                if (System.IO.File.Exists(p + ".png")) System.IO.File.Delete(p + ".png");
            }
        }

        private void DeleteFolderThumbnails(string relativePath)
        {
            foreach (var basePath in new[] { galleryThumbBasePath, folderThumbBasePath })
            {
                string p = Path.Combine(basePath, relativePath.Replace("/", "\\"));
                if (Directory.Exists(p)) Directory.Delete(p, true);
            }
        }

        // ─── UPLOAD ───────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Upload(string path = "")
        {
            var g = Guard(); if (g != null) return g;
            ViewBag.Path = path;
            ViewBag.Artists = GetAllArtistNames();
            ViewBag.Tags = LoadTags();
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file, string path, string name, string description,
            string creationDate, string[] artists, string[] tags, bool pinned,
            string[] externalLinkNames, string[] externalLinkIcons, string[] externalLinkUrls, string[] externalLinkNsfw)
        {
            var g = Guard(); if (g != null) return g;
            if (file == null || file.Length == 0) return BadRequest("No file");

            string targetDir = Path.Combine(artBasePath, path.Replace("/", "\\"));
            Directory.CreateDirectory(targetDir);

            string safeFilename = GetSafeFilename(targetDir, file.FileName);
            string filePath = Path.Combine(targetDir, safeFilename);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            var externalLinks = BuildExternalLinks(externalLinkNames, externalLinkIcons, externalLinkUrls, externalLinkNsfw);

            var info = new ImageInfo
            {
                Name = name,
                Description = description,
                Path = string.IsNullOrEmpty(path) ? safeFilename : $"{path}/{safeFilename}",
                CreationDate = DateTime.TryParse(creationDate, out var dt) ? dt : DateTime.Now,
                Artists = artists.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray(),
                Tags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray(),
                Pinned = pinned,
                ExternalLinks = externalLinks
            };

            System.IO.File.WriteAllText(Path.ChangeExtension(filePath, "json"), JsonSerializer.Serialize(info, JsonOpts));
            DeleteFolderThumbnails(path);

            return Redirect($"/{GalleryPrefix()}/{path}");
        }

        // ─── EDIT IMAGE/VIDEO ─────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Edit(string path)
        {
            var g = Guard(); if (g != null) return g;
            string file = Path.Combine(artBasePath, path.Replace("/", "\\"));
            if (!System.IO.File.Exists(file)) return NotFound();

            string jsonPath = Path.ChangeExtension(file, "json");
            ImageInfo info;
            if (System.IO.File.Exists(jsonPath))
            {
                info = JsonSerializer.Deserialize<ImageInfo>(System.IO.File.ReadAllText(jsonPath)) ?? new ImageInfo { Path = path };
            }
            else
            {
                info = new ImageInfo { Path = path, CreationDate = new FileInfo(file).CreationTime };
            }

            string folderPath = string.Join("/", path.Split('/').SkipLast(1));
            ViewBag.FilePath = path;
            ViewBag.FolderPath = folderPath;
            ViewBag.Info = info;
            ViewBag.Artists = GetAllArtistNames();
            ViewBag.Tags = LoadTags();
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View();
        }

        [HttpPost]
        public IActionResult Edit(string path, string name, string description,
            string creationDate, string[] artists, string[] tags, bool pinned, string[] altPaths,
            string[] externalLinkNames, string[] externalLinkIcons, string[] externalLinkUrls, string[] externalLinkNsfw)
        {
            var g = Guard(); if (g != null) return g;
            string file = Path.Combine(artBasePath, path.Replace("/", "\\"));
            if (!System.IO.File.Exists(file)) return NotFound();

            var info = new ImageInfo
            {
                Name = name,
                Description = description,
                Path = path,
                CreationDate = DateTime.TryParse(creationDate, out var dt) ? dt : DateTime.Now,
                Artists = artists.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray(),
                Tags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray(),
                Pinned = pinned,
                AltPaths = altPaths.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray(),
                ExternalLinks = BuildExternalLinks(externalLinkNames, externalLinkIcons, externalLinkUrls, externalLinkNsfw)
            };

            System.IO.File.WriteAllText(Path.ChangeExtension(file, "json"), JsonSerializer.Serialize(info, JsonOpts));
            return Redirect($"/{GalleryPrefix()}/{path}");
        }

        // ─── DELETE IMAGE/VIDEO ───────────────────────────────────────────────

        [HttpPost]
        public IActionResult Delete(string path)
        {
            var g = Guard(); if (g != null) return g;
            string file = Path.Combine(artBasePath, path.Replace("/", "\\"));
            if (!System.IO.File.Exists(file)) return NotFound();

            System.IO.File.Delete(file);
            string jsonPath = Path.ChangeExtension(file, "json");
            if (System.IO.File.Exists(jsonPath)) System.IO.File.Delete(jsonPath);
            DeleteFileThumbnails(path);

            string parentPath = string.Join("/", path.Split('/').SkipLast(1));
            return Redirect($"/{GalleryPrefix()}/{parentPath}");
        }

        // ─── FOLDER CRUD ──────────────────────────────────────────────────────

        [HttpPost]
        public IActionResult CreateFolder(string parentPath, string folderName)
        {
            var g = Guard(); if (g != null) return g;
            if (string.IsNullOrWhiteSpace(folderName)) return BadRequest();
            Directory.CreateDirectory(Path.Combine(artBasePath, parentPath.Replace("/", "\\"), folderName));
            string newPath = string.IsNullOrEmpty(parentPath) ? folderName : $"{parentPath}/{folderName}";
            return Redirect($"/{GalleryPrefix()}/{newPath}");
        }

        [HttpPost]
        public IActionResult RenameFolder(string path, string newName)
        {
            var g = Guard(); if (g != null) return g;
            if (string.IsNullOrWhiteSpace(newName)) return BadRequest();
            string oldFull = Path.Combine(artBasePath, path.Replace("/", "\\"));
            if (!Directory.Exists(oldFull)) return NotFound();

            string[] parts = path.Split('/');
            parts[parts.Length - 1] = newName;
            string newRelPath = string.Join("/", parts);
            Directory.Move(oldFull, Path.Combine(artBasePath, newRelPath.Replace("/", "\\")));

            // Move thumbnails to new path so they don't need regeneration
            foreach (var baseDir in new[] { thumbBasePath, galleryThumbBasePath, folderThumbBasePath })
            {
                string oldThumb = Path.Combine(baseDir, path.Replace("/", "\\"));
                string newThumb = Path.Combine(baseDir, newRelPath.Replace("/", "\\"));
                if (Directory.Exists(oldThumb))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newThumb)!);
                    Directory.Move(oldThumb, newThumb);
                }
            }

            return Redirect($"/{GalleryPrefix()}/{newRelPath}");
        }

        [HttpPost]
        public IActionResult DeleteFolder(string path)
        {
            var g = Guard(); if (g != null) return g;
            string fullPath = Path.Combine(artBasePath, path.Replace("/", "\\"));
            if (!Directory.Exists(fullPath)) return NotFound();
            Directory.Delete(fullPath, true);
            DeleteFolderThumbnails(path);
            string parentPath = string.Join("/", path.Split('/').SkipLast(1));
            return Redirect($"/{GalleryPrefix()}/{parentPath}");
        }

        // ─── ARTIST CRUD ──────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Artists()
        {
            var g = Guard(); if (g != null) return g;
            var artists = Directory.GetFiles(artistsBasePath, "*.json")
                .Select(f =>
                {
                    var info = JsonSerializer.Deserialize<ArtistInfo>(System.IO.File.ReadAllText(f))
                              ?? new ArtistInfo { Name = Path.GetFileNameWithoutExtension(f) };
                    info.FileName = Path.GetFileNameWithoutExtension(f);
                    return info;
                })
                .OrderBy(a => a.Name)
                .ToList();
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View(artists);
        }

        [HttpGet]
        public IActionResult ArtistCreate()
        {
            var g = Guard(); if (g != null) return g;
            ViewBag.ArtistFileName = "";
            ViewBag.HasFolder = false;
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View("ArtistForm", new ArtistInfo());
        }

        [HttpPost]
        public async Task<IActionResult> ArtistCreate(string displayName, IFormFile? profilePictureFile,
            string[] platformNames, string[] platformIcons, string[] platformLinks, string[] platformNsfw)
        {
            var g = Guard(); if (g != null) return g;
            if (string.IsNullOrWhiteSpace(displayName)) return BadRequest();
            string artistFileName = DeriveArtistFileName(displayName);
            string profilePicture = await SaveArtistPfp(profilePictureFile, artistFileName, "");
            var info = BuildArtistInfo(displayName, profilePicture, platformNames, platformIcons, platformLinks, platformNsfw);
            System.IO.File.WriteAllText(Path.Combine(artistsBasePath, artistFileName + ".json"), JsonSerializer.Serialize(info, JsonOpts));
            return Redirect($"/{GalleryPrefix()}/admin/artists");
        }

        [HttpGet]
        public IActionResult ArtistEdit(string artistName)
        {
            var g = Guard(); if (g != null) return g;
            string jsonPath = Path.Combine(artistsBasePath, artistName + ".json");
            if (!System.IO.File.Exists(jsonPath)) return NotFound();
            var info = JsonSerializer.Deserialize<ArtistInfo>(System.IO.File.ReadAllText(jsonPath)) ?? new ArtistInfo();
            info.FileName = artistName;
            ViewBag.ArtistFileName = artistName;
            ViewBag.HasFolder = Directory.Exists(Path.Combine(artBasePath, artistName));
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View("ArtistForm", info);
        }

        [HttpPost]
        public async Task<IActionResult> ArtistEdit(string artistFileName, string displayName, IFormFile? profilePictureFile,
            string[] platformNames, string[] platformIcons, string[] platformLinks, string[] platformNsfw)
        {
            var g = Guard(); if (g != null) return g;
            string jsonPath = Path.Combine(artistsBasePath, artistFileName + ".json");
            if (!System.IO.File.Exists(jsonPath)) return NotFound();
            var existing = JsonSerializer.Deserialize<ArtistInfo>(System.IO.File.ReadAllText(jsonPath)) ?? new ArtistInfo();
            string profilePicture = await SaveArtistPfp(profilePictureFile, artistFileName, existing.ProfilePicture);
            var info = BuildArtistInfo(displayName, profilePicture, platformNames, platformIcons, platformLinks, platformNsfw);
            System.IO.File.WriteAllText(jsonPath, JsonSerializer.Serialize(info, JsonOpts));
            return Redirect($"/{GalleryPrefix()}/admin/artists");
        }

        [HttpPost]
        public IActionResult ArtistDelete(string artistName)
        {
            var g = Guard(); if (g != null) return g;
            string jsonPath = Path.Combine(artistsBasePath, artistName + ".json");
            if (System.IO.File.Exists(jsonPath)) System.IO.File.Delete(jsonPath);
            return Redirect($"/{GalleryPrefix()}/admin/artists");
        }

        // ─── TAG CRUD ─────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Tags()
        {
            var g = Guard(); if (g != null) return g;
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View(LoadTags());
        }

        [HttpPost]
        public IActionResult TagSectionCreate(string sectionName, bool nsfw)
        {
            var g = Guard(); if (g != null) return g;
            if (string.IsNullOrWhiteSpace(sectionName)) return BadRequest();
            var config = LoadTags();
            if (!config.Sections.Any(s => s.Name == sectionName))
                config.Sections.Add(new TagSection { Name = sectionName, Nsfw = nsfw });
            SaveTags(config);
            return Redirect($"/{GalleryPrefix()}/admin/tags");
        }

        [HttpPost]
        public IActionResult TagSectionDelete(string sectionName)
        {
            var g = Guard(); if (g != null) return g;
            var config = LoadTags();
            config.Sections.RemoveAll(s => s.Name == sectionName);
            SaveTags(config);
            return Redirect($"/{GalleryPrefix()}/admin/tags");
        }

        [HttpPost]
        public IActionResult TagAdd(string sectionName, string tagName)
        {
            var g = Guard(); if (g != null) return g;
            if (string.IsNullOrWhiteSpace(tagName)) return BadRequest();
            var config = LoadTags();
            var section = config.Sections.FirstOrDefault(s => s.Name == sectionName);
            if (section != null && !section.Tags.Contains(tagName))
                section.Tags.Add(tagName);
            SaveTags(config);
            return Redirect($"/{GalleryPrefix()}/admin/tags");
        }

        [HttpPost]
        public IActionResult TagRemove(string sectionName, string tagName)
        {
            var g = Guard(); if (g != null) return g;
            var config = LoadTags();
            config.Sections.FirstOrDefault(s => s.Name == sectionName)?.Tags.Remove(tagName);
            SaveTags(config);
            return Redirect($"/{GalleryPrefix()}/admin/tags");
        }

        // ─── BROWSE (for gallery picker) ─────────────────────────────────────

        [HttpGet]
        public IActionResult BrowseFolder(string path = "")
        {
            var g = Guard(); if (g != null) return g;
            string fullPath = Path.Combine(artBasePath, path.Replace("/", "\\"));
            if (!Directory.Exists(fullPath)) return NotFound();

            var folders = Directory.GetDirectories(fullPath)
                .Select(d => new { name = Path.GetFileName(d), path = (string.IsNullOrEmpty(path) ? Path.GetFileName(d) : $"{path}/{Path.GetFileName(d)}") })
                .ToList();

            var extensions = ThumbnailService.ImageExtensions.Concat(ThumbnailService.VideoExtensions).ToList();
            var images = new DirectoryInfo(fullPath).GetFiles()
                .OrderByDescending(f => f.LastWriteTime)
                .Where(f => extensions.Contains(Path.GetExtension(f.Name).ToUpper()))
                .Select(f =>
                {
                    string relPath = string.IsNullOrEmpty(path) ? f.Name : $"{path}/{f.Name}";
                    string thumbRel = relPath.Replace("/", "\\");
                    string thumbPath = Path.Combine(_env.WebRootPath, "thumb", thumbRel);
                    bool hasThumb = System.IO.File.Exists(thumbPath);
                    string jsonPath = Path.Combine(artBasePath, path.Replace("/", "\\"), Path.ChangeExtension(f.Name, "json"));
                    string title = f.Name;
                    if (System.IO.File.Exists(jsonPath))
                    {
                        var info = JsonSerializer.Deserialize<ImageInfo>(System.IO.File.ReadAllText(jsonPath));
                        if (!string.IsNullOrEmpty(info?.Name)) title = info.Name;
                    }
                    return new { name = title, path = relPath, thumb = hasThumb ? $"/thumb/{thumbRel.Replace("\\", "/")}" : "" };
                })
                .ToList();

            return Json(new { folders, images });
        }

        // ─── THEME CRUD ───────────────────────────────────────────────────────

        private ThemesConfig LoadThemes()
        {
            if (!System.IO.File.Exists(themesPath)) return new ThemesConfig();
            return JsonSerializer.Deserialize<ThemesConfig>(System.IO.File.ReadAllText(themesPath)) ?? new ThemesConfig();
        }

        private void SaveThemes(ThemesConfig config) =>
            System.IO.File.WriteAllText(themesPath, JsonSerializer.Serialize(config, JsonOpts));

        [HttpGet]
        public IActionResult Themes()
        {
            var g = Guard(); if (g != null) return g;
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View(LoadThemes());
        }

        [HttpPost]
        public IActionResult ThemeSet(FolderTheme theme, string? returnUrl = null)
        {
            var g = Guard(); if (g != null) return g;
            theme.Folder ??= "";
            var config = LoadThemes();
            var existing = config.Themes.FirstOrDefault(t => string.Equals(t.Folder, theme.Folder, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.PageBackground = theme.PageBackground;
                existing.Section = theme.Section;
                existing.Primary = theme.Primary;
                existing.Secondary = theme.Secondary;
                existing.Accent = theme.Accent;
                existing.Background = theme.Background;
                existing.Surface = theme.Surface;
                existing.Link = theme.Link;
            }
            else
            {
                config.Themes.Add(theme);
            }
            SaveThemes(config);
            if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
            return Redirect($"/{GalleryPrefix()}/admin/themes");
        }

        [HttpPost]
        public IActionResult ThemeDelete(string folder, string? returnUrl = null)
        {
            var g = Guard(); if (g != null) return g;
            if (string.IsNullOrEmpty(folder)) return BadRequest("Cannot delete the root theme.");
            var config = LoadThemes();
            config.Themes.RemoveAll(t => string.Equals(t.Folder, folder, StringComparison.OrdinalIgnoreCase));
            SaveThemes(config);
            if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
            return Redirect($"/{GalleryPrefix()}/admin/themes");
        }

        [HttpGet]
        public IActionResult ThemeEditFolder(string folderPath = "")
        {
            var g = Guard(); if (g != null) return g;
            var config = LoadThemes();
            var existing = config.Themes.FirstOrDefault(t => string.Equals(t.Folder, folderPath, StringComparison.OrdinalIgnoreCase));
            var theme = existing ?? new FolderTheme { Folder = folderPath };
            ViewBag.HasTheme = existing != null;
            ViewBag.ReturnUrl = $"/{GalleryPrefix()}/{folderPath}";
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View("ThemeFolder", theme);
        }

        [HttpGet]
        public IActionResult SuggestThemeColors(string imagePath)
        {
            var g = Guard(); if (g != null) return g;
            var fullPath = Path.GetFullPath(Path.Combine(artBasePath, imagePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(artBasePath, StringComparison.OrdinalIgnoreCase))
                return BadRequest("Invalid path");
            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            using var image = new ImageMagick.MagickImage(fullPath);
            image.Resize(new ImageMagick.MagickGeometry(150, 150) { IgnoreAspectRatio = false });
            image.BackgroundColor = ImageMagick.MagickColors.White;
            image.Alpha(ImageMagick.AlphaOption.Remove);
            image.Quantize(new ImageMagick.QuantizeSettings
            {
                Colors = 16,
                ColorSpace = ImageMagick.ColorSpace.sRGB,
                DitherMethod = ImageMagick.DitherMethod.No
            });

            var colors = image.Histogram()
                .OrderByDescending(kv => kv.Value)
                .Take(16)
                .Select(kv => kv.Key)
                .Select(c =>
                {
                    double r = c.R / 65535.0, gr = c.G / 65535.0, b = c.B / 65535.0;
                    double max = Math.Max(r, Math.Max(gr, b));
                    double min = Math.Min(r, Math.Min(gr, b));
                    double delta = max - min;
                    double l = (max + min) / 2.0;
                    double s = delta < 0.001 ? 0 : delta / (1 - Math.Abs(2 * l - 1));
                    return new { hex = $"#{(int)(r * 255):X2}{(int)(gr * 255):X2}{(int)(b * 255):X2}", s, l };
                })
                .ToList();

            var byLightness = colors.OrderBy(c => c.l).ToList();
            var bySaturation = colors.Where(c => c.l is > 0.05 and < 0.95)
                                     .OrderByDescending(c => c.s).ToList();

            string background = byLightness.First().hex;
            string text       = byLightness.Last().hex;
            string primary    = bySaturation.FirstOrDefault()?.hex ?? "#888888";
            string secondary  = bySaturation.Skip(1).FirstOrDefault()?.hex ?? primary;
            string accent     = bySaturation.Skip(2).FirstOrDefault()?.hex ?? primary;

            string DarkenHex(string hex, double factor) {
                hex = hex.TrimStart('#');
                if (hex.Length != 6) return "#" + hex;
                int r2 = (int)(Convert.ToInt32(hex[0..2], 16) * factor);
                int g2 = (int)(Convert.ToInt32(hex[2..4], 16) * factor);
                int b2 = (int)(Convert.ToInt32(hex[4..6], 16) * factor);
                return $"#{r2:X2}{g2:X2}{b2:X2}";
            }
            string LightenHex(string hex, double amount) {
                hex = hex.TrimStart('#');
                if (hex.Length != 6) return "#" + hex;
                int r2 = Math.Min(255, (int)(Convert.ToInt32(hex[0..2], 16) + 255 * amount));
                int g2 = Math.Min(255, (int)(Convert.ToInt32(hex[2..4], 16) + 255 * amount));
                int b2 = Math.Min(255, (int)(Convert.ToInt32(hex[4..6], 16) + 255 * amount));
                return $"#{r2:X2}{g2:X2}{b2:X2}";
            }
            string pageBackground = background;
            string container      = LightenHex(background, 0.10);
            string section        = LightenHex(background, 0.20);
            string surface        = LightenHex(background, 0.30);
            background            = container;
            return Json(new { pageBackground, primary, secondary, accent, background, section, surface, link = primary });
        }

        // ─── REBUILD THUMBNAILS ───────────────────────────────────────────────

        [HttpPost]
        public IActionResult RebuildThumbnails()
        {
            var g = Guard(); if (g != null) return g;
            if (!RebuildState.IsRunning)
                Task.Run(() => ThumbnailService.RebuildAll(
                    artBasePath, thumbBasePath, ffmpegBasePath,
                    galleryThumbBasePath, folderThumbBasePath));
            return Redirect($"/{GalleryPrefix()}/admin/rebuild-thumbnails/progress");
        }

        [HttpGet]
        public IActionResult RebuildProgress()
        {
            var g = Guard(); if (g != null) return g;
            ViewBag.GalleryPrefix = GalleryPrefix();
            return View();
        }

        [HttpGet]
        public IActionResult RebuildStatus()
        {
            var g = Guard(); if (g != null) return g;
            return Json(new
            {
                isRunning = RebuildState.IsRunning,
                isComplete = RebuildState.IsComplete,
                total = RebuildState.Total,
                done = RebuildState.Done,
                currentFile = RebuildState.CurrentFile,
                error = RebuildState.Error
            });
        }

        [HttpGet]
        public IActionResult ArtistsList()
        {
            var g = Guard(); if (g != null) return g;
            return Json(GetAllArtistNames());
        }

        // ─── HELPERS ─────────────────────────────────────────────────────────

        private async Task<string> SaveArtistPfp(IFormFile? file, string artistFileName, string existing)
        {
            if (file == null || file.Length == 0) return existing;
            string ext = Path.GetExtension(file.FileName).ToLower();
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            string filename = artistFileName + ext;
            string destPath = Path.Combine(artistsBasePath, filename);
            using var stream = new FileStream(destPath, FileMode.Create);
            await file.CopyToAsync(stream);
            return $"/artists/{filename}";
        }

        private static PlatformInfo[] BuildExternalLinks(string[] names, string[] icons, string[] urls, string[] nsfwFlags)
        {
            var list = new List<PlatformInfo>();
            for (int i = 0; i < names.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(names[i]))
                    list.Add(new PlatformInfo
                    {
                        Name = names[i],
                        Icon = i < icons.Length ? icons[i] : "",
                        Link = i < urls.Length ? urls[i] : "",
                        Nsfw = i < nsfwFlags.Length && nsfwFlags[i] == "true"
                    });
            }
            return list.ToArray();
        }

        private static ArtistInfo BuildArtistInfo(string displayName, string profilePicture,
            string[] names, string[] icons, string[] links, string[] nsfwFlags)
        {
            var platforms = new List<PlatformInfo>();
            for (int i = 0; i < names.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(names[i]))
                    platforms.Add(new PlatformInfo
                    {
                        Name = names[i],
                        Icon = i < icons.Length ? icons[i] : "",
                        Link = i < links.Length ? links[i] : "",
                        Nsfw = i < nsfwFlags.Length && nsfwFlags[i] == "true"
                    });
            }
            return new ArtistInfo { Name = displayName, ProfilePicture = profilePicture, PlatformInfo = platforms.ToArray() };
        }
    }
}
