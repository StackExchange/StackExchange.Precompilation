using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Emit;
using System.Collections.Immutable;

namespace StackExchange.Precompilation
{
    class Compilation
    {
        private readonly PrecompilationCommandLineArgs _precompilationCommandLineArgs;

        internal CSharpCommandLineArguments CscArgs { get; private set; }
        internal DirectoryInfo CurrentDirectory { get; private set; }
        internal List<Diagnostic> Diagnostics { get; private set; }
        internal Encoding Encoding { get; private set; }
        private readonly Dictionary<string, Lazy<Parser>> _syntaxTreeLoaders;

        private const string DiagnosticCategory = "StackExchange.Precompilation";
        private static DiagnosticDescriptor FailedToCreateModule =
            new DiagnosticDescriptor("SE001", "Failed to instantiate ICompileModule", "Failed to instantiate ICompileModule '{0}': {1}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        private static DiagnosticDescriptor UnknownFileType =
            new DiagnosticDescriptor("SE002", "Unknown file type", "Unknown file type '{0}'", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ViewGenerationFailed =
            new DiagnosticDescriptor("SE003", "View generation failed", "View generation failed: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor FailedParsingSourceTree =
            new DiagnosticDescriptor("SE004", "Failed parsing source tree", "Failed parasing source tree: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_FileNotFound =
            new DiagnosticDescriptor("CS2001", "FileNotFound", "Source file '{0}' could not be found", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_BinaryFile =
            new DiagnosticDescriptor("CS2015", "BinaryFile", "'{0}' is a binary file instead of a text file", DiagnosticCategory, DiagnosticSeverity.Error, true);
        internal static DiagnosticDescriptor ERR_NoSourceFile =
            new DiagnosticDescriptor("CS1504", "NoSourceFile", "Source file '{0}' could not be opened ('{1}') ", DiagnosticCategory, DiagnosticSeverity.Error, true);


        public Compilation(PrecompilationCommandLineArgs precompilationCommandLineArgs)
        {
            _precompilationCommandLineArgs = precompilationCommandLineArgs;
            _syntaxTreeLoaders = new Dictionary<string, Lazy<Parser>>
            {
                {".cs", new Lazy<Parser>(CSharp, LazyThreadSafetyMode.PublicationOnly)},
                {".cshtml", new Lazy<Parser>(Razor, LazyThreadSafetyMode.PublicationOnly)},
            };

            CurrentDirectory = new DirectoryInfo(_precompilationCommandLineArgs.BaseDirectory);

            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(CurrentDirectory.FullName, "App_Data")); // HACK mocking ASP.NET's ~/App_Data aka. |DataDirectory|

            // HACK moar HttpRuntime stuff
            AppDomain.CurrentDomain.SetData(".appDomain", AppDomain.CurrentDomain.FriendlyName);
            AppDomain.CurrentDomain.SetData(".appPath", CurrentDirectory.FullName);
            AppDomain.CurrentDomain.SetData(".appVPath", "/");
        }

        private Parser CSharp()
        {
            return new CSharpParser(this);
        }

        private Parser Razor()
        {
            return new RazorParser(this);
        }

        public bool Run()
        {
            try
            {
                // this parameter was introduced in rc3, all call to it seem to be using RuntimeEnvironment.GetRuntimeDirectory()
                // https://github.com/dotnet/roslyn/blob/0382e3e3fc543fc483090bff3ab1eaae39dfb4d9/src/Compilers/CSharp/csc/Program.cs#L18
                var sdkDirectory = RuntimeEnvironment.GetRuntimeDirectory();
                CscArgs = CSharpCommandLineParser.Default.Parse(_precompilationCommandLineArgs.Arguments, _precompilationCommandLineArgs.BaseDirectory, sdkDirectory);
                
                Diagnostics = new List<Diagnostic>(CscArgs.Errors);
                if (Diagnostics.Any())
                {
                    return false;
                }
                Encoding = CscArgs.Encoding ?? new UTF8Encoding(false); // utf8 without bom

                var compilationOptions = CscArgs.CompilationOptions.WithAssemblyIdentityComparer(GetAssemblyIdentityComparer());
                if (!string.IsNullOrEmpty(CscArgs.CompilationOptions.CryptoKeyFile))
                {
                    var cryptoKeyFilePath = Path.Combine(CscArgs.BaseDirectory, CscArgs.CompilationOptions.CryptoKeyFile);
                    compilationOptions = compilationOptions.WithStrongNameProvider(new DesktopStrongNameProvider(ImmutableArray.Create(cryptoKeyFilePath)));
                }

                var references = SetupReferences();
                var sources = LoadSources(CscArgs.SourceFiles);

                var compilationModules = LoadModules().ToList();

                var compilation = CSharpCompilation.Create(
                    options: compilationOptions,
                    references: references,
                    syntaxTrees: sources,
                    assemblyName: CscArgs.CompilationName);

                var context = new CompileContext(compilationModules);

                context.Before(new BeforeCompileContext
                {
                    Arguments = CscArgs,
                    Compilation = compilation,
                    Diagnostics = Diagnostics,
                    Resources = CscArgs.ManifestResources.ToList()
                });

                var emitResult = Emit(context);
                return emitResult.Success;
            }
            finally
            {
                Diagnostics.ForEach(x => Console.WriteLine(x.ToString())); // strings only, since the Console.Out textwriter is another app domain...
            }
        }

        private DesktopAssemblyIdentityComparer GetAssemblyIdentityComparer()
        {
            // https://github.com/dotnet/roslyn/blob/41950e21da3ac2c307fb46c2ca8c8509b5059909/src/Compilers/CSharp/Portable/CommandLine/CSharpCompiler.cs#L105
            if (CscArgs.AppConfigPath == null)
                return DesktopAssemblyIdentityComparer.Default;

            using (var appConfigStream = File.OpenRead(CscArgs.AppConfigPath))
            {
                return DesktopAssemblyIdentityComparer.LoadFromXml(appConfigStream);
            }
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
                    var type = Type.GetType(module.Type, false);
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

        private Stream CreateWin32Resource(CSharpCompilation compilation)
        {
            if (CscArgs.Win32ResourceFile != null)
                return File.OpenRead(CscArgs.Win32ResourceFile);

            using (var manifestStream = compilation.Options.OutputKind != OutputKind.NetModule ? CscArgs.Win32Manifest != null ? File.OpenRead(CscArgs.Win32Manifest) : null : null)
            using (var iconStream = CscArgs.Win32Icon != null ? File.OpenRead(CscArgs.Win32Icon) : null)
                return compilation.CreateDefaultWin32Resources(true, CscArgs.NoWin32Manifest, manifestStream, iconStream);
        }

        private EmitResult Emit(CompileContext context)
        {
            var compilation = context.BeforeCompileContext.Compilation;
            var pdbPath = CscArgs.PdbPath;
            var outputPath = Path.Combine(CscArgs.OutputDirectory, CscArgs.OutputFileName);

            if (!CscArgs.EmitPdb)
            {
                pdbPath = null;
            }
            else if (string.IsNullOrWhiteSpace(pdbPath))
            {
                pdbPath = Path.ChangeExtension(outputPath, ".pdb");
            }

            using (var peStream = new MemoryStream())
            using (var pdbStream = !string.IsNullOrWhiteSpace(pdbPath) ? new MemoryStream() : null)
            using (var xmlDocumentationStream = !string.IsNullOrWhiteSpace(CscArgs.DocumentationPath) ? new MemoryStream(): null)
            using (var win32Resources = CreateWin32Resource(compilation))
            {
                // https://github.com/dotnet/roslyn/blob/41950e21da3ac2c307fb46c2ca8c8509b5059909/src/Compilers/Core/Portable/CommandLine/CommonCompiler.cs#L437
                var emitResult = compilation.Emit(
                    peStream: peStream,
                    pdbStream: pdbStream,
                    xmlDocumentationStream: xmlDocumentationStream,
                    win32Resources: win32Resources,
                    manifestResources: CscArgs.ManifestResources,
                    options: CscArgs.EmitOptions,
                    debugEntryPoint: null);

                Diagnostics.AddRange(emitResult.Diagnostics);
                context.After(new AfterCompileContext
                {
                    Arguments = CscArgs,
                    AssemblyStream = peStream,
                    Compilation = compilation,
                    Diagnostics = Diagnostics,
                    SymbolStream = pdbStream,
                    XmlDocStream = xmlDocumentationStream,
                });

                // do not create the output files if emit fails
                // if the output files are there, msbuild incremental build thinks the previous build succeeded
                if (emitResult.Success)
                {
                    Task.WaitAll(
                        DumpToFileAsync(outputPath, peStream),
                        DumpToFileAsync(pdbPath, pdbStream),
                        DumpToFileAsync(CscArgs.DocumentationPath, xmlDocumentationStream));
                }

                return emitResult;
            }
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

        private ICollection<MetadataReference> SetupReferences()
        {
            // really don't care about /addmodule and .netmodule stuff...
            // https://msdn.microsoft.com/en-us/library/226t7yxe.aspx
            return CscArgs.MetadataReferences.Select(x => (MetadataReference)MetadataReference.CreateFromFile(x.Reference, x.Properties)).ToArray();
        }

        private IEnumerable<SyntaxTree> LoadSources(ICollection<CommandLineSourceFile> paths)
        {
            var trees = new SyntaxTree[paths.Count];
            var parseOptions = CscArgs.ParseOptions;
            var scriptParseOptions = CscArgs.ParseOptions.WithKind(SourceCodeKind.Script);
            var diagnostics = new Diagnostic[paths.Count];
            Parallel.ForEach(paths,
                (path, state, index) =>
                {
                    var file = path.Path;
                    var ext = Path.GetExtension(file) ?? "";
                    Lazy<Parser> parser;

                    if(_syntaxTreeLoaders.TryGetValue(ext, out parser))
                    {
                        var fileOpen = false;
                        try
                        {
                            // bufferSize: 1 -> https://github.com/dotnet/roslyn/blob/ec1ea081ff5d84e91cbcb3b2f824655609cc5fc6/src/Compilers/Core/Portable/CommandLine/CommonCompiler.cs#L143
                            using (
                                var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                                    bufferSize: 1))
                            {
                                fileOpen = true;
                                trees[index] = parser.Value.GetSyntaxTree(file, sourceStream,
                                    path.IsScript ? scriptParseOptions : parseOptions);
                            }
                        }

                        // should be equivalent to CommonCompiler.ToFileReadDiagnostics
                        // see https://github.com/dotnet/roslyn/blob/ddaf4146/src/Compilers/Core/Portable/CommandLine/CommonCompiler.cs#L165
                        catch (Exception ex)
                            when (!fileOpen && (ex is FileNotFoundException || ex is DirectoryNotFoundException))
                        {
                            diagnostics[index] = Diagnostic.Create(ERR_FileNotFound, Location.None, file);
                        }
                        catch (InvalidDataException)
                        {
                            diagnostics[index] = Diagnostic.Create(ERR_BinaryFile, AsLocation(file), file);
                        }
                        catch (Exception ex)
                        {
                            diagnostics[index] = Diagnostic.Create(ERR_NoSourceFile, AsLocation(file), file, ex.Message);
                        }
                    }
                    else
                    {
                        diagnostics[index] = Diagnostic.Create(UnknownFileType, AsLocation(file), ext);
                    }
                });

            Diagnostics.AddRange(diagnostics.Where(x => x != null));

            return trees.Where(x => x != null).Concat(GeneratedSyntaxTrees());
        }

        private IEnumerable<SyntaxTree> GeneratedSyntaxTrees()
        {
            yield return SyntaxFactory.ParseSyntaxTree($"[assembly: {typeof(CompiledFromDirectoryAttribute).FullName}(@\"{CurrentDirectory.FullName}\")]");
        }

        public Location AsLocation(string path)
        {
            return Location.Create(path, new TextSpan(), new LinePositionSpan());
        }
    }
}
