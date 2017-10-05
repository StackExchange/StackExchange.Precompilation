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
    class Compilation
    {
        private readonly PrecompilationCommandLineArgs _precompilationCommandLineArgs;

        internal CSharpCommandLineArguments CscArgs { get; private set; }
        internal DirectoryInfo CurrentDirectory { get; private set; }
        internal List<Diagnostic> Diagnostics { get; private set; }
        internal Encoding Encoding { get; private set; }

        private const string DiagnosticCategory = "StackExchange.Precompilation";
        private static DiagnosticDescriptor FailedToCreateModule =
            new DiagnosticDescriptor("SE001", "Failed to instantiate ICompileModule", "Failed to instantiate ICompileModule '{0}': {1}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ViewGenerationFailed =
            new DiagnosticDescriptor("SE003", "View generation failed", "View generation failed: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor FailedParsingSourceTree =
            new DiagnosticDescriptor("SE004", "Failed parsing source tree", "Failed parasing source tree: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_FileNotFound =
            new DiagnosticDescriptor("CS2001", "FileNotFound", "Source file '{0}' could not be found", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_BinaryFile =
            new DiagnosticDescriptor("CS2015", "BinaryFile", "'{0}' is a binary file instead of a text file", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_NoSourceFile =
            new DiagnosticDescriptor("CS1504", "NoSourceFile", "Source file '{0}' could not be opened ('{1}')", DiagnosticCategory, DiagnosticSeverity.Error, true);


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

        public async Task<bool> RunAsync()
        {
            try
            {
                // this parameter was introduced in rc3, all call to it seem to be using RuntimeEnvironment.GetRuntimeDirectory()
                // https://github.com/dotnet/roslyn/blob/0382e3e3fc543fc483090bff3ab1eaae39dfb4d9/src/Compilers/CSharp/csc/Program.cs#L18
                var sdkDirectory = RuntimeEnvironment.GetRuntimeDirectory();

                CscArgs = CSharpCommandLineParser.Default.Parse(_precompilationCommandLineArgs.Arguments, _precompilationCommandLineArgs.BaseDirectory, sdkDirectory);
                Diagnostics = new List<Diagnostic>(CscArgs.Errors);

                // load those before anything else hooks into our AssemlbyResolve...
                var compilationModules = LoadModules().ToList();

                if (Diagnostics.Any())
                {
                    return false;
                }
                Encoding = CscArgs.Encoding ?? new UTF8Encoding(false); // utf8 without bom

                var outputPath = Path.Combine(CscArgs.OutputDirectory, CscArgs.OutputFileName);
                var pdbPath = CscArgs.PdbPath ?? Path.ChangeExtension(outputPath, ".pdb");

                using (var workspace = CreateWokspace())
                using (var analysisCts = new CancellationTokenSource())
                using (var peStream = new MemoryStream())
                using (var pdbStream = CscArgs.EmitPdb && CscArgs.EmitOptions.DebugInformationFormat != DebugInformationFormat.Embedded ? new MemoryStream() : null)
                using (var xmlDocumentationStream = !string.IsNullOrWhiteSpace(CscArgs.DocumentationPath) ? new MemoryStream() : null)
                using (var razorParser = CreateRazorParser(workspace))
                {
                    EmitResult emitResult = null;
                    var project = CreateProject(workspace, razorParser);
                    CSharpCompilation compilation = null;
                    CompilationWithAnalyzers compilationWithAnalyzers = null;
                    try
                    {
                        compilation = await project.GetCompilationAsync() as CSharpCompilation;
                        await razorParser.Complete();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("error: failed to create compilation: " + ex.Message);
                        return false;
                    }

                    var analyzers = project.AnalyzerReferences.SelectMany(x => x.GetAnalyzers(project.Language)).ToImmutableArray();
                    if (!analyzers.IsEmpty)
                    {
                        compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, project.AnalyzerOptions, analysisCts.Token);
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

                    var analysisTask = compilationWithAnalyzers?.GetAnalysisResultAsync(analysisCts.Token);

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
                            debugEntryPoint: null);
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
                        Console.WriteLine("error: analysis failed: " + ex.Message);
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
                            DumpToFileAsync(outputPath, peStream),
                            DumpToFileAsync(pdbPath, pdbStream),
                            DumpToFileAsync(CscArgs.DocumentationPath, xmlDocumentationStream));
                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Join("\n", ex.ToString().Split('\n').Select((x, i) => x + $"ERROR: {i:D4} {x}")));
                return false;
            }
            finally
            {
                Diagnostics.ForEach(x => Console.WriteLine(x.ToString())); // strings only, since the Console.Out textwriter is another app domain...
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

        private static AdhocWorkspace CreateWokspace()
        {
            var assemblies = new[]
            {
                "Microsoft.CodeAnalysis.Workspaces",
                "Microsoft.CodeAnalysis.CSharp.Workspaces",
                "Microsoft.CodeAnalysis.Workspaces.Desktop",
            };

            var parts = new List<Type>();
            foreach (var a in assemblies)
            {
                // https://msdn.microsoft.com/en-us/library/system.reflection.assembly.gettypes(v=vs.110).aspx#Anchor_2
                try
                {
                    parts.AddRange(Assembly.Load(a).GetTypes());
                }
                catch (ReflectionTypeLoadException thatsWhyWeCantHaveNiceThings)
                {
                    parts.AddRange(thatsWhyWeCantHaveNiceThings.Types);
                }
            }
            parts.RemoveAll(x => x == null);

            var container = new ContainerConfiguration()
                .WithParts(parts)
                .WithPart<CompilationAnalyzerService>()
                .WithPart<CompilationAnalyzerAssemblyLoader>()
                .CreateContainer();

            var host = MefHostServices.Create(container);
            // belive me, I did try DesktopMefHostServices.DefaultServices
            var workspace = new AdhocWorkspace(host, WorkspaceKind);
            return workspace;
        }

        private Project CreateProject(AdhocWorkspace workspace, RazorParser razorParser)
        {
            var projectInfo = CommandLineProject.CreateProjectInfo(CscArgs.OutputFileName, "C#", Environment.CommandLine, _precompilationCommandLineArgs.BaseDirectory, workspace);

            projectInfo = projectInfo
                .WithCompilationOptions(CscArgs.CompilationOptions
                    .WithSourceReferenceResolver(new SourceFileResolver(CscArgs.SourcePaths, CscArgs.BaseDirectory, CscArgs.PathMap))) // required for path mapping support
                .WithDocuments(
                    projectInfo
                        .Documents
                        .Select(d => Path.GetExtension(d.FilePath) == ".cshtml"
                            ? razorParser.Wrap(d)
                            : d));

            //projectInfprojectInfo.WithCompilationOptions(CscArgs.CompilationOptions.WithSourceReferenceResolver(new SourceFileResolver(CscArgs.SourcePaths, CscArgs.BaseDirectory, CscArgs.PathMap)));
            //Console.WriteLine(projectInfo.CompilationOptions.SourceReferenceResolver.NormalizePath());

            return workspace.AddProject(projectInfo);
        }

        private IEnumerable<ICompileModule> LoadModules()
        {
            var compilationSection = PrecompilerSection.Current;
            if (compilationSection == null) yield break;

            foreach(var module in compilationSection.CompileModules.Cast<CompileModuleElement>())
            {
                ICompileModule compileModule = null;
                try
                {
                    var type = Type.GetType(module.Type, true);
                    compileModule = Activator.CreateInstance(type, true) as ICompileModule;
                }
                catch(Exception ex)
                {
                    Diagnostics.Add(Diagnostic.Create(
                        FailedToCreateModule,
                        Location.Create(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile, new TextSpan(), new LinePositionSpan()),
                        module.Type,
                        ex.Message));
                }
                if (compileModule != null)
                {
                    yield return compileModule;
                }
            }
        }

        private RazorParser CreateRazorParser(Workspace workspace) =>
            PrecompilerSection.Current?.RazorCache?.Directory is var dir &&
            string.IsNullOrWhiteSpace(dir)
                ? new RazorParser(workspace, this)
                : new RazorParser(workspace, this, Directory.CreateDirectory(Path.Combine(CscArgs.OutputDirectory, dir)));

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

        private static async Task DumpToFileAsync(string path, MemoryStream stream)
        {
            if (stream?.Length > 0)
            {
                stream.Position = 0;
                using (var file = File.Create(path))
                {
                    await stream.CopyToAsync(file);
                }
            }
        }

        class CompileContext
        {
            private readonly ICompileModule[] _modules;
            public BeforeCompileContext BeforeCompileContext { get; private set; }
            public AfterCompileContext AfterCompileContext { get; private set; }
            public CompileContext(IEnumerable<ICompileModule> modules)
            {
                _modules = modules == null ? new ICompileModule[0] : modules.ToArray();
            }
            public void Before(BeforeCompileContext context)
            {
                Apply(context, x => BeforeCompileContext = x, m => m.BeforeCompile);
            }
            public void After(AfterCompileContext context)
            {
                Apply(context, x => AfterCompileContext = x, m => m.AfterCompile);
            }
            private void Apply<TContext>(TContext ctx, Action<TContext> setter, Func<ICompileModule, Action<TContext>> actionGetter)
            {
                setter(ctx);
                foreach(var module in _modules)
                {
                    var action = actionGetter(module);
                    action(ctx);
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
}
