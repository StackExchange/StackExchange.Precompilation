using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompilation
{
    public class AfterCompileContext : ICompileContext
    {
        public CSharpCommandLineArguments Arguments { get; internal set; }

        public CSharpCompilation Compilation { get; internal set; }

        public Stream AssemblyStream { get; internal set; }

        public Stream SymbolStream { get; internal set; }

        public Stream XmlDocStream { get; internal set; }

        public IList<Diagnostic> Diagnostics { get; internal set; }
    }
}