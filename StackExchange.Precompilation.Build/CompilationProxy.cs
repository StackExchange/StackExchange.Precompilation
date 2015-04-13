using System;
using System.IO;
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

        public static void RunCs(CSharpCommandLineArguments cscArgs, string[] args)
        {
            var setup = new AppDomainSetup
            {
                ApplicationName = AppDomain.CurrentDomain.SetupInformation.ApplicationName,
                ApplicationBase = AppDomain.CurrentDomain.SetupInformation.ApplicationBase,
                ConfigurationFile = Path.Combine(Directory.GetCurrentDirectory(), "web.config"),
            };
            CompileInDomain(setup, proxy => proxy.RunCs(cscArgs.BaseDirectory, args), PrecompilationExtensions.CsCompilationAppDomainName);
        }

        private void RunCs(string baseDirectory, string[] args)
        {
            var cscArgs = CSharpCommandLineParser.Default.Parse(args, baseDirectory);
            new Compilation(cscArgs, new DirectoryInfo(Directory.GetCurrentDirectory())).Run();
        }

        private static void CompileInDomain(AppDomainSetup setup, Action<CompilationProxy> action, string appDomainName = null)
        {
            AppDomain compilationDomain = null;
            try
            {
                compilationDomain = AppDomain.CreateDomain(
                    appDomainName ?? "Compilation Domain",
                    AppDomain.CurrentDomain.Evidence,
                    setup
                    );
                action(InitProxy(compilationDomain));
            }
            finally
            {
                // runtime has exited, finish off by unloading the runtime appdomain
                if (compilationDomain != null) AppDomain.Unload(compilationDomain);
            }
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