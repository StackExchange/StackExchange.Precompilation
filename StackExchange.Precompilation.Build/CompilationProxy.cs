using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace StackExchange.Precompilation
{
    class CompilationProxy : MarshalByRefObject
    {
        private void SetConsoleIo(TextReader @in, TextWriter @out, TextWriter error)
        {
            Console.SetIn(@in);
            Console.SetOut(@out);
            Console.SetError(error);
        }

        public static bool RunCs(CSharpCommandLineArguments cscArgs, string[] args)
        {
            var setup = new AppDomainSetup
            {
                ApplicationName = AppDomain.CurrentDomain.SetupInformation.ApplicationName,
                ApplicationBase = AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
                ConfigurationFile = DetectConfigFile(cscArgs),
            };
            return CompileInDomain(setup, proxy => proxy.RunCs(cscArgs.BaseDirectory, args), PrecompilationExtensions.CsCompilationAppDomainName);
        }

        private static string DetectConfigFile(CommandLineArguments arguments)
        {
            var candidates = new []
            {
                arguments.AppConfigPath,
                Path.Combine(arguments.BaseDirectory, "web.config"),
                Path.Combine(arguments.BaseDirectory, "app.config"),
            };
            return candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x));
        }

        private bool RunCs(string baseDirectory, string[] args)
        {
            var cscArgs = CSharpCommandLineParser.Default.Parse(args, baseDirectory);
            return new Compilation(cscArgs, new DirectoryInfo(Directory.GetCurrentDirectory())).Run();
        }

        private static bool CompileInDomain(AppDomainSetup setup, Func<CompilationProxy, bool> compile, string appDomainName)
        {
            AppDomain compilationDomain = null;
            bool success = false;
            try
            {
                compilationDomain = AppDomain.CreateDomain(
                    appDomainName,
                    AppDomain.CurrentDomain.Evidence,
                    setup
                    );
                var proxy = InitProxy(compilationDomain);
                success = compile(proxy);
            }
            finally
            {
                // runtime has exited, finish off by unloading the runtime appdomain
                if (compilationDomain != null) AppDomain.Unload(compilationDomain);
            }
            return success;
        }

        private static CompilationProxy InitProxy(AppDomain compilationDomain)
        {
            var proxy = (CompilationProxy) compilationDomain.CreateInstanceAndUnwrap(
                typeof (CompilationProxy).Assembly.FullName,
                typeof (CompilationProxy).FullName);
            proxy.SetConsoleIo(Console.In, Console.Out, Console.Error);
            return proxy;
        }


        public override object InitializeLifetimeService()
        {
            return null;
        }
    }
}