using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.Compilation;
using System.Web.Hosting;
using System.Web.Mvc;
using System.Web.Razor;
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
                var instance = Activator.CreateInstance(_type);
                var webViewPage = instance as WebViewPage;
                if (webViewPage == null)
                {
                    throw new InvalidOperationException("Invalid view type: " + _virtualPath);
                }

                webViewPage.VirtualPathFactory = this;
                webViewPage.VirtualPath = _virtualPath;
                webViewPage.ViewContext = viewContext;
                webViewPage.ViewData = viewContext.ViewData;
                webViewPage.InitHelpers();

                WebPageRenderingBase startPage = null;
                if (_runViewStartPages)
                {
                    startPage = StartPage.GetStartPage(webViewPage, "_ViewStart", _viewEngine.FileExtensions);
                }

                var pageContext = new WebPageContext(viewContext.HttpContext, webViewPage, null);
                webViewPage.ExecutePageHierarchy(pageContext, writer, startPage);
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
                var host = WebRazorHostFactory.CreateHostFromConfig(virtualPath);
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

        private static Assembly CompileToAssembly(WebPageRazorHost host, SyntaxTree syntaxTree)
        {
            var compilation = CSharpCompilation.Create(
                "RoslynRazor", // Note: using a fixed assembly name, which doesn't matter as long as we don't expect cross references of generated assemblies
                new[] { syntaxTree },
                BuildManager.GetReferencedAssemblies().OfType<Assembly>().Select(MetadataReference.CreateFromAssembly),
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
    }
}

