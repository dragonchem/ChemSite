using ChemSite.Models;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.Versioning;
using System.Text.Json;

namespace ChemSite.Controllers
{
    [SupportedOSPlatform("windows")]
    public class SearchController : Controller
    {
        private static readonly List<string> ImageExtensions = new() { ".JPG", ".JPEG", ".JPE", ".BMP", ".GIF", ".PNG" };
        private static readonly List<string> VideoExtensions = new() { ".MP4", ".MOV", ".WEBM" };

        private readonly string artBasePath;
        private readonly string thumbBasePath;
        private readonly string ffmpegBasePath;
        private readonly string artistsBasePath;
        private readonly string tagsPath;

        public SearchController(IWebHostEnvironment env)
        {
            artBasePath = Path.Combine(env.WebRootPath, "art");
            thumbBasePath = Path.Combine(env.WebRootPath, "thumb");
            ffmpegBasePath = Path.Combine(env.WebRootPath, "ffmpeg");
            artistsBasePath = Path.Combine(env.WebRootPath, "artists");
            tagsPath = Path.Combine(env.WebRootPath, "tags.json");
        }

        public IActionResult GallerySearch(string? q, string? filteredtags, string? artist, string sort = "date", int page = 1)
        {
            var model = BuildSearch(q, filteredtags, artist, sort, page, isNsfw: false);
            ViewData["SearchUrl"] = "/gallery/search";
            return View("Index", model);
        }

        public IActionResult NsfwGallerySearch(string? q, string? filteredtags, string? artist, string sort = "date", int page = 1)
        {
            var model = BuildSearch(q, filteredtags, artist, sort, page, isNsfw: true);
            ViewData["SearchUrl"] = "/nsfwgallery/search";
            return View("Index", model);
        }

        private SearchViewModel BuildSearch(string? q, string? filteredtags, string? artist, string sort, int page, bool isNsfw)
        {
            var filteredTagSet = !string.IsNullOrEmpty(filteredtags)
                ? filteredtags.Split(',').Where(t => !string.IsNullOrWhiteSpace(t)).ToHashSet()
                : new HashSet<string>();

            var tagsConfig = System.IO.File.Exists(tagsPath)
                ? JsonSerializer.Deserialize<TagsConfig>(System.IO.File.ReadAllText(tagsPath)) ?? new TagsConfig()
                : new TagsConfig();

            var allArtists = Directory.GetFiles(artistsBasePath, "*.json")
                .Select(f =>
                {
                    string filename = Path.GetFileNameWithoutExtension(f);
                    try
                    {
                        var info = JsonSerializer.Deserialize<ArtistInfo>(System.IO.File.ReadAllText(f));
                        return new ArtistOption(filename, info?.Name ?? filename);
                    }
                    catch { return new ArtistOption(filename, filename); }
                })
                .OrderBy(a => a.Label)
                .ToList();

            var allImageFiles = Directory.GetFiles(artBasePath, "*.*", SearchOption.AllDirectories)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToUpper()) || VideoExtensions.Contains(Path.GetExtension(f).ToUpper()))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Track which image files are covered by a JSON so we can find uncovered ones later
            var coveredImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var jsonFiles = Directory.GetFiles(artBasePath, "*.json", SearchOption.AllDirectories);
            var results = new List<SearchResult>();

