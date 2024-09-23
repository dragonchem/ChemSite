namespace ChemSite.Models
{
    public class GalleryImageViewModel
    {
        public string[] PathParts { get; set; } = Array.Empty<string>();
        public string Path { get; set; } = "";
        public GalleryImageContent Image { get; set; } = new GalleryImageContent();
    }
}
