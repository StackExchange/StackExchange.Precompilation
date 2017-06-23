using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompilation
{
    public interface ICompileContext
    {
        CSharpCommandLineArguments Arguments { get; }

        CSharpCompilation Compilation { get; }

        IList<Diagnostic> Diagnostics { get; }
    }
}
