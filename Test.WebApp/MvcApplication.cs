using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using StackExchange.Precompilation;
using Test.WebApp.Controllers;

namespace Test.WebApp
{
    public static class MvcApplicationInitializer
    {
        public static void PreApplicationStart() =>
            System.Web.UI.PageParser.DefaultApplicationBaseType = typeof(MvcApplication);
    }

    public class MvcApplication : HttpApplication
    {
        public static bool IsDebug =>
#if DEBUG
            true;
#else
                false;
#endif

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
            ViewEngines.Engines.Add(new PrecompiledViewEngine(typeof(HomeController).Assembly, typeof(ExternalViews).Assembly));
#endif
            // fallback to RoslynRazorViewEngine (RazorViewEngine doesn't support C# 6).
            ViewEngines.Engines.Add(new RoslynRazorViewEngine() { UseCompilationModules = true });
        }
    }
}