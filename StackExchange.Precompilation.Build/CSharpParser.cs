using System;
using System.IO;
using System.Reflection;
using System.Text;
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

        // https://github.com/dotnet/roslyn/blob/master/src/Compilers/Core/Portable/CommandLine/CommonCompiler.cs#L149
        private delegate SourceText _CreateSourceText(Stream stream, Encoding encoding, SourceHashAlgorithm hash);
        private static Type EncodedStringText = Type.GetType("Microsoft.CodeAnalysis.Text.EncodedStringText, Microsoft.CodeAnalysis");
        private static MethodInfo EncodedStringTextCreateMethod = EncodedStringText.GetMethod(
            "Create",
            BindingFlags.NonPublic | BindingFlags.Static,
            Type.DefaultBinder,
            new[] {typeof (Stream), typeof(Encoding), typeof(SourceHashAlgorithm)},
            null);
        private static _CreateSourceText CreateSourceText = (_CreateSourceText) Delegate.CreateDelegate(typeof (_CreateSourceText), null, EncodedStringTextCreateMethod);

        private static Type SyntaxTreeType = typeof (SyntaxTree);
        private static MethodInfo GetMappedLineSpanAndVisibility = SyntaxTreeType.GetMethod(
            "GetMappedLineSpanAndVisibility",
            BindingFlags.NonPublic | BindingFlags.Instance,
            Type.DefaultBinder,
            new[] {typeof (TextSpan), typeof(bool).MakeByRefType()},
            null);

        public override SyntaxTree GetSyntaxTree(string sourcePath, Stream sourceStream, CSharpParseOptions parseOptions)
        {
            try
            {
                var sourceText = CreateSourceText(sourceStream, Compilation.Encoding,  Compilation.CscArgs.ChecksumAlgorithm);
                var tree = SyntaxFactory.ParseSyntaxTree(sourceText, parseOptions, sourcePath);

                // prepopulate line tables
                // https://github.com/dotnet/roslyn/blob/4692040255a63ded0b3924f066947b5c6ec2ec48/src/Compilers/CSharp/Portable/CommandLine/CSharpCompiler.cs#L181-182
                GetMappedLineSpanAndVisibility.Invoke(tree, new object[] {default(TextSpan), false});
                return tree;
            }
            catch (Exception ex)
            {
                Compilation.Diagnostics.Add(Diagnostic.Create(Compilation.FailedParsingSourceTree, Compilation.AsLocation(sourcePath), ex.ToString()));
                return null;
            }
        }
    }
}