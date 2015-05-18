using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using StackExchange.Precompilation;
using Test.WebApp.Controllers;

namespace Test.WebApp
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalFilters.Filters.Add(new HandleErrorAttribute());

            RouteTable.Routes.IgnoreRoute("{resource}.axd/{*pathInfo}");
            RouteTable.Routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
                );

            var precompiledViewEngine = new PrecompiledViewEngine(typeof(HomeController).Assembly);
            precompiledViewEngine.ViewThunk += path =>
            {
                throw new Exception("Couldn't find precompiled view: " + path);
            };

            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(precompiledViewEngine);
        }
    }
}
