using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompilation
{
    public class BeforeCompileContext : ICompileContext
    {
        public CSharpCommandLineArguments Arguments { get; set; }

        public CSharpCompilation Compilation { get; set; }

        public IList<Diagnostic> Diagnostics { get; internal set; }
    }
}