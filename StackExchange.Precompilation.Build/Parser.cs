using System.IO;
using Microsoft.CodeAnalysis;

namespace StackExchange.Precompilation
{
    abstract class Parser
    {
        protected Compilation Compilation { get; private set; }
        protected Parser(Compilation compilation)
        {
            Compilation = compilation;
        }
        public abstract SyntaxTree GetSyntaxTree(string sourcePath, Stream sourceStream);
    }
}