using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompilation
{
    public interface IBeforeCompileContext
    {
        CSharpCommandLineArguments Arguments { get; }

        CSharpCompilation Compilation { get; set; }

        IList<ResourceDescription> Resources { get; }

        IList<Diagnostic> Diagnostics { get; }
    }
}