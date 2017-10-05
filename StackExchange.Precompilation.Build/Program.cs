using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompilation
{
    static class Program
    {
        static void Main(string[] args)
        {
            try
            {
                if (!CompilationProxy.RunCs(args))
                {
                    Environment.ExitCode = 1;
                }
            }
            catch (Exception ex)
            {
                var agg = ex as AggregateException;
                Console.WriteLine("ERROR: An unhandled exception occured");
                if (agg != null)
                {
                    agg = agg.Flatten();
                    foreach (var inner in agg.InnerExceptions)
                    {
                        Console.Error.WriteLine("error: " + inner);
                    }
                }
                else
                {
                    Console.Error.WriteLine("error: " + ex);
                }
                Environment.ExitCode = 2;
            }
        }

    }
}
