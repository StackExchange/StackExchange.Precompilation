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

            ViewEngines.Engines.Clear();
#if !DEBUG
            // use precompiled engine first (supports some C# 6),
            ViewEngines.Engines.Add(new PrecompiledViewEngine(typeof(HomeController).Assembly));
#endif
            // fallback to RoslynRazorViewEngine (RazorViewEngine doesn't support C# 6).
            ViewEngines.Engines.Add(new RoslynRazorViewEngine());
        }
    }
}
