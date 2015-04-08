using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompiler
{
    public interface ICompileModule
    {
        void BeforeCompile(IBeforeCompileContext context);

        void AfterCompile(IAfterCompileContext context);
    }

    public interface IBeforeCompileContext
    {
        CSharpCommandLineArguments Arguments { get; }

        CSharpCompilation Compilation { get; set; }

        IList<ResourceDescription> Resources { get; }

        IList<Diagnostic> Diagnostics { get; }
    }

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