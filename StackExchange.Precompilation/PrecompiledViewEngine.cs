using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.WebPages;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// Supports loading of precompiled views.
    /// </summary>
    public class PrecompiledViewEngine : VirtualPathProviderViewEngine
    {
        private class PrecompiledVirtualPathFactory : IVirtualPathFactory
        {
            private Func<string, Type> PathLookup;
            private IVirtualPathFactory Backup;

            public PrecompiledVirtualPathFactory(Func<string, Type> pathLookup, IVirtualPathFactory backup)
            {
                PathLookup = pathLookup;
                Backup = backup;
            }

            public object CreateInstance(string virtualPath)
            {
                var compiledType = PathLookup(virtualPath);
                if (compiledType != null)
                {
                    return Activator.CreateInstance(compiledType);
                }

                return Backup.CreateInstance(virtualPath);
            }

            public bool Exists(string virtualPath)
            {
                if (PathLookup(virtualPath) != null) return true;

                return Backup.Exists(virtualPath);
            }
        }

        private class PrecompiledView : IView
        {
            private static readonly Action<WebViewPage, string> LayoutSetter;

            static PrecompiledView()
            {
                var property = typeof(WebViewPage).GetProperty("OverridenLayoutPath", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (property == null)
                {
                    throw new Exception("Couldn't generate LayoutSetter, no OverridenLayoutPath property");
                }

                var setter = property.GetSetMethod(nonPublic: true);
                if (setter == null)
                {
                    throw new Exception("Couldn't generate LayoutSetter, no OverridenLayoutPath setter");
                }

                LayoutSetter = (Action<WebViewPage, string>)Delegate.CreateDelegate(typeof(Action<WebViewPage, string>), setter, throwOnBindFailure: true);
            }

            private readonly string Path;
            private readonly string MasterPath;
            private readonly Type Compiled;
            private readonly Func<WebViewPage, string, WebPageRenderingBase> StartPageGetter;
            private readonly Func<string, Type> CompiledTypeLookup;
            private readonly PrecompiledViewEngine ViewEngine;

            public PrecompiledView(string path, string masterPath, Type compiled, Func<string, Type> compiledTypeLookup, Func<WebViewPage, string, WebPageRenderingBase> startPageGetter, PrecompiledViewEngine viewEngine)
            {
                Path = path;
                MasterPath = masterPath;
                Compiled = compiled;
                CompiledTypeLookup = compiledTypeLookup;
                StartPageGetter = startPageGetter;
                ViewEngine = viewEngine;
            }

            private WebViewPage CreatePage(ViewContext viewContext, System.IO.TextWriter writer, out WebPageContext pageContext, out WebPageRenderingBase startPage)
            {
                var webViewPage = (WebViewPage)Activator.CreateInstance(Compiled);

                if (!string.IsNullOrEmpty(MasterPath))
                {
                    LayoutSetter(webViewPage, MasterPath);
                }

                webViewPage.VirtualPath = Path;
                webViewPage.ViewContext = viewContext;
                webViewPage.ViewData = viewContext.ViewData;
                webViewPage.InitHelpers();

                pageContext = new WebPageContext(viewContext.HttpContext, webViewPage, null);

                startPage = null;
                if (StartPageGetter != null)
                {
                    startPage = StartPageGetter(webViewPage, "_ViewStart");
                }

                var asExecuting = webViewPage as WebPageExecutingBase;

                if (asExecuting != null)
                {
                    asExecuting.VirtualPathFactory = new PrecompiledVirtualPathFactory(CompiledTypeLookup, asExecuting.VirtualPathFactory);
                }

                return webViewPage;
            }

            public void Render(ViewContext viewContext, System.IO.TextWriter writer)
            {
                using (ViewEngine.DoProfileStep("Render"))
                {
                    WebPageContext pageContext;
                    WebPageRenderingBase startPage;
                    var webViewPage = CreatePage(viewContext, writer, out pageContext, out startPage);

                    var asExecuting = webViewPage as WebPageExecutingBase;

                    if (asExecuting != null)
                    {
                        asExecuting.VirtualPathFactory = new PrecompiledVirtualPathFactory(CompiledTypeLookup, asExecuting.VirtualPathFactory);
                    }

                    using (ViewEngine.DoProfileStep("ExecutePageHierarchy"))
                    {
                        webViewPage.ExecutePageHierarchy(pageContext, writer, startPage);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the view paths
        /// </summary>
        public IEnumerable<string> ViewPaths { get; private set; }

        private readonly Dictionary<string, Type> _views;

        /// <summary>
        /// Triggers when the engine performs a step that can be profiled.
        /// </summary>
        public Func<string, IDisposable> ProfileStep { get; set; }

        /// <summary>
        /// Creates a new PrecompiledViewEngine instance, scanning all assemblies in <paramref name="findAssembliesInPath"/> for precompiled views.
        /// Precompiled views are types deriving from <see cref="WebPageRenderingBase"/> decorated with a <see cref="CompiledFromFileAttribute" />
        /// </summary>
        /// <param name="findAssembliesInPath">The path to scan for assemblies with precompiled views.</param>
        /// <remarks>
        /// Use this constructor if you use aspnet_compiler.exe with it's targetDir parameter instead of StackExchange.Precompilation.Build.
        /// </remarks>
        public PrecompiledViewEngine(string findAssembliesInPath)
            : this(FindViewAssemblies(findAssembliesInPath).ToArray())
        {
        }

        /// <summary>
        /// Creates a new PrecompiledViewEngine instance, scanning the provided <paramref name="assemblies"/> for precompiled views.
        /// Precompiled views are types deriving from <see cref="WebPageRenderingBase"/> decorated with a <see cref="CompiledFromFileAttribute" />
        /// </summary>
        /// <param name="assemblies">The assemblies to scan for precompiled views.</param>
        public PrecompiledViewEngine(params Assembly[] assemblies)
        {
            AreaViewLocationFormats = new[]
            {
                "~/Areas/{2}/Views/{1}/{0}.cshtml",
                "~/Areas/{2}/Views/Shared/{0}.cshtml",
            };
            AreaMasterLocationFormats = new[]
            {
                "~/Areas/{2}/Views/{1}/{0}.cshtml",
                "~/Areas/{2}/Views/Shared/{0}.cshtml",
            };
            AreaPartialViewLocationFormats = new[]
            {
                "~/Areas/{2}/Views/{1}/{0}.cshtml",
                "~/Areas/{2}/Views/Shared/{0}.cshtml",
            };
            FileExtensions = new[]
            {
                "cshtml",
            };
            MasterLocationFormats = new[]
            {
                "~/Views/{1}/{0}.cshtml",
                "~/Views/Shared/{0}.cshtml",
            };
            PartialViewLocationFormats = new[]
            {
                "~/Views/{1}/{0}.cshtml",
                "~/Views/Shared/{0}.cshtml",
            };
            ViewLocationFormats = new[]
            {
                "~/Views/{1}/{0}.cshtml",
                "~/Views/Shared/{0}.cshtml",
            };

            _views = new Dictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var asm in assemblies)
            {
                List<Type> viewTypes;
                try
                {
                    viewTypes = asm.GetTypes().Where(t => typeof(WebPageRenderingBase).IsAssignableFrom(t)).ToList();
                }
                catch (Exception e)
                {
                    throw e;
                }

                foreach (var view in viewTypes)
                {
                    var attr = view.GetCustomAttribute<CompiledFromFileAttribute>();
                    if (attr != null)
                    {
                        _views[MakeVirtualPath(attr.SourceFile)] = view;
                    }
                }
            }

            ViewPaths = _views.Keys.OrderBy(_ => _).ToList();
        }

        private IDisposable DoProfileStep(string name)
        {
            return ProfileStep?.Invoke(name);
        }

        private static string MakeVirtualPath(string absolutePath)
        {
            var firstArea = absolutePath.IndexOf(@"\Areas\");

            if (firstArea != -1)
            {
                var tail = absolutePath.Substring(firstArea);
                var vp = "~" + tail.Replace(@"\", "/");

                return vp;
            }
            else
            {
                var firstView = absolutePath.IndexOf(@"\Views\");
                if (firstView == -1) throw new Exception("Couldn't convert: " + absolutePath);

                var tail = absolutePath.Substring(firstView);
                var vp = "~" + tail.Replace(@"\", "/");

                return vp;
            }
        }

        private static List<Assembly> FindViewAssemblies(string dirPath)
        {
            var pendingDirs = new List<string>();
            pendingDirs.Add(dirPath);

            var ret = new List<Assembly>();

            while (pendingDirs.Count > 0)
            {
                var dir = pendingDirs[0];
                pendingDirs.RemoveAt(0);

                pendingDirs.AddRange(Directory.EnumerateDirectories(dir));

                var dlls = Directory.EnumerateFiles(dir, "*.dll").Where(w => Path.GetFileNameWithoutExtension(w).Contains("_Web_"));

                foreach (var dll in dlls)
                {
                    try
                    {
                        var pdb = Path.Combine(Path.GetDirectoryName(dll), Path.GetFileNameWithoutExtension(dll) + ".pdb");

                        var asmBytes = File.ReadAllBytes(dll);
                        var pdbBytes = File.Exists(pdb) ? File.ReadAllBytes(pdb) : null;

                        Assembly asm;

                        if (pdbBytes == null)
                        {
                            asm = Assembly.Load(asmBytes);
                        }
                        else
                        {
                            asm = Assembly.Load(asmBytes, pdbBytes);
                        }

                        ret.Add(asm);

                        Debug.WriteLine("Loading view assembly: " + dll);
                    }
                    catch (Exception) { }
                }
            }

            return ret;
        }

        /// <inheritdoc />
        protected override IView CreatePartialView(ControllerContext controllerContext, string partialPath) =>
            CreateViewImpl(partialPath, masterPath: null, runViewStart: false);

        /// <inheritdoc />
        protected override IView CreateView(ControllerContext controllerContext, string viewPath, string masterPath) =>
            CreateViewImpl(viewPath, masterPath, runViewStart: true);


        private Type TryLookupCompiledType(string viewPath)
        {
            Type compiledView;
            if (!_views.TryGetValue(viewPath, out compiledView))
            {
                return null;
            }

            return compiledView;
        }

        private PrecompiledView CreateViewImpl(string viewPath, string masterPath, bool runViewStart)
        {
            Type compiledView;
            if (!_views.TryGetValue(viewPath, out compiledView))
            {
                return null;
            }

            return runViewStart
                ? new PrecompiledView(viewPath, masterPath, compiledView, TryLookupCompiledType, GetStartPage, this)
                : new PrecompiledView(viewPath, masterPath, compiledView, TryLookupCompiledType, null, this);
        }

        private WebPageRenderingBase GetStartPage(WebPageRenderingBase page, string fileName)
        {
            WebPageRenderingBase currentPage = page;
            var pageDirectory = VirtualPathUtility.GetDirectory(page.VirtualPath);

            while (!string.IsNullOrEmpty(pageDirectory) && pageDirectory != "/")
            {
                var virtualPath = VirtualPathUtility.Combine(pageDirectory, fileName + ".cshtml");

                Type compiledType;
                if (_views.TryGetValue(virtualPath, out compiledType))
                {
                    var parentStartPage = (StartPage)Activator.CreateInstance(compiledType);
                    parentStartPage.VirtualPath = virtualPath;
                    parentStartPage.ChildPage = currentPage;
                    parentStartPage.VirtualPathFactory = page.VirtualPathFactory;
                    currentPage = parentStartPage;

                    break;
                }

                pageDirectory = VirtualPathUtility.GetDirectory(pageDirectory);
            }

            return currentPage;
        }

        /// <inheritdoc />
        public override ViewEngineResult FindPartialView(ControllerContext controllerContext, string partialViewName, bool useCache)
        {
            if (controllerContext == null) throw new ArgumentNullException(nameof(controllerContext));
            if (string.IsNullOrEmpty(partialViewName)) throw new ArgumentException("\"viewName\" argument cannot be null or empty.");

            var controllerName = controllerContext.RouteData.GetRequiredString("controller");
            var areaName = AreaHelpers.GetAreaName(controllerContext.RouteData);

            var locationsSearched = new List<string>(

                ((PartialViewLocationFormats?.Length ?? 0) +

                (areaName != null ? AreaPartialViewLocationFormats?.Length ?? 0 : 0))

            );

            var viewPath = ResolveViewPath(partialViewName, controllerName, areaName, PartialViewLocationFormats, AreaPartialViewLocationFormats, locationsSearched);

            return string.IsNullOrEmpty(viewPath)
                ? new ViewEngineResult(locationsSearched)
                : new ViewEngineResult(CreatePartialView(controllerContext, viewPath), this);
        }

        /// <inheritdoc />
        public override ViewEngineResult FindView(ControllerContext controllerContext, string viewName, string masterName, bool useCache)
        {
            // All this madness is essentially re-written from the VirtualPathProviderViewEngine class, but that class
            // checks on things like cache and whether the file exists and a whole bunch of stuff that's unnecessary.
            // Plus: unecessary allocations :(

            if (controllerContext == null) throw new ArgumentNullException(nameof(controllerContext));
            if (string.IsNullOrEmpty(viewName)) throw new ArgumentException("\"viewName\" argument cannot be null or empty.");

            var controllerName = controllerContext.RouteData.GetRequiredString("controller");
            var areaName = AreaHelpers.GetAreaName(controllerContext.RouteData);

            // minimize re-allocations of List
            var locationsSearched = new List<string>(

                ((ViewLocationFormats?.Length ?? 0) +

                (areaName != null ? AreaViewLocationFormats?.Length ?? 0 : 0)) +

                (MasterLocationFormats?.Length ?? 0) +

                (areaName != null ? AreaMasterLocationFormats?.Length ?? 0 : 0)

            );

            var viewPath = ResolveViewPath(viewName, controllerName, areaName, ViewLocationFormats, AreaViewLocationFormats, locationsSearched);
            var masterPath = ResolveViewPath(masterName, controllerName, areaName, MasterLocationFormats, AreaMasterLocationFormats, locationsSearched);

            if (string.IsNullOrEmpty(viewPath) ||
                (string.IsNullOrEmpty(masterPath) && !string.IsNullOrEmpty(masterName)))
            {
                return new ViewEngineResult(locationsSearched);
            }

            return new ViewEngineResult(CreateView(controllerContext, viewPath, masterPath), this);
        }

        private string ResolveViewPath(string viewName, string controllerName, string areaName, string[] viewLocationFormats, string[] areaViewLocationFormats, List<string> viewLocationsSearched)
        {
            if (IsSpecificPath(viewName))
            {
                var normalized = NormalizeViewName(viewName);
                viewLocationsSearched.Add(viewName);
                return _views.ContainsKey(normalized) ? normalized : null;
            }

            if (!string.IsNullOrEmpty(areaName) && areaViewLocationFormats != null)
            {
                foreach (var format in areaViewLocationFormats)
                {
                    var path = string.Format(format, viewName, controllerName, areaName);
                    viewLocationsSearched.Add(path);
                    if (_views.ContainsKey(path))
                        return path;
                }
            }

            if (viewLocationFormats == null)

                return null;

            foreach (var format in viewLocationFormats)
            {
                var path = string.Format(format, viewName, controllerName);
                viewLocationsSearched.Add(path);
                if (_views.ContainsKey(path))
                    return path;
            }

            return null;
        }

        private static string NormalizeViewName(string viewName)
        {
            return viewName[0] == '/' ? ("~" + viewName) : viewName;
        }

        private static bool IsSpecificPath(string path) => path.Length > 0 && (path[0] == '~' || path[0] == '/');
    }

    // Hooray, another MVC5 class that should be public but ISN'T
    internal static class AreaHelpers

    {

        public static string GetAreaName(RouteBase route)

        {

            var routeWithArea = route as IRouteWithArea;
            if (routeWithArea != null)
                return routeWithArea.Area;

            var castRoute = route as Route;
            return castRoute?.DataTokens?["area"] as string;
        }



        public static string GetAreaName(RouteData routeData)

        {

            object area;

            if (routeData.DataTokens.TryGetValue("area", out area))

            {

                return area as string;

            }



            return GetAreaName(routeData.Route);

        }

    }
}
