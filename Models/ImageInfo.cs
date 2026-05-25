using System.Text.Json.Serialization;

namespace ChemSite.Models
{
    public class ImageInfo
    {
        /// <summary>
        /// Name of the image, shown on the folder and image
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// Description of the image, only shown on the detail page
        /// </summary>
        public string Description { get; set; } = "";
        /// <summary>
        /// The full filepath of the image
        /// </summary>
        public string Path { get; set; } = "";
        /// <summary>
        /// The date on which the art was made
        /// </summary>
        public DateTime CreationDate { get; set; }
        /// <summary>
        /// File paths of the alt versions of this image
        /// </summary>
        public string[] AltPaths { get; set; } = [];
        /// <summary>
        /// Info of the alt versions of this image
        /// </summary>
        public List<ImageInfo> AltInfo { get; set; } = new List<ImageInfo>();
        /// <summary>
        /// Artist names that made this image
        /// </summary>
        public string[] Artists { get; set; } = [];
        /// <summary>
        /// Info of the artists that made this image
        /// </summary>
        public List<ArtistInfo> ArtistsInfo { get; set; } = new List<ArtistInfo>();
        /// <summary>
        /// Info on the external links to this post
        /// </summary>
        public PlatformInfo[] ExternalLinks { get; set; } = [];
        public string[] Tags { get; set; } = [];
        public bool Pinned { get; set; } = false;
    }

    public class PlatformInfo
    {
        /// <summary>
        /// Name of the artist on this platform
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// Icon of the platform
        /// </summary>
        public string Icon { get; set; } = "";
        /// <summary>
        /// Link to the post / artist on the platform specified
        /// </summary>
        public string Link { get; set; } = "";
        /// <summary>
        /// Is this link NSFW
        /// </summary>
        public bool Nsfw { get; set; } = false;
    }

    public class ArtistInfo
    {
        /// <summary>
        /// Name of the artist
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// Filename key (not serialized) — populated at runtime from the JSON filename
        /// </summary>
        [JsonIgnore]
        public string FileName { get; set; } = "";
        /// <summary>
        /// Public link to a profile picture of the artist
        /// </summary>
        public string ProfilePicture { get; set; } = "";
        /// <summary>
        /// List of platforms that the artist is on
        /// </summary>
        public PlatformInfo[] PlatformInfo { get; set; } = [];
    }
}
