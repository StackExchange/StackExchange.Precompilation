using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.Mvc;
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

        private static readonly Func<VirtualPathProviderViewEngine, ControllerContext, string, IView> CreatePartialViewThunk;
        private static readonly Func<VirtualPathProviderViewEngine, ControllerContext, string, string, IView> CreateViewThunk;

        static PrecompiledViewEngine()
        {
            var createPartialViewMtd = typeof(VirtualPathProviderViewEngine).GetMethod(nameof(CreatePartialView), BindingFlags.NonPublic | BindingFlags.Instance);
            var createViewMtd = typeof(VirtualPathProviderViewEngine).GetMethod(nameof(CreateView), BindingFlags.NonPublic | BindingFlags.Instance);

            CreatePartialViewThunk = (Func<VirtualPathProviderViewEngine, ControllerContext, string, IView>)Delegate.CreateDelegate(typeof(Func<VirtualPathProviderViewEngine, ControllerContext, string, IView>), createPartialViewMtd, throwOnBindFailure: true);
            CreateViewThunk = (Func<VirtualPathProviderViewEngine, ControllerContext, string, string, IView>)Delegate.CreateDelegate(typeof(Func<VirtualPathProviderViewEngine, ControllerContext, string, string, IView>), createViewMtd, throwOnBindFailure: true);
        }

        /// <summary>
        /// Gets the view paths
        /// </summary>
        public IEnumerable<string> ViewPaths { get; private set; }

        private readonly Dictionary<string, Type> _views;

        /// <summary>
        /// Gets or sets the fallback view engine. This view engine is called when a precompiled view is not found.
        /// </summary>
        public VirtualPathProviderViewEngine FallbackViewEngine { get; set; }

        /// <summary>
        /// Triggers when the engine performs a step that can be profiled.
        /// </summary>
        public Func<string, IDisposable> ProfileStep { get; set; }

        /// <summary>
        /// Triggers when the engine creates a view thunk.
        /// </summary>
        public Action<string> ViewThunk { get; set; }

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
            FallbackViewEngine = new RazorViewEngine();

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

        private void ReportViewThunk(string viewName)
        {
            ViewThunk?.Invoke(viewName);
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
            CreateViewImpl(partialPath, masterPath: null, runViewStart: false) ?? Thunk(controllerContext, partialPath);

        /// <inheritdoc />
        protected override IView CreateView(ControllerContext controllerContext, string viewPath, string masterPath) =>
            CreateViewImpl(viewPath, masterPath, runViewStart: true) ?? Thunk(controllerContext, viewPath, masterPath);   

        private IView Thunk(ControllerContext controllerContext, string partialPath)
        {
            using (DoProfileStep(nameof(CreatePartialViewThunk)))
            {
                ReportViewThunk(partialPath);
                return CreatePartialViewThunk(FallbackViewEngine, controllerContext, partialPath);
            }
        }
     

        private IView Thunk(ControllerContext controllerContext, string viewPath, string masterPath)
        {
            using (DoProfileStep(nameof(CreateViewThunk)))
            {
                ReportViewThunk(viewPath);
                return CreateViewThunk(FallbackViewEngine, controllerContext, viewPath, masterPath);
            }
        }

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

            if (runViewStart)
            {
                return new PrecompiledView(viewPath, masterPath, compiledView, TryLookupCompiledType, GetStartPage, this);
            }

            return new PrecompiledView(viewPath, masterPath, compiledView, TryLookupCompiledType, null, this);
        }

        private WebPageRenderingBase GetStartPage(WebPageRenderingBase page, string fileName)
        {
            WebPageRenderingBase currentPage = page;
            var pageDirectory = VirtualPathUtility.GetDirectory(page.VirtualPath);

            while (!String.IsNullOrEmpty(pageDirectory) && pageDirectory != "/")
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
            var created = CreateViewImpl(partialViewName, null, false);
            if (created != null)
            {
                return new ViewEngineResult(created, this);
            }

            return base.FindPartialView(controllerContext, partialViewName, useCache);
        }

        /// <inheritdoc />
        public override ViewEngineResult FindView(ControllerContext controllerContext, string viewName, string masterName, bool useCache)
        {
            var created = CreateViewImpl(viewName, masterName, true);
            if (created != null)
            {
                return new ViewEngineResult(created, this);
            }

            return base.FindView(controllerContext, viewName, masterName, useCache);
        }
    }
}