using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web;
using System.Web.Compilation;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Web.Razor;
using System.Web.Razor.Generator;
using System.Web.Razor.Parser.SyntaxTree;
using System.Web.WebPages;
using System.Web.WebPages.Razor;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// A replacement for the <see cref="RazorViewEngine"/> that uses roslyn (<see cref="Microsoft.CodeAnalysis"/>) instead of <see cref="System.CodeDom"/> to compile views.
    /// </summary>
    public class RoslynRazorViewEngine : ProfiledVirtualPathProviderViewEngine
    {
        /// <summary>
        /// Creates a new <see cref="RoslynRazorViewEngine"/> instance.
        /// </summary>
        public RoslynRazorViewEngine()
        {
            AreaViewLocationFormats = new[] {
                "~/Areas/{2}/Views/{1}/{0}.cshtml",
                "~/Areas/{2}/Views/Shared/{0}.cshtml"
            };

            AreaMasterLocationFormats = new[] {
                "~/Areas/{2}/Views/{1}/{0}.cshtml",
                "~/Areas/{2}/Views/Shared/{0}.cshtml"
            };

            AreaPartialViewLocationFormats = new[] {
                "~/Areas/{2}/Views/{1}/{0}.cshtml",
                "~/Areas/{2}/Views/Shared/{0}.cshtml"
            };
            ViewLocationFormats = new[] {
                "~/Views/{1}/{0}.cshtml",
                "~/Views/Shared/{0}.cshtml"
            };
            MasterLocationFormats = new[] {
                "~/Views/{1}/{0}.cshtml",
                "~/Views/Shared/{0}.cshtml"
            };
            PartialViewLocationFormats = new[] {
                "~/Views/{1}/{0}.cshtml",
                "~/Views/Shared/{0}.cshtml"
            };
            FileExtensions = new[] {
                "cshtml"
            };
        }

        /// <inheritdoc />
        protected override IView CreatePartialView(ControllerContext controllerContext, string partialPath) =>
            new RoslynRazorView(this, controllerContext, partialPath, GetTypeFromVirtualPath(partialPath), false);

        /// <inheritdoc />
        protected override IView CreateView(ControllerContext controllerContext, string viewPath, string masterPath) =>
            new RoslynRazorView(this, controllerContext, viewPath, GetTypeFromVirtualPath(viewPath), true);

        private class RoslynRazorView : IView, IVirtualPathFactory
        {
            private readonly Type _type;
            private readonly string _virtualPath;
            private readonly RoslynRazorViewEngine _viewEngine;
            private readonly bool _runViewStartPages;
            private readonly ControllerContext _controllerContext;

            public RoslynRazorView(RoslynRazorViewEngine viewEngine, ControllerContext controllerContext, string virtualPath, Type type, bool runViewStartPages)
            {
                _type = type;
                _virtualPath = virtualPath;
                _viewEngine = viewEngine;
                _runViewStartPages = runViewStartPages;
                _controllerContext = controllerContext;
            }

            public void Render(ViewContext viewContext, TextWriter writer)
            {
                var basePage = (WebPageBase)Activator.CreateInstance(_type);
                if (basePage == null)
                {
                    throw new InvalidOperationException("Invalid view type: " + _virtualPath);
                }
                basePage.VirtualPath = _virtualPath;
                basePage.VirtualPathFactory = this;

                var startPage = _runViewStartPages
                    ? StartPage.GetStartPage(basePage, "_ViewStart", _viewEngine.FileExtensions)
                    : null;

                var webViewPage = basePage as WebViewPage;
                if (webViewPage != null)
                {
                    webViewPage.ViewContext = viewContext;
                    webViewPage.ViewData = viewContext.ViewData;
                    webViewPage.InitHelpers();
                }

                var pageContext = new WebPageContext(viewContext.HttpContext, basePage, null);
                basePage.ExecutePageHierarchy(pageContext, writer, startPage);
            }

            public object CreateInstance(string virtualPath) =>
                Activator.CreateInstance(_viewEngine.GetTypeFromVirtualPath(virtualPath));

            public bool Exists(string virtualPath) =>
                _viewEngine.FileExists(controllerContext: _controllerContext, virtualPath: virtualPath);
        }

        private Type GetTypeFromVirtualPath(string virtualPath)
        {
            virtualPath = VirtualPathUtility.ToAbsolute(virtualPath);

            var cacheKey = "RoslynRazor_" + virtualPath;
            var type = HttpRuntime.Cache[cacheKey] as Type;
            if (type == null)
            {
                type = GetTypeFromVirtualPathNoCache(virtualPath);

                // Cache it, and make it dependent on the razor file
                var cacheDependency = HostingEnvironment.VirtualPathProvider.GetCacheDependency(virtualPath, new string[] { virtualPath }, DateTime.UtcNow);
                HttpRuntime.Cache.Insert(cacheKey, type, cacheDependency);
            }

            return type;
        }


        private Type GetTypeFromVirtualPathNoCache(string virtualPath)
        {
            using (DoProfileStep($"{nameof(RoslynRazorViewEngine)}: Compiling {virtualPath}"))
            {
                OnCodeGenerationStarted();
                var args = new CompilingPathEventArgs(virtualPath, WebRazorHostFactory.CreateHostFromConfig(virtualPath));
                OnBeforeCompilePath(args);
                var host = args.Host;
                var razorResult = RunRazorGenerator(virtualPath, host);
                var syntaxTree = GetSyntaxTree(host, razorResult);
                var assembly = CompileToAssembly(host, syntaxTree);
                return assembly.GetType($"{host.DefaultNamespace}.{host.DefaultClassName}");
            }
        }

        private GeneratorResults RunRazorGenerator(string virtualPath, WebPageRazorHost host)
        {
            var file = HostingEnvironment.VirtualPathProvider.GetFile(virtualPath);
            var engine = new RazorTemplateEngine(host);
            using (var viewStream = file.Open())
            using (var viewReader = new StreamReader(viewStream))
            {
                var razorResult = engine.GenerateCode(viewReader, className: null, rootNamespace: null, sourceFileName: host.PhysicalPath);
                if (!razorResult.Success)
                {
                    throw CreateExceptionFromParserError(razorResult.ParserErrors.Last(), virtualPath);
                }
                OnCodeGenerationCompleted(razorResult.GeneratedCode, host);
                return razorResult;
            }
        }

        private static SyntaxTree GetSyntaxTree(WebPageRazorHost host, GeneratorResults razorResult)
        {
            // Use CodeDom to generate source code from the CodeCompileUnit
            // Use roslyn to parse it back
            using (var codeDomProvider = CodeDomProvider.CreateProvider(host.CodeLanguage.LanguageName))
            using (var viewCodeStream = new MemoryStream())
            using (var viewCodeWriter = new StreamWriter(viewCodeStream))
            {
                codeDomProvider.GenerateCodeFromCompileUnit(razorResult.GeneratedCode, viewCodeWriter, new CodeGeneratorOptions());
                viewCodeWriter.Flush();
                viewCodeStream.Position = 0;
                var sourceText = SourceText.From(viewCodeStream);

                // We need a different file path for the generated file, otherwise breakpoints won't get
                // hit due to #line directives pointing at the original .cshtml. If we'd want breakpoint
                // in the generated .cs code, we'd have to dump them somewhere on disk, and specify the path here.
                var sourcePath = string.IsNullOrEmpty(host.PhysicalPath)
                    ? host.VirtualPath // yay virtual paths, won't point at the original file
                    : Path.ChangeExtension(host.PhysicalPath, ".roslynviewengine");
                return SyntaxFactory.ParseSyntaxTree(sourceText, path: sourcePath);
            }
        }

        // we were getting OutOfMemory exceptions caused by MetadataReference.CreateFrom* creating 
        // System.Reflection.PortableExecutable.PEReader instances for the same assembly for each view being compiled
        private static readonly ConcurrentDictionary<string, Lazy<MetadataReference>> ReferenceCache = new ConcurrentDictionary<string, Lazy<MetadataReference>>();
        private static readonly Func<Assembly, MetadataReference> ResolveReference = assembly =>
        {
            return ReferenceCache.GetOrAdd(
                assembly.Location,
                loc => new Lazy<MetadataReference>(
                    () => MetadataReference.CreateFromFile(loc),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        };

        private static Assembly CompileToAssembly(WebPageRazorHost host, SyntaxTree syntaxTree)
        {
            var compilation = CSharpCompilation.Create(
                "RoslynRazor", // Note: using a fixed assembly name, which doesn't matter as long as we don't expect cross references of generated assemblies
                new[] { syntaxTree },
                BuildManager.GetReferencedAssemblies().OfType<Assembly>().Select(ResolveReference),
                new CSharpCompilationOptions(
                    outputKind: OutputKind.DynamicallyLinkedLibrary,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default,
                    optimizationLevel: host.DefaultDebugCompilation ? OptimizationLevel.Debug : OptimizationLevel.Release));

            using (var dllStream = new MemoryStream())
            using (var pdbStream = new MemoryStream())
            {
                EmitResult emitResult = compilation.Emit(dllStream, pdbStream);

                if (!emitResult.Success)
                {
                    Diagnostic diagnostic = emitResult.Diagnostics.First(x => x.Severity == DiagnosticSeverity.Error);
                    string message = diagnostic.ToString();
                    LinePosition linePosition = diagnostic.Location.GetMappedLineSpan().StartLinePosition;

                    throw new HttpParseException(message, null, host.VirtualPath, null, linePosition.Line + 1);
                }

                return Assembly.Load(dllStream.GetBuffer(), pdbStream.GetBuffer());
            }
        }

        private static HttpParseException CreateExceptionFromParserError(RazorError error, string virtualPath) =>
            new HttpParseException(error.Message + Environment.NewLine, null, virtualPath, null, error.Location.LineIndex + 1);

        /// <summary>
        /// This is the equivalent of the <see cref="RazorBuildProvider.CompilingPath"/> event, since <see cref="PrecompiledViewEngine"/> bypasses <see cref="RazorBuildProvider"/> completely.
        /// </summary>
        public static event EventHandler<CompilingPathEventArgs> CompilingPath;

        /// <summary>
        /// Raises the <see cref="CompilingPath"/> event.
        /// </summary>
        /// <param name="args"></param>
        protected virtual void OnBeforeCompilePath(CompilingPathEventArgs args) =>
            CompilingPath?.Invoke(this, args);

        /// <summary>
        /// This is the equivalent of the <see cref="RazorBuildProvider.CodeGenerationStarted"/> event, since <see cref="PrecompiledViewEngine"/> bypasses <see cref="RazorBuildProvider"/> completely.
        /// </summary>
        public static event EventHandler CodeGenerationStarted;

        private void OnCodeGenerationStarted() =>
            CodeGenerationStarted?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// This is the equivalent of the <see cref="RazorBuildProvider.CodeGenerationCompleted"/> event, since <see cref="PrecompiledViewEngine"/> bypasses <see cref="RazorBuildProvider"/> completely.
        /// </summary>
        public static event EventHandler<CodeGenerationCompleteEventArgs> CodeGenerationCompleted;

        private void OnCodeGenerationCompleted(CodeCompileUnit codeCompileUnit, WebPageRazorHost host) =>
           CodeGenerationCompleted?.Invoke(this, new CodeGenerationCompleteEventArgs(host.VirtualPath, host.PhysicalPath, codeCompileUnit));
    }
}

