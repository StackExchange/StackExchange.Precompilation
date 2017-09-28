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

namespace StackExchange.Precompilation
{
    class RazorParser : IDisposable
    {
        private readonly Compilation _compilation;
        private readonly Workspace _workspace;
        private readonly WebConfigurationFileMap _configMap;
        private readonly DirectoryInfo _cacheDirectory;
        private readonly MD5 _md5;
        private readonly Func<TextAndVersion, Stream> _worker;
        private readonly BlockingCollection<RazorTextLoader> _workItems;
        private readonly Lazy<Task> _backgroundWorkers;

        public RazorParser(Workspace workspace, Compilation compilation, DirectoryInfo cacheDirectory)
            : this (workspace, compilation)
        {
            if (cacheDirectory == null)
            {
                throw new ArgumentNullException(nameof(cacheDirectory));
            }
            if (cacheDirectory.Exists != true)
            {
                throw new ArgumentException($"Specified directory '{cacheDirectory.FullName}' doesn't exist.", nameof(cacheDirectory));
            }
            _cacheDirectory = cacheDirectory;
            _md5 = MD5.Create();
            _worker = CachedRazorWorker;
        }

        public RazorParser(Workspace workspace, Compilation compilation)
        {
            _workItems = new BlockingCollection<RazorTextLoader>();
            _workspace = workspace;
            _compilation = compilation;
            _configMap = new WebConfigurationFileMap { VirtualDirectories = { { "/", new VirtualDirectoryMapping(compilation.CurrentDirectory.FullName, true) } } };
            _worker = RazorWorker;
            _backgroundWorkers = new Lazy<Task>(
                () => Task.WhenAll(Enumerable.Range(0, Environment.ProcessorCount).Select(_ => Task.Run(BackgroundWorker))),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private async Task BackgroundWorker()
        {
            foreach(var loader in _workItems.GetConsumingEnumerable())
            {
                try
                {
                    var originalText = await loader.OriginalLoader.LoadTextAndVersionAsync(_workspace, null, default(CancellationToken));
                    using (var stream = _worker(originalText))
                    {
                        var generatedText = TextAndVersion.Create(
                            SourceText.From(stream, _compilation.Encoding, _compilation.CscArgs.ChecksumAlgorithm, canBeEmbedded: originalText.Text.CanBeEmbedded, throwIfBinaryDetected: true),
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
            _md5?.Dispose();
            _workItems?.Dispose();
        }

        private Stream CachedRazorWorker(TextAndVersion originalText)
        {
            var cacheFile = GetCachedFileInfo();
            if (cacheFile.Exists)
            {
                return cacheFile.OpenRead();
            }
            else
            {
                var source = RazorWorker(originalText);
                using (var fs = cacheFile.Create())
                {
                    source.CopyTo(fs);
                    source.Position = 0;
                }
                return source; // return the in-memory stream, since it's faster
            }

            FileInfo GetCachedFileInfo()
            {
                using (var str = new MemoryStream())
                using (var sw = new StreamWriter(str))
                {
                    // todo should VersionStamp affect this as well?
                    sw.Write(originalText.FilePath);
                    sw.Write(originalText.Version);
                    originalText.Text.Write(sw);
                    sw.Flush();
                    str.Position = 0;
                    var hashBytes = _md5.ComputeHash(str);
                    var fileName = BitConverter.ToString(hashBytes).Replace("-","") + ".cs";
                    var filePath = Path.Combine(_cacheDirectory.FullName, fileName);
                    return new FileInfo(filePath);
                }
            }
        }

        private Stream RazorWorker(TextAndVersion originalText)
        {
            var generatedStream = new MemoryStream(capacity: originalText.Text.Length * 8); // generated .cs files contain a lot of additional crap vs actualy cshtml
            var viewFullPath = originalText.FilePath;
            var viewVirtualPath = GetRelativeUri(originalText.FilePath, _compilation.CurrentDirectory.FullName);
            var viewConfig = WebConfigurationManager.OpenMappedWebConfiguration(_configMap, viewVirtualPath);
            var host = viewConfig.GetSectionGroup("system.web.webPages.razor") is RazorWebSectionGroup razorConfig
                ? WebRazorHostFactory.CreateHostFromConfig(razorConfig, viewVirtualPath, viewFullPath)
                : WebRazorHostFactory.CreateDefaultHost(viewVirtualPath, viewFullPath);
            using (var sourceReader = new StreamReader(generatedStream, _compilation.Encoding, false, 4096, leaveOpen: true))
            using (var provider = CodeDomProvider.CreateProvider("csharp"))
            using (var generatedWriter = new StreamWriter(generatedStream, _compilation.Encoding, 4096, leaveOpen: true))
            {
                // write cshtml into generated stream and rewind
                originalText.Text.Write(generatedWriter);
                generatedWriter.Flush();
                generatedStream.Position = 0;

                // generated code and clear memory stream
                var engine = new RazorTemplateEngine(host);
                var razorOut = engine.GenerateCode(sourceReader, null, null, viewFullPath);

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

            return generatedStream;

            string GetRelativeUri(string filespec, string folder)
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

        public DocumentInfo Wrap(DocumentInfo document)
        {
            var razorLoader = new RazorTextLoader(this, document.TextLoader);
            _workItems.Add(razorLoader);
            return document.WithTextLoader(razorLoader);
        }

        public Task Complete()
        {
            _workItems.CompleteAdding();
            if (_backgroundWorkers.IsValueCreated || !_workItems.IsCompleted)
            {
                return _backgroundWorkers.Value;
            }
            else
            {
                return Task.CompletedTask;
            }
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