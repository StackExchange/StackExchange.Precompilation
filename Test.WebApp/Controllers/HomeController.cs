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
            var viewEngine = ViewEngines.Engines.OfType<PrecompiledViewEngine>().Single();

            return View(new Models.SampleModel { ViewPaths = viewEngine.ViewPaths });
        }
    }
}