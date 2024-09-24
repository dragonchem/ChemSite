using ChemSite.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChemSite.Controllers
{
    public class StaticPageController : Controller
    {
        public IActionResult Index(string path)
        {
            StaticPageViewModel viewModel = new StaticPageViewModel();

            string viewPath = path;
            if (string.IsNullOrEmpty(path))
            {
                path = "";
                viewPath = "index";
            }

            viewModel.Path = path;
            viewModel.PathParts = path.Split("/");
            viewModel.QueryParams = Request.Query;

            return View(viewPath, viewModel);
        }
    }
}
