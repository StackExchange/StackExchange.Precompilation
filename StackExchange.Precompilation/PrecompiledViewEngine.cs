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
    public class PrecompiledViewEngine : ProfiledVirtualPathProviderViewEngine
    {
        /// <summary>
        /// Gets the view paths
        /// </summary>
        public IEnumerable<string> ViewPaths { get; private set; }

        private readonly Dictionary<string, Type> _views;

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
                // https://msdn.microsoft.com/en-us/library/system.reflection.assembly.gettypes(v=vs.110).aspx#Anchor_2
                Type[] asmTypes;
                try
                {
                    asmTypes = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException thatsWhyWeCantHaveNiceThings)
                {
                    asmTypes = thatsWhyWeCantHaveNiceThings.Types;
                }
                var viewTypes = asmTypes.Where(t => typeof(WebPageRenderingBase).IsAssignableFrom(t)).ToList();

                var sourceDirectory = asm.GetCustomAttribute<CompiledFromDirectoryAttribute>()?.SourceDirectory;

                foreach (var view in viewTypes)
                {
                    var attr = view.GetCustomAttribute<CompiledFromFileAttribute>();
                    if (attr != null)
                    {
                        _views[MakeVirtualPath(attr.SourceFile, sourceDirectory)] = view;
                    }
                }
            }

            ViewPaths = _views.Keys.OrderBy(_ => _).ToList();
        }

        /// <inheritdoc />
        protected override IVirtualPathFactory CreateVirtualPathFactory() => new PrecompilationVirtualPathFactory(
            precompiled: this,
            runtime: ViewEngines.Engines.OfType<RoslynRazorViewEngine>().FirstOrDefault());

        private static string MakeVirtualPath(string absoluteViewPath, string absoluteDirectoryPath = null)
        {
            if (absoluteDirectoryPath != null && absoluteViewPath.StartsWith(absoluteDirectoryPath))
            {
                return MakeVirtualPath(absoluteViewPath, absoluteDirectoryPath.Length - (absoluteDirectoryPath.EndsWith("\\") ? 1 : 0));
            }

            // we could just bail here, but let's make a best effort...
            var firstArea = absoluteViewPath.IndexOf(@"\Areas\");
            if (firstArea != -1)
            {
                return MakeVirtualPath(absoluteViewPath, firstArea);
            }
            else
            {
                var firstView = absoluteViewPath.IndexOf(@"\Views\");
                if (firstView == -1) throw new Exception("Couldn't make virtual for: " + absoluteViewPath);

                return MakeVirtualPath(absoluteViewPath, firstView);
            }
        }

        private static string MakeVirtualPath(string absoluteViewPath, int startIndex)
        {
            var tail = absoluteViewPath.Substring(startIndex);
            var vp = "~" + tail.Replace(@"\", "/");

            return vp;
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


        internal Type TryLookupCompiledType(string viewPath)
        {
            Type compiledView;
            if (!_views.TryGetValue(viewPath, out compiledView))
            {
                return null;
            }

            return compiledView;
        }
        private IView CreateViewImpl(string viewPath, string masterPath, bool runViewStart)
        {
            var compiledType = TryLookupCompiledType(viewPath);
            if (compiledType == null)
            {
                return null;
            }

            return new PrecompilationView(viewPath, masterPath, compiledType, runViewStart, this);
        }

        /// <inheritdoc />
        public override ViewEngineResult FindPartialView(ControllerContext controllerContext, string partialViewName, bool useCache)
        {
            if (controllerContext == null) throw new ArgumentNullException(nameof(controllerContext));
            if (string.IsNullOrEmpty(partialViewName)) throw new ArgumentException($"\"{nameof(partialViewName)}\" argument cannot be null or empty.", nameof(partialViewName));

            var areaName = AreaHelpers.GetAreaName(controllerContext.RouteData);

            var locationsSearched = new List<string>(
                DisplayModeProvider.Modes.Count * (
                    ((PartialViewLocationFormats?.Length ?? 0) +
                    (areaName != null ? AreaPartialViewLocationFormats?.Length ?? 0 : 0)))
            );

            var viewPath = ResolveViewPath(partialViewName, areaName, PartialViewLocationFormats, AreaPartialViewLocationFormats, locationsSearched, controllerContext);

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
            if (string.IsNullOrEmpty(viewName)) throw new ArgumentException($"\"{nameof(viewName)}\" argument cannot be null or empty.", nameof(viewName));

            var areaName = AreaHelpers.GetAreaName(controllerContext.RouteData);

            // minimize re-allocations of List
            var locationsSearched = new List<string>(
                DisplayModeProvider.Modes.Count * (
                    ((ViewLocationFormats?.Length ?? 0) +
                    (areaName != null ? AreaViewLocationFormats?.Length ?? 0 : 0)) +
                    (MasterLocationFormats?.Length ?? 0) +
                    (areaName != null ? AreaMasterLocationFormats?.Length ?? 0 : 0))
            );

            var viewPath = ResolveViewPath(viewName, areaName, ViewLocationFormats, AreaViewLocationFormats, locationsSearched, controllerContext);
            var masterPath = ResolveViewPath(masterName, areaName, MasterLocationFormats, AreaMasterLocationFormats, locationsSearched, controllerContext);

            if (string.IsNullOrEmpty(viewPath) ||
                (string.IsNullOrEmpty(masterPath) && !string.IsNullOrEmpty(masterName)))
            {
                return new ViewEngineResult(locationsSearched);
            }

            return new ViewEngineResult(CreateView(controllerContext, viewPath, masterPath), this);
        }

        private string ResolveViewPath(string viewName, string areaName, string[] viewLocationFormats, string[] areaViewLocationFormats, List<string> viewLocationsSearched, ControllerContext controllerContext)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                return null;
            }

            var controllerName = controllerContext.RouteData.GetRequiredString("controller");
            if (IsSpecificPath(viewName))
            {
                var normalized = NormalizeViewName(viewName);

                viewLocationsSearched.Add(viewName);
                return _views.ContainsKey(normalized) ? normalized : null;
            }

            areaViewLocationFormats = (areaName == null ? null : areaViewLocationFormats) ?? new string[0];
            viewLocationFormats = viewLocationFormats ?? new string[0];

            var httpContext = controllerContext.HttpContext;
            var availableDisplayModes = DisplayModeProvider.GetAvailableDisplayModesForContext(httpContext, controllerContext.DisplayMode);
            foreach (var displayMode in availableDisplayModes)
            {
                for (var i = 0; i < areaViewLocationFormats.Length; i++)
                {
                    var path = string.Format(areaViewLocationFormats[i], viewName, controllerName, areaName);
                    if (TryResolveView(httpContext, displayMode, ref path, viewLocationsSearched)) return path;
                }

                for (var i = 0; i < viewLocationFormats.Length; i++)
                {
                    var path = string.Format(viewLocationFormats[i], viewName, controllerName);
                    if (TryResolveView(httpContext, displayMode, ref path, viewLocationsSearched)) return path;
                }
            }

            return null;
        }

        private bool TryResolveView(HttpContextBase httpContext, IDisplayMode displayMode, ref string path, ICollection<string> viewLocationsSearched)
        {
            path = NormalizeViewName(VirtualPathUtility.ToAppRelative(path)); // resolve relative path portions
            var displayInfo = displayMode.GetDisplayInfo(httpContext, path, _views.ContainsKey);

            if (displayInfo == null || displayInfo.FilePath == null)
            {
                viewLocationsSearched.Add(path);
                return false;
            }

            path = displayInfo.FilePath;
            return true;
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
