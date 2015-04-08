using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
namespace StackExchange.Precompiler
{
    public static class Program
    {
        static void Main(string[] args)
        {
            try
            {
                if (args.Contains("/help", StringComparer.OrdinalIgnoreCase) 
                 || args.Contains("/?", StringComparer.OrdinalIgnoreCase))
                {
                    PrintHelp(false);
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
                        PrintHelp(true);
                    }
                    Console.WriteLine("Starting in csc mode");
                    CompilationProxy.RunCs(cscArgs, args);
                }
            }
            catch (Exception ex)
            {
                if (Walker_TexasRanger(ex))
                {
                    Environment.ExitCode = -1;
                }
                else
                {
                    Console.Error.WriteLine("error: an unhandled exception occured - {0} - {1}", ex.Message, ex.StackTrace);
                    Environment.ExitCode = -2;
                }
            }
        }

        private static void PrintHelp(bool @throw)
        {
            var assembly = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            Console.WriteLine("USAGE: execute {0} instead of csc.exe to compile .cs and .cshtml files", assembly);

            if (@throw)
            {
                throw Compilation.GetMsBuildError(text: "invalid invocation syntax");
            }
        }

        static bool Walker_TexasRanger(Exception ex)
        {
            var com = ex as CompilationFailedException;
            var agg = ex as AggregateException;
            var handled = false;
            if (com != null)
            {
                Console.WriteLine(ex.Message);
                handled = true;
            }
            else if (agg != null)
            {
                agg.Flatten().Handle(Walker_TexasRanger);
                handled = true;
            }

            return handled;
        }
    }
}
