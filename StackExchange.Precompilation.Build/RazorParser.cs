using System;
using System.Linq;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
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
    class RazorParser : TextLoader
    {
        public Compilation Compilation { get; }
        private readonly WebConfigurationFileMap _configMap;
        private readonly TextLoader _originalLoader;
        private readonly Workspace _workspace;
        private readonly TaskCompletionSource<TextAndVersion> _result;
            
        public RazorParser(Compilation compilation, TextLoader originalLoader, Workspace workspace)
        {
            Compilation = compilation;
            _configMap = new WebConfigurationFileMap { VirtualDirectories = { { "/", new VirtualDirectoryMapping(Compilation.CurrentDirectory.FullName, true) } } };
            _originalLoader = originalLoader;
            _workspace = workspace;
            _result = new TaskCompletionSource<TextAndVersion>();
        }

        public void Start()
        {
            Task.Run(async () => {
                try
                {
                    var result = await _originalLoader.LoadTextAndVersionAsync(_workspace, null, default(CancellationToken));
                    var sourceText = result.Text.ToString();
                    using (var sourceReader = new StringReader(sourceText))
                    using (var generatedStream = new MemoryStream())
                    {
                        WrapCshtmlReader(result.FilePath, sourceReader, generatedStream);

                        result = TextAndVersion.Create(SourceText.From(generatedStream, Compilation.Encoding, Compilation.CscArgs.ChecksumAlgorithm, canBeEmbedded: result.Text.CanBeEmbedded), result.Version, result.FilePath);
                        _result.SetResult(result);
                    }
                }
                catch(Exception ex)
                {
                    _result.SetException(ex);
                }
            });
        }

        public Task Result => _result.Task;
        public override Task<TextAndVersion> LoadTextAndVersionAsync(Workspace workspace, DocumentId documentId, CancellationToken cancellationToken) => _result.Task;

        private void WrapCshtmlReader(string sourcePath, TextReader sourceReader, MemoryStream generatedStream)
        {
            var viewFullPath = sourcePath;
            var viewVirtualPath = GetRelativeUri(sourcePath, Compilation.CurrentDirectory.FullName);
            var viewConfig = WebConfigurationManager.OpenMappedWebConfiguration(_configMap, viewVirtualPath);
            var razorConfig = viewConfig.GetSectionGroup("system.web.webPages.razor") as RazorWebSectionGroup;
            var host = razorConfig == null
                ? WebRazorHostFactory.CreateDefaultHost(viewVirtualPath, viewFullPath)
                : WebRazorHostFactory.CreateHostFromConfig(razorConfig, viewVirtualPath, viewFullPath);

            using (var provider = CodeDomProvider.CreateProvider("csharp"))
            using (var generatedWriter = new StreamWriter(generatedStream, Compilation.Encoding, 1024, leaveOpen: true))
            {
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

                var codeGenOptions = new CodeGeneratorOptions { VerbatimOrder = true, ElseOnClosing = false, BlankLinesBetweenMembers = false };
                provider.GenerateCodeFromCompileUnit(razorOut.GeneratedCode, generatedWriter, codeGenOptions);

                // rewind
                generatedWriter.Flush();
                generatedStream.Position = 0;
            }
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