using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompilation
{
    public class AfterCompileContext
    {
        public CSharpCommandLineArguments Arguments { get; set; }

        public CSharpCompilation Compilation { get; set; }

        public Stream AssemblyStream { get; set; }

        public Stream SymbolStream { get; set; }

        public Stream XmlDocStream { get; set; }

        public IList<Diagnostic> Diagnostics { get; set; }
    }
}