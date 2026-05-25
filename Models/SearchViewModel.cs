namespace ChemSite.Models
{
    public record ArtistOption(string Value, string Label);

    public class SearchViewModel
    {
        public List<SearchResult> Results { get; set; } = new();
        public int TotalResults { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 24;
        public string Query { get; set; } = "";
        public HashSet<string> SelectedTags { get; set; } = new();
        public string SelectedArtist { get; set; } = "";
        public string Sort { get; set; } = "date";
        public TagsConfig TagsConfig { get; set; } = new();
        public List<ArtistOption> AllArtists { get; set; } = new();
        public bool IsNsfw { get; set; }
    }

    public class SearchResult
    {
        public string Path { get; set; } = "";
        public string ThumbUrl { get; set; } = "";
        public ImageInfo Info { get; set; } = new();
        public bool IsImage { get; set; } = true;
        public string GalleryPrefix { get; set; } = "gallery";
        public DateTime CreationDate { get; set; }
    }
}
