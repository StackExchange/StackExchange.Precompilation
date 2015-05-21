using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace StackExchange.Precompilation
{
    class CSharpParser : Parser
    {
        public CSharpParser(Compilation compilation) : base (compilation)
        {
        }

        public override SyntaxTree GetSyntaxTree(string sourcePath, Stream sourceStream)
        {
            try
            {
                var sourceText = SourceText.From(sourceStream, Compilation.Encoding);
                return SyntaxFactory.ParseSyntaxTree(sourceText, Compilation.CscArgs.ParseOptions, sourcePath);
            }
            catch (Exception ex)
            {
                Compilation.Diagnostics.Add(Diagnostic.Create(Compilation.FailedParsingSourceTree, Compilation.AsLocation(sourcePath), ex.ToString()));
                return null;
            }
        }
    }
}