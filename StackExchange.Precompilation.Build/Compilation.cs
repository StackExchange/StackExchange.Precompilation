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
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Emit;

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
                Encoding = CscArgs.Encoding ?? Encoding.UTF8;

                var references = SetupReferences();
                var sources = LoadSources(CscArgs.SourceFiles.Select(x => x.Path).ToArray());

                var compilationModules = LoadModules().ToList();

                var compilation = CSharpCompilation.Create(
                    options: CscArgs.CompilationOptions.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default),
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

            using (var peStream = File.Create(outputPath))
            using (var pdbStream = !string.IsNullOrWhiteSpace(pdbPath) ? File.Create(pdbPath) : null)
            using (var xmlDocumentationStream = !string.IsNullOrWhiteSpace(CscArgs.DocumentationPath) ? File.Create(CscArgs.DocumentationPath) : null)
            using (var win32Resources = compilation.CreateDefaultWin32Resources(
                versionResource: true,
                noManifest: CscArgs.NoWin32Manifest,
                manifestContents: !string.IsNullOrWhiteSpace(CscArgs.Win32Manifest) ? new MemoryStream(File.ReadAllBytes(CscArgs.Win32Manifest)) : null,
                iconInIcoFormat: !string.IsNullOrWhiteSpace(CscArgs.Win32Icon) ? new MemoryStream(File.ReadAllBytes(CscArgs.Win32Icon)) : null))
            {
                var emitResult = compilation.Emit(
                    peStream: peStream,
                    pdbStream: pdbStream,
                    xmlDocumentationStream: xmlDocumentationStream,
                    options: CscArgs.EmitOptions,
                    manifestResources: CscArgs.ManifestResources,
                    win32Resources: win32Resources);

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

                return emitResult;
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
            return _precompilationCommandLineArgs.References.Select(x => (MetadataReference)MetadataReference.CreateFromFile(x, MetadataReferenceProperties.Assembly)).ToArray();
        }

        private IEnumerable<SyntaxTree> LoadSources(ICollection<string> paths)
        {
            var trees = new SyntaxTree[paths.Count];
            Parallel.ForEach(paths,
                (file, state, index) =>
                {
                    var ext = Path.GetExtension(file) ?? "";
                    Lazy<Parser> parser;
                    if(_syntaxTreeLoaders.TryGetValue(ext, out parser))
                    {
                        using (var sourceStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            trees[index] = parser.Value.GetSyntaxTree(file, sourceStream);
                        }
                    }
                    else
                    {
                        Diagnostics.Add(Diagnostic.Create(UnknownFileType, AsLocation(file), ext));
                    }
                });

            return trees.Where(x => x != null);
        }

        public Location AsLocation(string path)
        {
            return Location.Create(path, new TextSpan(), new LinePositionSpan());
        }
    }
}