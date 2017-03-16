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
        private readonly Compilation _compilation;
        private readonly WebConfigurationFileMap _configMap;
        private readonly Task<TextAndVersion> _result;
        private readonly CancellationTokenSource _cts;

        public RazorParser(Compilation compilation, TextLoader originalLoader, Workspace workspace, CancellationTokenSource cts)
        {
            _cts = cts;
            _compilation = compilation;
            _configMap = new WebConfigurationFileMap { VirtualDirectories = { { "/", new VirtualDirectoryMapping(_compilation.CurrentDirectory.FullName, true) } } };
            _result = Task.Run(async () => 
            {
                var result = await originalLoader.LoadTextAndVersionAsync(workspace, null, _cts.Token);
                var sourceText = result.Text.ToString();
                using (var sourceReader = new StringReader(sourceText))
                using (var generatedStream = new MemoryStream())
                {
                    WrapCshtmlReader(result.FilePath, sourceReader, generatedStream);

                    result = TextAndVersion.Create(SourceText.From(generatedStream, _compilation.Encoding, _compilation.CscArgs.ChecksumAlgorithm, canBeEmbedded: result.Text.CanBeEmbedded), result.Version, result.FilePath);
                }
                return result;
            }, _cts.Token);
        }

        public override Task<TextAndVersion> LoadTextAndVersionAsync(Workspace workspace, DocumentId documentId, CancellationToken cancellationToken)
        {
            cancellationToken.Register(_cts.Cancel);
            return _result;
        }

        private void WrapCshtmlReader(string sourcePath, TextReader sourceReader, MemoryStream generatedStream)
        {
            var viewFullPath = sourcePath;
            var viewVirtualPath = GetRelativeUri(sourcePath, _compilation.CurrentDirectory.FullName);
            var viewConfig = WebConfigurationManager.OpenMappedWebConfiguration(_configMap, viewVirtualPath);
            var razorConfig = viewConfig.GetSectionGroup("system.web.webPages.razor") as RazorWebSectionGroup;
            var host = razorConfig == null
                ? WebRazorHostFactory.CreateDefaultHost(viewVirtualPath, viewFullPath)
                : WebRazorHostFactory.CreateHostFromConfig(razorConfig, viewVirtualPath, viewFullPath);

            using (var provider = CodeDomProvider.CreateProvider("csharp"))
            using (var generatedWriter = new StreamWriter(generatedStream, _compilation.Encoding, 1024, leaveOpen: true))
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