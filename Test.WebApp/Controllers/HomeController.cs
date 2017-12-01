using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using StackExchange.Precompilation;

namespace Test.WebApp.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            IEnumerable<string> viewPaths;
#if DEBUG
            viewPaths = new string[] { "We don't keep track of the views in the RoslynRazorViewEngine." };
#else
            var viewEngine = ViewEngines.Engines.OfType<PrecompiledViewEngine>().Single();
            viewPaths = viewEngine.ViewPaths;
#endif

            return View(new Models.SampleModel { ViewPaths = viewPaths });
        }

        public ActionResult IndexOverridden()
        {
            return new ViewResult
            {
                ViewName = "~/Views/Home/Index.cshtml",
                MasterName = "~/Views/Shared/_Layout.Overridden.cshtml",
                ViewData = new ViewDataDictionary(new Models.SampleModel { ViewPaths = new [] { "OVERRIDDDDDEN" } }),
            };
        }

        public ActionResult ExcludedLayout() => View();
    }
}