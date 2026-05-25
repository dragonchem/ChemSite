namespace ChemSite.Models
{
    public class GalleryImageViewModel
    {
        public string[] PathParts { get; set; } = Array.Empty<string>();
        public string Path { get; set; } = "";
        public GalleryImageContent Image { get; set; } = new GalleryImageContent();
        public bool IsNsfw { get; set; } = false;
        public string GalleryPrefix { get; set; } = "gallery";
        public bool IsAdmin { get; set; } = false;
    }
}
