using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Emit;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host;
using System.Composition.Hosting;
using Microsoft.CodeAnalysis.Host.Mef;
using System.Composition;
using System.Threading;

namespace StackExchange.Precompilation
{
    internal class Compilation
    {
        private readonly PrecompilationCommandLineArgs _precompilationCommandLineArgs;

        internal CSharpCommandLineArguments CscArgs { get; private set; }
        internal DirectoryInfo CurrentDirectory { get; private set; }
        internal List<Diagnostic> Diagnostics { get; private set; }
        internal Encoding Encoding { get; private set; }

        private const string DiagnosticCategory = "StackExchange.Precompilation";
        private static DiagnosticDescriptor FailedToCreateModule =
            new DiagnosticDescriptor("SE001", "Failed to instantiate ICompileModule", "Failed to instantiate ICompileModule '{0}': {1}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        private static DiagnosticDescriptor FailedToCreateCompilation =
            new DiagnosticDescriptor("SE002", "Failed to create compilation", "{0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ViewGenerationFailed =
            new DiagnosticDescriptor("SE003", "View generation failed", "View generation failed: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor FailedParsingSourceTree =
            new DiagnosticDescriptor("SE004", "Failed parsing source tree", "Failed parasing source tree: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor PrecompilationModuleFailed =
            new DiagnosticDescriptor("SE005", "Precompilation module failed", "{0}: {1}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        private static DiagnosticDescriptor AnalysisFailed =
            new DiagnosticDescriptor("SE006", "Analysis failed", "{0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        private static DiagnosticDescriptor UnhandledException =
            new DiagnosticDescriptor("SE007", "Unhandled exception", "Unhandled exception: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_FileNotFound =
            new DiagnosticDescriptor("CS2001", "FileNotFound", "Source file '{0}' could not be found", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_BinaryFile =
            new DiagnosticDescriptor("CS2015", "BinaryFile", "'{0}' is a binary file instead of a text file", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_NoSourceFile =
            new DiagnosticDescriptor("CS1504", "NoSourceFile", "Source file '{0}' could not be opened ('{1}')", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor CachingFailed =
            new DiagnosticDescriptor("SE008", "Razor caching failed", "Caching generated cshtml for '{0}' failed, deleting file '{1}' - '{2}'", DiagnosticCategory, DiagnosticSeverity.Warning, true);
        internal static DiagnosticDescriptor CachingFailedHard =
            new DiagnosticDescriptor("SE009", "Razor caching failed hard", "Caching generated cshtml for '{0}' to '{1}' failed, unabled to delete cache file", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor RazorParserError =
            new DiagnosticDescriptor("SE010", "Razor parser error", "Razor parser error: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);

        public Compilation(PrecompilationCommandLineArgs precompilationCommandLineArgs)
        {
            _precompilationCommandLineArgs = precompilationCommandLineArgs;

            CurrentDirectory = new DirectoryInfo(_precompilationCommandLineArgs.BaseDirectory);

            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(CurrentDirectory.FullName, "App_Data")); // HACK mocking ASP.NET's ~/App_Data aka. |DataDirectory|

            // HACK moar HttpRuntime stuff
            AppDomain.CurrentDomain.SetData(".appDomain", AppDomain.CurrentDomain.FriendlyName);
            AppDomain.CurrentDomain.SetData(".appPath", CurrentDirectory.FullName);
            AppDomain.CurrentDomain.SetData(".appVPath", "/");
        }

        public async Task<bool> RunAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                // this parameter was introduced in rc3, all call to it seem to be using RuntimeEnvironment.GetRuntimeDirectory()
                // https://github.com/dotnet/roslyn/blob/0382e3e3fc543fc483090bff3ab1eaae39dfb4d9/src/Compilers/CSharp/csc/Program.cs#L18
                var sdkDirectory = RuntimeEnvironment.GetRuntimeDirectory();

                CscArgs = CSharpCommandLineParser.Default.Parse(_precompilationCommandLineArgs.Arguments, _precompilationCommandLineArgs.BaseDirectory, sdkDirectory);
                Diagnostics = new List<Diagnostic>(CscArgs.Errors);

                // load those before anything else hooks into our AssemlbyResolve.
                var loader = new PrecompilationModuleLoader(PrecompilerSection.Current);
                loader.ModuleInitializationFailed += (module, ex) =>
                {
                    Diagnostics.Add(Diagnostic.Create(
                        FailedToCreateModule,
                        Location.Create(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile, new TextSpan(), new LinePositionSpan()),
                        module.Type,
                        ex.Message));
                };
                var compilationModules = loader.LoadedModules;

                if (Diagnostics.Any())
                {
                    return false;
                }
                Encoding = CscArgs.Encoding ?? new UTF8Encoding(false); // utf8 without bom

                var outputPath = Path.Combine(CscArgs.OutputDirectory, CscArgs.OutputFileName);
                var pdbPath = CscArgs.PdbPath ?? Path.ChangeExtension(outputPath, ".pdb");

                using (var container = CreateCompositionHost())
                using (var workspace = CreateWokspace(container))
                using (var peStream = new MemoryStream())
                using (var pdbStream = CscArgs.EmitPdb && CscArgs.EmitOptions.DebugInformationFormat != DebugInformationFormat.Embedded ? new MemoryStream() : null)
                using (var xmlDocumentationStream = !string.IsNullOrWhiteSpace(CscArgs.DocumentationPath) ? new MemoryStream() : null)
                {
                    EmitResult emitResult = null;

                    var documentExtenders = workspace.Services.FindLanguageServices<IDocumentExtender>(_ => true).ToList();
                    var project = CreateProject(workspace, documentExtenders);
                    CSharpCompilation compilation = null;
                    CompilationWithAnalyzers compilationWithAnalyzers = null;
                    try
                    {
                        Diagnostics.AddRange((await Task.WhenAll(documentExtenders.Select(x => x.Complete()))).SelectMany(x => x));
                        compilation = await project.GetCompilationAsync(cancellationToken) as CSharpCompilation;
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Add(Diagnostic.Create(FailedToCreateCompilation, Location.None, ex));
                        return false;
                    }

                    var analyzers = project.AnalyzerReferences.SelectMany(x => x.GetAnalyzers(project.Language)).ToImmutableArray();
                    if (!analyzers.IsEmpty)
                    {
                        compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, project.AnalyzerOptions, cancellationToken);
                        compilation = compilationWithAnalyzers.Compilation as CSharpCompilation;
                    }

                    var context = new CompileContext(compilationModules);
                    context.Before(new BeforeCompileContext
                    {
                        Arguments = CscArgs,
                        Compilation = compilation.AddSyntaxTrees(GeneratedSyntaxTrees()),
                        Diagnostics = Diagnostics,
                    });

                    CscArgs = context.BeforeCompileContext.Arguments;
                    compilation = context.BeforeCompileContext.Compilation;

                    var analysisTask = compilationWithAnalyzers?.GetAnalysisResultAsync(cancellationToken);

                    using (var win32Resources = CreateWin32Resource(compilation))
                    {
                        // PathMapping is also required here, to actually get the symbols to line up:
                        // https://github.com/dotnet/roslyn/blob/9d081e899b35294b8f1793d31abe5e2c43698844/src/Compilers/Core/Portable/CommandLine/CommonCompiler.cs#L616
                        // PathUtilities.NormalizePathPrefix is internal, but callable via the SourceFileResolver, that we set in CreateProject
                        var emitOptions = CscArgs.EmitOptions
                            .WithPdbFilePath(compilation.Options.SourceReferenceResolver.NormalizePath(pdbPath, CscArgs.BaseDirectory));

                        // https://github.com/dotnet/roslyn/blob/41950e21da3ac2c307fb46c2ca8c8509b5059909/src/Compilers/Core/Portable/CommandLine/CommonCompiler.cs#L437
                        emitResult = compilation.Emit(
                            peStream: peStream,
                            pdbStream: pdbStream,
                            xmlDocumentationStream: xmlDocumentationStream,
                            win32Resources: win32Resources,
                            manifestResources: CscArgs.ManifestResources,
                            options: emitOptions,
                            sourceLinkStream: TryOpenFile(CscArgs.SourceLink, out var sourceLinkStream) ? sourceLinkStream : null,
                            embeddedTexts: CscArgs.EmbeddedFiles.AsEnumerable()
                                .Select(x => TryOpenFile(x.Path, out var embeddedText) ? EmbeddedText.FromStream(x.Path, embeddedText) : null)
                                .Where(x => x != null),
                            debugEntryPoint: null,
                            cancellationToken: cancellationToken);
                    }

                    Diagnostics.AddRange(emitResult.Diagnostics);

                    try
                    {
                        var analysisResult = analysisTask == null ? null : await analysisTask;
                        if (analysisResult != null)
                        {
                            Diagnostics.AddRange(analysisResult.GetAllDiagnostics());

                            foreach (var info in analysisResult.AnalyzerTelemetryInfo)
                            {
                                Console.WriteLine($"hidden: {info.Key} {info.Value.ExecutionTime.TotalMilliseconds:#}ms");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine("warning: analysis canceled");
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Add(Diagnostic.Create(AnalysisFailed, Location.None, ex));
                        return false;
                    }

                    if (!emitResult.Success || HasErrors)
                    {
                        return false;
                    }

                    context.After(new AfterCompileContext
                    {
                        Arguments = CscArgs,
                        AssemblyStream = peStream,
                        Compilation = compilation,
                        Diagnostics = Diagnostics,
                        SymbolStream = pdbStream,
                        XmlDocStream = xmlDocumentationStream,
                    });

                    if (!HasErrors)
                    {
                        // do not create the output files if emit fails
                        // if the output files are there, msbuild incremental build thinks the previous build succeeded
                        await Task.WhenAll(
                            DumpToFileAsync(outputPath, peStream, cancellationToken),
                            DumpToFileAsync(pdbPath, pdbStream, cancellationToken),
                            DumpToFileAsync(CscArgs.DocumentationPath, xmlDocumentationStream, cancellationToken));
                        return true;
                    }

                    return false;
                }
            }
            catch (PrecompilationModuleException pmex)
            {
                Diagnostics.Add(Diagnostic.Create(PrecompilationModuleFailed, Location.None, pmex.Message, pmex.InnerException));
                return false;
            }
            catch (Exception ex)
            {
                Diagnostics.Add(Diagnostic.Create(UnhandledException, Location.None, ex));
                return false;
            }
            finally
            {
                // strings only, since the Console.Out textwriter is another app domain...
                // https://stackoverflow.com/questions/2459994/is-there-a-way-to-print-a-new-line-when-using-message
                for (var i = 0; i < Diagnostics.Count; i++)
                {
                    var d = Diagnostics[i];
                    if (!d.IsSuppressed && d.Severity != DiagnosticSeverity.Hidden)
                    {
                        Console.WriteLine(d.ToString().Replace("\r", "").Replace("\n", "\\n"));
                    }
                }
            }
        }

        private bool HasErrors => Diagnostics.Any(x => !x.IsSuppressed && x.Severity == DiagnosticSeverity.Error);

        private const string WorkspaceKind = nameof(StackExchange) + "." + nameof(StackExchange.Precompilation);

        // all of this is because DesktopAnalyzerAssemblyLoader needs full paths
        [ExportWorkspaceService(typeof(IAnalyzerService), WorkspaceKind), Shared]
        private class CompilationAnalyzerService : IAnalyzerService, IWorkspaceService
        {
            private readonly IAnalyzerAssemblyLoader _loader = new CompilationAnalyzerAssemblyLoader();

            public IAnalyzerAssemblyLoader GetLoader() => _loader;
        }

        private class CompilationAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
        {
            private static Type DesktopAssemblyLoader = Type.GetType("Microsoft.CodeAnalysis.DesktopAnalyzerAssemblyLoader, Microsoft.CodeAnalysis.Workspaces.Desktop");
            private static IAnalyzerAssemblyLoader _desktopLoader = (IAnalyzerAssemblyLoader)Activator.CreateInstance(DesktopAssemblyLoader);

            private string ResolvePath(string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(path);

            public void AddDependencyLocation(string fullPath) => _desktopLoader.AddDependencyLocation(ResolvePath(fullPath));

            public Assembly LoadFromPath(string fullPath) => _desktopLoader.LoadFromPath(ResolvePath(fullPath));
        }

        private CompositionHost CreateCompositionHost()
        {
            var assemblies = new[]
            {
                "Microsoft.CodeAnalysis.Workspaces",
                "Microsoft.CodeAnalysis.CSharp.Workspaces",
                "Microsoft.CodeAnalysis.Workspaces.Desktop",
                "StackExchange.Precompilation.MVC5",
            };

            var parts = new List<Type>();
            foreach (var a in assemblies)
            {
                try
                {
                    parts.AddRange(Assembly.Load(a)?.GetTypes() ?? Enumerable.Empty<Type>());
                }
                catch (ReflectionTypeLoadException thatsWhyWeCantHaveNiceThings)
                {
                    // https://msdn.microsoft.com/en-us/library/system.reflection.assembly.gettypes(v=vs.110).aspx#Anchor_2
                    parts.AddRange(thatsWhyWeCantHaveNiceThings.Types.Where(x => x != null));
                }
                catch (FileNotFoundException nfe) when (nfe.FileName == "StackExchange.Precompilation.MVC5")
                {
                    // enable this to be loaded dynamically
                }
            }

            return new ContainerConfiguration()
                .WithParts(parts)
                .WithPart<CompilationAnalyzerService>()
                .WithPart<CompilationAnalyzerAssemblyLoader>()
                .CreateContainer();
        }

        private static AdhocWorkspace CreateWokspace(CompositionHost container)
        {
            var host = MefHostServices.Create(container);
            // belive me, I did try DesktopMefHostServices.DefaultServices
            var workspace = new AdhocWorkspace(host, WorkspaceKind);
            return workspace;
        }

        private Project CreateProject(AdhocWorkspace workspace, List<IDocumentExtender> documentExtenders)
        {
            var projectInfo = CommandLineProject.CreateProjectInfo(CscArgs.OutputFileName, "C#", Environment.CommandLine, _precompilationCommandLineArgs.BaseDirectory, workspace);

            projectInfo = projectInfo
                .WithCompilationOptions(CscArgs.CompilationOptions
                    .WithSourceReferenceResolver(new SourceFileResolver(CscArgs.SourcePaths, CscArgs.BaseDirectory, CscArgs.PathMap))) // required for path mapping support
                .WithDocuments(
                    projectInfo
                        .Documents
                        .Select(d => documentExtenders.Aggregate(d, (doc, ex) => ex.Extend(doc))));

            return workspace.AddProject(projectInfo);
        }

        private bool TryOpenFile(string path, out Stream stream)
        {
            stream = null;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            if (!File.Exists(path))
            {
                Diagnostics.Add(Diagnostic.Create(ERR_FileNotFound, null, path));
                return false;
            }
            try
            {
                stream = File.OpenRead(path);
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Add(Diagnostic.Create(ERR_NoSourceFile, null, path, ex.Message));
                return false;
            }
        }

        private Stream CreateWin32Resource(CSharpCompilation compilation)
        {
            if (TryOpenFile(CscArgs.Win32ResourceFile, out var stream)) return stream;

            using (var manifestStream = compilation.Options.OutputKind != OutputKind.NetModule && TryOpenFile(CscArgs.Win32Manifest, out var manifest) ? manifest : null)
            using (var iconStream = TryOpenFile(CscArgs.Win32Icon, out var icon) ? icon : null)
                return compilation.CreateDefaultWin32Resources(true, CscArgs.NoWin32Manifest, manifestStream, iconStream);
        }

        private static async Task DumpToFileAsync(string path, MemoryStream stream, CancellationToken cancellationToken)
        {
            if (stream?.Length > 0)
            {
                stream.Position = 0;
                using (var file = File.Create(path))
                using (cancellationToken.Register(() => { try { File.Delete(path); } catch { } }))
                {
                    await stream.CopyToAsync(file, 4096, cancellationToken);
                }
            }
        }

        private sealed class NaiveReferenceResolver : MetadataReferenceResolver
        {
            private NaiveReferenceResolver() { }
            public static NaiveReferenceResolver Instance { get; } = new NaiveReferenceResolver();
            public override bool Equals(object other) => other is NaiveReferenceResolver;

            public override int GetHashCode() => 42;

            public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
                => ImmutableArray.Create(MetadataReference.CreateFromFile(reference, properties));
        }

        private IEnumerable<SyntaxTree> GeneratedSyntaxTrees()
        {
            yield return SyntaxFactory.ParseSyntaxTree($"[assembly: {typeof(CompiledFromDirectoryAttribute).FullName}(@\"{CurrentDirectory.FullName}\")]", CscArgs.ParseOptions);
        }

        public Location AsLocation(string path)
        {
            return Location.Create(path, new TextSpan(), new LinePositionSpan());
        }
    }

    public interface IDocumentExtender : ILanguageService
    {
        DocumentInfo Extend(DocumentInfo document);
        Task<ICollection<Diagnostic>> Complete();
    }
}
