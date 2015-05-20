using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Configuration;
using System.Web.Razor;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Emit;

namespace StackExchange.Precompilation
{
    class Compilation
    {
        private readonly CSharpCommandLineArguments _cscArgs;
        private readonly DirectoryInfo _currentDirectory;
        private readonly Encoding _encoding;
        private readonly WebConfigurationFileMap _configMap;
        private readonly List<Diagnostic> _diagnostics;

        private const string DiagnosticCategory = "StackExchange.Precompilation";
        private static DiagnosticDescriptor FailedToCreateModule =
            new DiagnosticDescriptor("SE001", "Failed to instantiate ICompileModule", "Failed to instantiate ICompileModule '{0}': {1}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        private static DiagnosticDescriptor UnknownFileType =
            new DiagnosticDescriptor("SE002", "Unknown file type", "Unknown file type '{0}'", DiagnosticCategory, DiagnosticSeverity.Error, true);
        private static DiagnosticDescriptor ViewGenerationFailed =
            new DiagnosticDescriptor("SE003", "View generation failed", "View generation failed: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);
        private static DiagnosticDescriptor FailedParsingSourceTree =
            new DiagnosticDescriptor("SE004", "Failed parsing source tree", "Failed parasing source tree: {0}", DiagnosticCategory, DiagnosticSeverity.Error, true);


        public Compilation(CSharpCommandLineArguments cscArgs, DirectoryInfo currentDirectory, Encoding defaultEncoding = null)
        {
            _diagnostics = new List<Diagnostic>();
            _cscArgs = cscArgs;
            _currentDirectory = currentDirectory;
            _encoding = _cscArgs.Encoding ?? defaultEncoding ?? Encoding.UTF8;

            _configMap = new WebConfigurationFileMap { VirtualDirectories = { { "/", new VirtualDirectoryMapping(currentDirectory.FullName, true) } } };

            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(currentDirectory.FullName, "App_Data")); // HACK mocking ASP.NET's ~/App_Data aka. |DataDirectory|
        }

        public bool Run()
        {
            try
            {
                var references = SetupReferences();
                var sources = LoadSources(_cscArgs.SourceFiles.Select(x => x.Path).ToArray());

                var compilationModules = LoadModules().ToList();

                var compilation = CSharpCompilation.Create(
                    options: _cscArgs.CompilationOptions.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default),
                    references: references,
                    syntaxTrees: sources,
                    assemblyName: _cscArgs.CompilationName);

                var context = new CompileContext(compilationModules);

                context.Before(new BeforeCompileContext
                {
                    Arguments = _cscArgs,
                    Compilation = compilation,
                    Diagnostics = _diagnostics,
                    Resources = _cscArgs.ManifestResources.ToList()
                });

                var emitResult = Emit(context);
                return emitResult.Success;
            }
            finally
            {
                _diagnostics.ForEach(x => Console.WriteLine(x.ToString())); // strings only, since the Console.Out textwriter is another app domain...
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
                    _diagnostics.Add(Diagnostic.Create(
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
            var pdbPath = _cscArgs.PdbPath;
            var outputPath = Path.Combine(_cscArgs.OutputDirectory, _cscArgs.OutputFileName);

            if (!_cscArgs.EmitPdb)
            {
                pdbPath = null;
            }
            else if (string.IsNullOrWhiteSpace(pdbPath))
            {
                pdbPath = Path.ChangeExtension(outputPath, ".pdb");
            }

            using (var peStream = File.Create(outputPath))
            using (var pdbStream = !string.IsNullOrWhiteSpace(pdbPath) ? File.Create(pdbPath) : null)
            using (var xmlDocumentationStream = !string.IsNullOrWhiteSpace(_cscArgs.DocumentationPath) ? File.Create(_cscArgs.DocumentationPath) : null)
            using (var win32Resources = compilation.CreateDefaultWin32Resources(
                versionResource: true,
                noManifest: _cscArgs.NoWin32Manifest,
                manifestContents: !string.IsNullOrWhiteSpace(_cscArgs.Win32Manifest) ? new MemoryStream(File.ReadAllBytes(_cscArgs.Win32Manifest)) : null,
                iconInIcoFormat: !string.IsNullOrWhiteSpace(_cscArgs.Win32Icon) ? new MemoryStream(File.ReadAllBytes(_cscArgs.Win32Icon)) : null))
            {
                var emitResult = compilation.Emit(
                    peStream: peStream,
                    pdbStream: pdbStream,
                    xmlDocumentationStream: xmlDocumentationStream,
                    options: _cscArgs.EmitOptions,
                    manifestResources: _cscArgs.ManifestResources,
                    win32Resources: win32Resources);

                _diagnostics.AddRange(emitResult.Diagnostics);
                context.After(new AfterCompileContext
                {
                    Arguments = _cscArgs,
                    AssemblyStream = peStream,
                    Compilation = compilation,
                    Diagnostics = _diagnostics,
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
            var references = _cscArgs.MetadataReferences.Select(x => (MetadataReference)MetadataReference.CreateFromFile(x.Reference, x.Properties)).ToArray();
            var referenceAssemblies = references.Select(x =>
            {
                try
                {
                    return Assembly.ReflectionOnlyLoadFrom(x.Display);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("warning: could not load reference '{0}' - {1} - {2}", x.Display, ex.Message, ex.StackTrace);
                    return null;
                }
            }).Where(x => x != null).ToArray();
            var fullLookup = referenceAssemblies.ToDictionary(x => x.FullName);
            var shortLookup = referenceAssemblies.ToDictionary(x => x.GetName().Name);
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                Assembly a;
                if (fullLookup.TryGetValue(e.Name, out a) || shortLookup.TryGetValue(e.Name, out a))
                {
                    return Assembly.LoadFile(a.Location);
                }
                return null;
            };
            return references;
        }

        private SyntaxTree ParseSyntaxTreeAndDispose(Stream stream, string path, Encoding encoding = null)
        {
            try
            {
                var sourceText = SourceText.From(stream, encoding ?? _encoding);
                return SyntaxFactory.ParseSyntaxTree(sourceText, _cscArgs.ParseOptions, path);
            }
            catch (Exception ex)
            {
                _diagnostics.Add(Diagnostic.Create(FailedParsingSourceTree, AsLocation(path), ex.ToString()));
                return null;
            }
            finally
            {
                stream.Dispose();
            }
        }

        private SyntaxTree ParseSyntaxTree(string source, string path, Encoding encoding = null)
        {
            encoding = encoding ?? _encoding;
            return ParseSyntaxTreeAndDispose(new MemoryStream(encoding.GetBytes(source)), path, encoding);
        }

        private IEnumerable<SyntaxTree> LoadSources(ICollection<string> paths)
        {
            var trees = new SyntaxTree[paths.Count];
            Parallel.ForEach(paths,
                (file, state, index) =>
                {
                    var ext = Path.GetExtension(file);
                    switch (ext)
                    {
                        case ".cs":
                            trees[index] = ParseSyntaxTreeAndDispose(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read), file);
                            break;
                        case ".cshtml":
                            var viewFullPath = file;
                            var viewVirtualPath = GetRelativeUri(file, _currentDirectory.FullName);
                            try
                            {
                                var viewConfig = WebConfigurationManager.OpenMappedWebConfiguration(_configMap, viewVirtualPath);
                                var razorConfig = viewConfig.GetSectionGroup("system.web.webPages.razor") as System.Web.WebPages.Razor.Configuration.RazorWebSectionGroup;
                                var host = razorConfig == null
                                    ? System.Web.WebPages.Razor.WebRazorHostFactory.CreateDefaultHost(viewVirtualPath, viewFullPath)
                                    : System.Web.WebPages.Razor.WebRazorHostFactory.CreateHostFromConfig(razorConfig, viewVirtualPath, viewFullPath);
                                var sbSource = new StringBuilder();
                                using (var str = new FileStream(viewFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                using (var rdr = new StreamReader(str, _encoding, detectEncodingFromByteOrderMarks: true))
                                using (var provider = CodeDomProvider.CreateProvider("csharp"))
                                using (var typeWr = new StringWriter(sbSource))
                                {
                                    var engine = new RazorTemplateEngine(host);
                                    var razorOut = engine.GenerateCode(rdr, null, null, viewFullPath);
                                    var codeGenOptions = new CodeGeneratorOptions { VerbatimOrder = true, ElseOnClosing = false, BlankLinesBetweenMembers = false };
                                    provider.GenerateCodeFromCompileUnit(razorOut.GeneratedCode, typeWr, codeGenOptions);
                                    trees[index] = ParseSyntaxTree(sbSource.ToString(), viewFullPath, rdr.CurrentEncoding);
                                }
                            }
                            catch (Exception ex)
                            {
                                _diagnostics.Add(Diagnostic.Create(ViewGenerationFailed, AsLocation(file), ex.ToString()));
                            }
                            break;
                        default:
                            _diagnostics.Add(Diagnostic.Create(UnknownFileType, AsLocation(file), ext));
                            break;
                    }
                });
            return trees.Where(x => x != null);
        }

        private static Location AsLocation(string path)
        {
            return Location.Create(path, new TextSpan(), new LinePositionSpan());
        }

        private static string GetRelativeUri(string filespec, string folder)
        {
            Uri pathUri = new Uri(filespec);
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }
            Uri folderUri = new Uri(folder);
            return "/" + folderUri.MakeRelativeUri(pathUri).ToString().TrimStart('/');
        }
    }
}