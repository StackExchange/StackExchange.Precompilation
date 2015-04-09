using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompilation
{
    public interface IAfterCompileContext
    {
        CSharpCommandLineArguments Arguments { get; }

        CSharpCompilation Compilation { get; }

        Stream AssemblyStream { get; set; }

        Stream SymbolStream { get; set; }

        Stream XmlDocStream { get; set; }

        IList<Diagnostic> Diagnostics { get; }
    }
}