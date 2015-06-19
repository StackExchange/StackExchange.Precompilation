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

namespace StackExchange.Precompilation
{
    class RazorParser : CSharpParser
    {
        private readonly WebConfigurationFileMap _configMap;

        public RazorParser(Compilation compilation) : base(compilation)
        {
            _configMap = new WebConfigurationFileMap { VirtualDirectories = { { "/", new VirtualDirectoryMapping(Compilation.CurrentDirectory.FullName, true) } } };
        }

        public override SyntaxTree GetSyntaxTree(string sourcePath, Stream sourceStream)
        {
            try
            {
                var viewFullPath = sourcePath;
                var viewVirtualPath = GetRelativeUri(sourcePath, Compilation.CurrentDirectory.FullName);
                var viewConfig = WebConfigurationManager.OpenMappedWebConfiguration(_configMap, viewVirtualPath);
                var razorConfig = viewConfig.GetSectionGroup("system.web.webPages.razor") as RazorWebSectionGroup;
                var host = razorConfig == null
                    ? WebRazorHostFactory.CreateDefaultHost(viewVirtualPath, viewFullPath)
                    : WebRazorHostFactory.CreateHostFromConfig(razorConfig, viewVirtualPath, viewFullPath);

                using (var rdr = new StreamReader(sourceStream, Compilation.Encoding, detectEncodingFromByteOrderMarks: true))
                using (var provider = CodeDomProvider.CreateProvider("csharp"))
                using (var generatedStream = new MemoryStream())
                using (var generatedWriter = new StreamWriter(generatedStream, Compilation.Encoding))
                {
                    var engine = new RazorTemplateEngine(host);
                    var razorOut = engine.GenerateCode(rdr, null, null, viewFullPath);

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

                    return base.GetSyntaxTree(sourcePath, generatedStream);
                }
            }
            catch (Exception ex)
            {
                Compilation.Diagnostics.Add(Diagnostic.Create(Compilation.ViewGenerationFailed, Compilation.AsLocation(sourcePath), ex.ToString()));
                return null;
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