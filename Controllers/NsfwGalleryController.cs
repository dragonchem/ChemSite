using Microsoft.AspNetCore.Mvc;
using System.Runtime.Versioning;

namespace ChemSite.Controllers
{
    [SupportedOSPlatform("windows")]
    public class NsfwGalleryController : BaseGalleryController
    {
        protected override bool IsNsfw => true;
        protected override string GalleryPrefix => "nsfwgallery";

        public NsfwGalleryController(IWebHostEnvironment webHostEnvironment) : base(webHostEnvironment) { }

        public IActionResult Index(string path, int page = 1)
        {
            if (path == null) path = "";
            if (path.StartsWith("/")) path = path.Substring(1);
            if (path.EndsWith("/")) path = path.Substring(0, path.Length - 1);

            return IsFile(path) ? RenderImage(path) : RenderFolder(path, page);
        }
    }
}
