namespace ChemSite.Models
{
    public class StaticPageViewModel
    {
        public string Path { get; set; } = string.Empty;
        public string[] PathParts { get; set; } = Array.Empty<string>();
        public IQueryCollection QueryParams { get; set; } = QueryCollection.Empty;
    }
}
