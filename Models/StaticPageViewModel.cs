namespace ChemSite.Models
{
    public class StaticPageViewModel
    {
        public string Path { get; set; } = string.Empty;
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Image { get; set; } = "";
        public string[] PathParts { get; set; } = Array.Empty<string>();
        public IQueryCollection QueryParams { get; set; } = QueryCollection.Empty;
    }
}
