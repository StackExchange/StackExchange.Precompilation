using System;
using System.Web.Mvc;
using System.Web.WebPages;

namespace StackExchange.Precompilation
{
    internal class PrecompilationView : IView
    {
        private readonly string _virtualPath;
        private readonly string _masterPath;
        private readonly Type _viewType;
        private readonly ProfiledVirtualPathProviderViewEngine _viewEngine;
        private readonly bool _runViewStart;

        public PrecompilationView(string virtualPath, string masterPath, Type viewType, bool runViewStart, ProfiledVirtualPathProviderViewEngine viewEngine)
        {
            _virtualPath = virtualPath;
            _masterPath = masterPath;
            _viewType = viewType;
            _runViewStart = runViewStart;
            _viewEngine = viewEngine;
        }

        private WebPageBase CreatePage(ViewContext viewContext, System.IO.TextWriter writer, out WebPageContext pageContext, out WebPageRenderingBase startPage)
        {
            var basePage = (WebPageBase)Activator.CreateInstance(_viewType);
            basePage.VirtualPath = _virtualPath;
            basePage.VirtualPathFactory = _viewEngine.VirtualPathFactory;

            pageContext = new WebPageContext(viewContext.HttpContext, basePage, viewContext.ViewData?.Model);

            startPage = _runViewStart
                ? StartPage.GetStartPage(basePage, "_ViewStart", _viewEngine.FileExtensions)
                : null;

            var viewPage = basePage as WebViewPage;
            if (viewPage != null)
            {
                if (!string.IsNullOrEmpty(_masterPath))
                {
                    Hacks.SetOverriddenLayoutPath(viewPage, _masterPath);
                }

                viewPage.ViewContext = viewContext;
                viewPage.ViewData = viewContext.ViewData;
                viewPage.InitHelpers();
            }

            return basePage;
        }

        public void Render(ViewContext viewContext, System.IO.TextWriter writer)
        {
            using (_viewEngine.DoProfileStep("Render"))
            {
                var webViewPage = CreatePage(viewContext, writer, out var pageContext, out var startPage);

                using (_viewEngine.DoProfileStep("ExecutePageHierarchy"))
                {
                    webViewPage.ExecutePageHierarchy(pageContext, writer, startPage);
                }
            }
        }
    }
}
