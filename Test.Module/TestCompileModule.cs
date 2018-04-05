using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using StackExchange.Precompilation;

namespace Test.Module
{
    public class TestCompileModule : ICompileModule
    {
        public void BeforeCompile(BeforeCompileContext context)
        {
            // this can potentially run multiple times (for every view compiled at runtime) in RoslynRazorViewEngine;
            if(context.Compilation.GetTypeByMetadataName("Test.Module.Extensions") != null) return;

            context.Diagnostics.Add(
                Diagnostic.Create(
                    new DiagnosticDescriptor("TEST", "TEST", "Hello meta programming world!", "TEST", DiagnosticSeverity.Info, true),
                    Location.None));

            context.Compilation = context.Compilation.AddSyntaxTrees(
                SyntaxFactory.ParseSyntaxTree(@"
namespace Test.Module
{
    public static class Extensions
    {
        public static T Dump<T>(this T i)
        {
            if (i != null)
            {
                System.Console.WriteLine(i);
            }
            return i;
        }
    }
}
", context.Arguments.ParseOptions));
        }

        public void AfterCompile(AfterCompileContext context)
        {
        }
    }
}