            foreach (var jsonFile in jsonFiles)
            {
                string relPath = jsonFile.Replace(artBasePath, "").TrimStart('\\').Replace("\\", "/");
                if (!isNsfw && (relPath.ToLower().Contains("nsfw") || relPath.ToLower().Contains("kinky")))
                    continue;

                ImageInfo? info;
                try { info = JsonSerializer.Deserialize<ImageInfo>(System.IO.File.ReadAllText(jsonFile)); }
                catch { continue; }
                if (info == null || string.IsNullOrEmpty(info.Path)) continue;

                string imagePath = info.Path;
                string fullImagePath = Path.Combine(artBasePath, imagePath.Replace("/", "\\"));
                if (!System.IO.File.Exists(fullImagePath)) continue;
                coveredImages.Add(fullImagePath);

                // Text filter
                if (!string.IsNullOrWhiteSpace(q))
                {
                    string ql = q.ToLower();
                    if (!info.Name.ToLower().Contains(ql) && !info.Description.ToLower().Contains(ql))
                        continue;
                }

                // Artist filter
                if (!string.IsNullOrWhiteSpace(artist) &&
                    !info.Artists.Any(a => string.Equals(a, artist, StringComparison.OrdinalIgnoreCase)))
                    continue;

                    // Tag filter: hide items that have a filtered-out tag; no-tag items always pass
                if (filteredTagSet.Count > 0 && info.Tags.Any(t => filteredTagSet.Contains(t))) continue;

                foreach (string artistId in info.Artists)
                {
                    string artistFilePath = Path.Combine(artistsBasePath, artistId + ".json");
                    if (System.IO.File.Exists(artistFilePath))
                    {
                        try
                        {
                            var artistInfo = JsonSerializer.Deserialize<ArtistInfo>(System.IO.File.ReadAllText(artistFilePath));
                            if (artistInfo != null) info.ArtistsInfo.Add(artistInfo);
                        }
                        catch { }
                    }
                }

                bool isVideo = VideoExtensions.Contains(Path.GetExtension(fullImagePath).ToUpper());
                string thumbPath = isVideo
                    ? fullImagePath.Replace(artBasePath, ffmpegBasePath) + ".png"
                    : fullImagePath.Replace(artBasePath, thumbBasePath);
                string thumbUrl = thumbPath.Replace(thumbBasePath, "").Replace(ffmpegBasePath, "").TrimStart('\\').Replace("\\", "/");

                DateTime creationDate = info.CreationDate != default
                    ? info.CreationDate
                    : new FileInfo(fullImagePath).CreationTime;

                results.Add(new SearchResult
                {
                    Path = imagePath,
                    ThumbUrl = thumbUrl,
                    Info = info,
                    IsImage = !isVideo,
                    GalleryPrefix = isNsfw ? "nsfwgallery" : "gallery",
                    CreationDate = creationDate
                });
            }

            // Second pass: image/video files with no JSON metadata
            foreach (var imageFile in allImageFiles)
            {
                if (coveredImages.Contains(imageFile)) continue;

                string relPath = imageFile.Replace(artBasePath, "").TrimStart('\\').Replace("\\", "/");
                if (!isNsfw && (relPath.ToLower().Contains("nsfw") || relPath.ToLower().Contains("kinky")))
                    continue;

                // Skip if artist filter is active — no artist info available
                if (!string.IsNullOrWhiteSpace(artist)) continue;

                // Tag filter: no tags, always passes filter-out style

                string filename = Path.GetFileNameWithoutExtension(imageFile);

                // Text filter on filename
                if (!string.IsNullOrWhiteSpace(q) && !filename.ToLower().Contains(q.ToLower()))
                    continue;

                bool isVideo = VideoExtensions.Contains(Path.GetExtension(imageFile).ToUpper());
                string thumbPath = isVideo
                    ? imageFile.Replace(artBasePath, ffmpegBasePath) + ".png"
                    : imageFile.Replace(artBasePath, thumbBasePath);
                string thumbUrl = thumbPath.Replace(thumbBasePath, "").Replace(ffmpegBasePath, "").TrimStart('\\').Replace("\\", "/");

                string imagePath = relPath;
                DateTime creationDate = new FileInfo(imageFile).CreationTime;

                results.Add(new SearchResult
                {
                    Path = imagePath,
                    ThumbUrl = thumbUrl,
                    Info = new ImageInfo { Name = filename, Path = imagePath },
                    IsImage = !isVideo,
                    GalleryPrefix = isNsfw ? "nsfwgallery" : "gallery",
                    CreationDate = creationDate
                });
            }

            results = sort switch
            {
                "oldest" => results.OrderBy(r => r.CreationDate).ToList(),
                "artist" => results.OrderBy(r => r.Info.Artists.FirstOrDefault() ?? "").ToList(),
                _ => results.OrderByDescending(r => r.CreationDate).ToList()
            };

            const int pageSize = 24;
            return new SearchViewModel
            {
                Results = results.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalResults = results.Count,
                Page = page,
                PageSize = pageSize,
                Query = q ?? "",
                SelectedTags = filteredTagSet,
                SelectedArtist = artist ?? "",
                Sort = sort,
                TagsConfig = tagsConfig,
                AllArtists = allArtists,
                IsNsfw = isNsfw
            };
        }
    }
}
