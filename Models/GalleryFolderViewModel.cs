namespace ChemSite.Models
{
    public class GalleryFolderViewModel
    {
        public List<GalleryFolderContent> Folders { get; set; } = new List<GalleryFolderContent>();
        public List<GalleryImageContent> Images { get; set; } = new List<GalleryImageContent>();
        public string[] PathParts { get; set; } = Array.Empty<string>();
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
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
    }
}
