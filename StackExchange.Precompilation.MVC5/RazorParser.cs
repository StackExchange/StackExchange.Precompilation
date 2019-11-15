using System;
using System.Linq;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Web.Configuration;
using System.Web.Razor;
using System.Web.WebPages.Razor;
using System.Web.WebPages.Razor.Configuration;
using Microsoft.CodeAnalysis;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.CodeAnalysis.Text;
using RazorWorker = System.Func<System.Web.Razor.RazorEngineHost, Microsoft.CodeAnalysis.TextAndVersion, System.Threading.Tasks.Task<System.IO.Stream>>;
using System.Composition;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace StackExchange.Precompilation
{
    [ExportLanguageServiceFactoryAttribute(typeof(IDocumentExtender), LanguageNames.CSharp)]
    class RazorParserFactory : ILanguageServiceFactory
    {
        public ILanguageService CreateLanguageService(HostLanguageServices languageServices) =>
            new RazorParser(languageServices.WorkspaceServices.Workspace);
    }

    class RazorParser : IDocumentExtender, IDisposable
    {
        private readonly Workspace _workspace;
        private readonly WebConfigurationFileMap _configMap;
        private readonly DirectoryInfo _cacheDirectory;
        private readonly BlockingCollection<RazorTextLoader> _workItems;
        private readonly Lazy<Task> _backgroundWorkers;
        private readonly CancellationToken _cancellationToken;

        public RazorParser(Workspace workspace)
        {
            var dir = PrecompilerSection.Current?.RazorCache?.Directory;
//             dir = string.IsNullOrWhiteSpace(dir)
//                 ? Environment.GetEnvironmentVariable(nameof(Precompilation) + "_" + nameof(PrecompilerSection.RazorCache) + nameof(RazorCacheElement.Directory))
//                 : dir;
//             if (!string.IsNullOrWhiteSpace(dir) && ! dir exists)
//             Directory.CreateDirectory(Path.Combine(CscArgs.OutputDirectory, dir))

//             if (cacheDirectory.Exists != true)
//             {
//                 throw new ArgumentException($"Specified directory '{cacheDirectory.FullName}' doesn't exist.", nameof(cacheDirectory));
//             }

            _workItems = new BlockingCollection<RazorTextLoader>();
            _workspace = workspace;
            _configMap = new WebConfigurationFileMap { VirtualDirectories = { { "/", new VirtualDirectoryMapping(Environment.CurrentDirectory, true) } } };
            _backgroundWorkers = new Lazy<Task>(
                () => _cancellationToken.IsCancellationRequested
                    ? Task.CompletedTask
                    : Task.WhenAll(Enumerable.Range(0, Environment.ProcessorCount).Select(_ => Task.Run(BackgroundWorker, _cancellationToken))),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private async Task BackgroundWorker()
        {
            foreach(var loader in _workItems.GetConsumingEnumerable(_cancellationToken))
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    loader.Result.SetCanceled();
                    continue;
                }
                try
                {
                    var originalText = await loader.OriginalLoader.LoadTextAndVersionAsync(_workspace, null, default(CancellationToken));
                    var viewFullPath = originalText.FilePath;
                    var viewVirtualPath = GetRelativeUri(originalText.FilePath, Environment.CurrentDirectory);
                    var viewConfig = WebConfigurationManager.OpenMappedWebConfiguration(_configMap, viewVirtualPath);
                    var host = viewConfig.GetSectionGroup("system.web.webPages.razor") is RazorWebSectionGroup razorConfig
                        ? WebRazorHostFactory.CreateHostFromConfig(razorConfig, viewVirtualPath, viewFullPath)
                        : WebRazorHostFactory.CreateDefaultHost(viewVirtualPath, viewFullPath);

                    // having this as a field would require the ASP.NET MVC dependency even for console apps...
                    RazorWorker worker = RazorWorker;
                    if (_cacheDirectory != null)
                    {
                        worker = CachedRazorWorker;
                    }

                    using (var stream = await worker(host, originalText))
                    {
                        var generatedText = TextAndVersion.Create(
                            SourceText.From(stream, originalText.Text.Encoding, originalText.Text.ChecksumAlgorithm, canBeEmbedded: originalText.Text.CanBeEmbedded, throwIfBinaryDetected: true),
                            originalText.Version,
                            originalText.FilePath);

                        loader.Result.TrySetResult(generatedText);
                    }
                }
                catch (Exception ex)
                {
                    loader.Result.TrySetException(ex);
                }
            }
        }

        void IDisposable.Dispose()
        {
            _workItems?.Dispose();
        }

        private async Task<Stream> CachedRazorWorker(RazorEngineHost host, TextAndVersion originalText)
        {
            var cacheFile = GetCachedFileInfo();
            if (cacheFile.Exists)
            {
                return cacheFile.OpenRead();
            }
            else
            {
                (var success, var source) = await RazorWorkerImpl(host, originalText);

                FileStream fs = null;
                try
                {
                    if (success)
                    {
                        fs = cacheFile.Create();
                        await source.CopyToAsync(fs, 4096, _cancellationToken);
                        await fs.FlushAsync(_cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    ReportDiagnostic(Diagnostic.Create(Compilation.CachingFailed, Location.None, originalText.FilePath, cacheFile.FullName, ex));
                    for (var i = 0; i < 10 && cacheFile.Exists; i++)
                    {
                        await Task.Delay(100 * i);
                        try { cacheFile.Delete(); } catch { }
                    }
                    if (cacheFile.Exists)
                    {
                        ReportDiagnostic(Diagnostic.Create(Compilation.CachingFailedHard, Location.None, originalText.FilePath, cacheFile.FullName));
                    }
                }
                finally
                {
                    fs?.Dispose();
                    source.Position = 0;
                }

                return source; // return the in-memory stream, since it's faster
            }


            FileInfo GetCachedFileInfo()
            {
                using (var md5 = MD5.Create())
                using (var str = new MemoryStream())
                using (var sw = new StreamWriter(str))
                {

                    // all those things can affect the generated c#
                    // so we need to include them in the hash...
                    sw.WriteLine(host.CodeLanguage.LanguageName);
                    sw.WriteLine(host.CodeLanguage.CodeDomProviderType.FullName);
                    sw.WriteLine(host.DefaultBaseClass);
                    sw.WriteLine(host.DefaultClassName);
                    sw.WriteLine(host.DefaultNamespace);
                    sw.WriteLine(string.Join(";",host.NamespaceImports));
                    sw.WriteLine(host.StaticHelpers);
                    sw.WriteLine(host.TabSize);
                    sw.WriteLine(originalText.FilePath);
                    originalText.Text.Write(sw, _cancellationToken); // .cshtml content

                    sw.Flush();
                    str.Position = 0;
                    var hashBytes = md5.ComputeHash(str);
                    var fileName = BitConverter.ToString(hashBytes).Replace("-","") + ".cs";
                    var filePath = Path.Combine(_cacheDirectory.FullName, fileName);
                    return new FileInfo(filePath);
                }
            }
        }
        private readonly ConcurrentBag<Diagnostic> _diagnostics = new ConcurrentBag<Diagnostic>();
        private void ReportDiagnostic(Diagnostic d) => _diagnostics.Add(d);

        private Task<Stream> RazorWorker(RazorEngineHost host, TextAndVersion originalText) =>
            RazorWorkerImpl(host, originalText).ContinueWith(x => x.Result.result, TaskContinuationOptions.OnlyOnRanToCompletion);

        private Task<(bool success, Stream result)> RazorWorkerImpl(RazorEngineHost host, TextAndVersion originalText)
        {
            var success = true;
            var generatedStream = new MemoryStream(capacity: originalText.Text.Length * 8); // generated .cs files contain a lot of additional crap vs actualy cshtml
            var viewFullPath = originalText.FilePath;
            using (var sourceReader = new StreamReader(generatedStream, originalText.Text.Encoding, false, 4096, leaveOpen: true))
            using (var provider = CodeDomProvider.CreateProvider("csharp"))
            using (var generatedWriter = new StreamWriter(generatedStream, originalText.Text.Encoding, 4096, leaveOpen: true))
            {
                // write cshtml into generated stream and rewind
                originalText.Text.Write(generatedWriter);
                generatedWriter.Flush();
                generatedStream.Position = 0;

                // generated code and clear memory stream
                var engine = new RazorTemplateEngine(host);
                var razorOut = engine.GenerateCode(sourceReader, null, null, viewFullPath);

                if (!razorOut.Success)
                {
                    success = false;
                    foreach(var error in razorOut.ParserErrors)
                    {
                        var position = new LinePosition(error.Location.LineIndex, error.Location.CharacterIndex - 1);
                        ReportDiagnostic(Diagnostic.Create(
                            Compilation.RazorParserError,
                            Location.Create(
                                originalText.FilePath,
                                new TextSpan(error.Location.AbsoluteIndex, error.Length),
                                new LinePositionSpan(position, position)),
                            error.Message));
                    }
                }

                // add the CompiledFromFileAttribute to the generated class
                razorOut.GeneratedCode
                    .Namespaces.OfType<CodeNamespace>().FirstOrDefault()?
                    .Types.OfType<CodeTypeDeclaration>().FirstOrDefault()?
                    .CustomAttributes.Add(
                        new CodeAttributeDeclaration(
                            new CodeTypeReference(typeof(CompiledFromFileAttribute)),
                            new CodeAttributeArgument(new CodePrimitiveExpression(viewFullPath))
                        ));

                // reuse the memory stream for code generation
                generatedStream.Position = 0;
                generatedStream.SetLength(0);
                var codeGenOptions = new CodeGeneratorOptions { VerbatimOrder = true, ElseOnClosing = false, BlankLinesBetweenMembers = false };
                provider.GenerateCodeFromCompileUnit(razorOut.GeneratedCode, generatedWriter, codeGenOptions);

                // rewind
                generatedWriter.Flush();
                generatedStream.Position = 0;
            }

            return Task.FromResult((success: success, stream: (Stream)generatedStream));
        }

        private string GetRelativeUri(string filespec, string folder)
        {
            Uri pathUri = new Uri(filespec);
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }
            Uri folderUri = new Uri(folder);
            return "/" + folderUri.MakeRelativeUri(pathUri).ToString().TrimStart('/');
        }

        public DocumentInfo Extend(DocumentInfo document)
        {
            if (Path.GetExtension(document.Name) != ".cshtml") return document;

            var razorLoader = new RazorTextLoader(this, document.TextLoader);
            _workItems.Add(razorLoader);
            return document.WithTextLoader(razorLoader);
        }

        public async Task<System.Collections.Generic.ICollection<Diagnostic>> Complete()
        {
            _workItems.CompleteAdding();
            if (_backgroundWorkers.IsValueCreated || !_workItems.IsCompleted)
            {
                await _backgroundWorkers.Value;
            }
            return _diagnostics.ToList();
        }

        private Task EnsureWorkers() => _backgroundWorkers.Value;

        private sealed class RazorTextLoader : TextLoader
        {
            public TextLoader OriginalLoader { get; }
            public TaskCompletionSource<TextAndVersion> Result { get; }

            private readonly RazorParser _parser;

            public RazorTextLoader(RazorParser parser, TextLoader originalLoader)
            {
                _parser = parser;
                OriginalLoader = originalLoader;
                Result =  new TaskCompletionSource<TextAndVersion>();
            }

            private Task _worker;
            public override Task<TextAndVersion> LoadTextAndVersionAsync(Workspace workspace, DocumentId documentId, CancellationToken cancellationToken)
            {
                _worker = _worker ?? _parser.EnsureWorkers(); // ensuring that lazy workers are running
                return Result.Task;
            }
        }
    }
}