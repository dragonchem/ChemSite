namespace ChemSite.Models
{
    public class TagsConfig
    {
        public List<TagSection> Sections { get; set; } = new();
    }

    public class TagSection
    {
        public string Name { get; set; } = "";
        public bool Nsfw { get; set; } = false;
        public List<string> Tags { get; set; } = new();
    }
}
