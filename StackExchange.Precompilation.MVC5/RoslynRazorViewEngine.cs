using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        /// <summary>When set to <c>true</c>, configured <see cref="ICompileModule" />s are used when the views are compiled</summary>
        public bool UseCompilationModules { get; set; }

        private readonly ICompileModule[] _noModule = new ICompileModule[0];
        private readonly PrecompilationModuleLoader _moduleLoader = new PrecompilationModuleLoader(PrecompilerSection.Current);

        /// <inheritdoc />
        protected override IVirtualPathFactory CreateVirtualPathFactory() => new PrecompilationVirtualPathFactory(
            runtime: this,
            precompiled: ViewEngines.Engines.OfType<PrecompiledViewEngine>().FirstOrDefault());

        /// <inheritdoc />
        protected override IView CreatePartialView(ControllerContext controllerContext, string partialPath) =>
            new PrecompilationView(partialPath, null, GetTypeFromVirtualPath(partialPath), false, this);

        /// <inheritdoc />
        protected override IView CreateView(ControllerContext controllerContext, string viewPath, string masterPath) =>
            new PrecompilationView(viewPath, masterPath, GetTypeFromVirtualPath(viewPath), true, this);

        internal bool FileExists(string virtualPath) =>
            HostingEnvironment.VirtualPathProvider.FileExists(virtualPath);

        internal Type GetTypeFromVirtualPath(string virtualPath)
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
            using (this.DoProfileStep($"{nameof(RoslynRazorViewEngine)}: Compiling {virtualPath}"))
            {
                OnCodeGenerationStarted();
                var args = new CompilingPathEventArgs(virtualPath, WebRazorHostFactory.CreateHostFromConfig(virtualPath));
                OnBeforeCompilePath(args);
                var host = args.Host;
                var razorResult = RunRazorGenerator(virtualPath, host);
                var syntaxTree = GetSyntaxTree(host, razorResult);
                var assembly = CompileToAssembly(host, syntaxTree, UseCompilationModules ? _moduleLoader.LoadedModules : _noModule);
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
                    var sourceCode = (string)null;
                    if (viewStream.CanSeek)
                    {
                        viewStream.Seek(0, SeekOrigin.Begin);
                        sourceCode = viewReader.ReadToEnd();
                    }
                    throw CreateExceptionFromParserError(razorResult.ParserErrors.Last(), virtualPath, sourceCode);
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
        internal static MetadataReference ResolveReference(Assembly assembly)
        {
            var key = assembly.Location;
            Uri uri;
            if (Uri.TryCreate(assembly.CodeBase, UriKind.Absolute, out uri) && uri.IsFile)
            {
                key = uri.LocalPath;
            }
            return ReferenceCache.GetOrAdd(
                key,
                loc => new Lazy<MetadataReference>(
                    () => MetadataReference.CreateFromFile(loc),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        private static Assembly CompileToAssembly(WebPageRazorHost host, SyntaxTree syntaxTree, ICollection<ICompileModule> compilationModules)
        {
            var strArgs = new List<string>();
            strArgs.Add("/target:library");
            strArgs.Add(host.DefaultDebugCompilation ? "/o-" : "/o+");
            strArgs.Add(host.DefaultDebugCompilation ? "/debug+" : "/debug-");

            var cscArgs = CSharpCommandLineParser.Default.Parse(strArgs, null, null);

            var compilation = CSharpCompilation.Create(
                "RoslynRazor", // Note: using a fixed assembly name, which doesn't matter as long as we don't expect cross references of generated assemblies
                new[] { syntaxTree },
                BuildManager.GetReferencedAssemblies().OfType<Assembly>().Select(ResolveReference),
                cscArgs.CompilationOptions.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default));

            compilation = Hacks.MakeValueTuplesWorkWhenRunningOn47RuntimeAndTargetingNet45Plus(compilation);
            var diagnostics = new List<Diagnostic>();
            var context = new CompileContext(compilationModules);
            context.Before(new BeforeCompileContext
            {
                Arguments = cscArgs,
                Compilation = compilation,
                Diagnostics = diagnostics,
            });
            compilation = context.BeforeCompileContext.Compilation;

            using (var dllStream = new MemoryStream())
            using (var pdbStream = new MemoryStream())
            {
                EmitResult emitResult = compilation.Emit(dllStream, pdbStream);
                diagnostics.AddRange(emitResult.Diagnostics);

                if (!emitResult.Success)
                {
                    Diagnostic diagnostic = diagnostics.First(x => x.Severity == DiagnosticSeverity.Error);
                    string message = diagnostic.ToString();
                    LinePosition linePosition = diagnostic.Location.GetMappedLineSpan().StartLinePosition;

                    throw new HttpParseException(message, null, host.VirtualPath, null, linePosition.Line + 1);
                }

                context.After(new AfterCompileContext
                {
                    Arguments = context.BeforeCompileContext.Arguments,
                    AssemblyStream = dllStream,
                    Compilation = compilation,
                    Diagnostics = diagnostics,
                    SymbolStream = pdbStream,
                    XmlDocStream = null,
                });

                return Assembly.Load(dllStream.GetBuffer(), pdbStream.GetBuffer());
            }
        }

        private static HttpParseException CreateExceptionFromParserError(RazorError error, string virtualPath, string sourceCode) =>
            new HttpParseException(error.Message + Environment.NewLine, null, virtualPath, sourceCode, error.Location.LineIndex + 1);

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

