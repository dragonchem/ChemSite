namespace ChemSite.Models
{
    public class GalleryFolderViewModel
    {
        public List<GalleryFolderContent> Folders { get; set; } = new List<GalleryFolderContent>();
        public List<GalleryImageContent> Images { get; set; } = new List<GalleryImageContent>();
        public List<GalleryImageContent> PinnedImages { get; set; } = new List<GalleryImageContent>();
        public string[] PathParts { get; set; } = Array.Empty<string>();
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public bool IsNsfw { get; set; } = false;
        public string GalleryPrefix { get; set; } = "gallery";
        public bool IsAdmin { get; set; } = false;
        public int Page { get; set; } = 1;
        public int TotalImages { get; set; } = 0;
        public int PageSize { get; set; } = 24;
    }

    public class GalleryFolderContent
    {
        public string Title { get; set; } = "";
        public string Path { get; set; } = "";
        public string ImageUrl { get; set; } = "";
    }

    public class GalleryImageContent
    {
        public string Title { get; set; } = "";
        public string Path { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public ImageInfo? ImageInfo { get; set; } = null;
        public bool Image { get; set; } = true;
        public bool Pinned { get; set; } = false;
    }
}
