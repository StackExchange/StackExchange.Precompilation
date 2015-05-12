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
                if (args.Contains("/help", StringComparer.OrdinalIgnoreCase) 
                 || args.Contains("/?", StringComparer.OrdinalIgnoreCase))
                {
                    PrintHelp();
                }
                else
                {
                    var cscArgs = CSharpCommandLineParser.Default.Parse(args, Directory.GetCurrentDirectory());
                    if (cscArgs.Errors.Any())
                    {
                        foreach (var diagnostic in cscArgs.Errors)
                        {
                            Console.Error.WriteLine(diagnostic.ToString());
                        }
                        PrintHelp();
                        Console.WriteLine("ERROR: invalid invocation syntax");
                    }

                    Console.WriteLine("Starting in csc mode");
                    if (!CompilationProxy.RunCs(cscArgs, args))
                    {
                        Environment.ExitCode = 1;
                    }
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
                        Console.Error.WriteLine(inner);
                    }
                }
                else
                {
                    Console.Error.WriteLine(ex);
                }
                Environment.ExitCode = 2;
            }
        }

        private static void PrintHelp()
        {
            var assembly = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            Console.WriteLine("USAGE: execute {0} instead of csc.exe to compile .cs and .cshtml files", assembly);
        }
    }
}
