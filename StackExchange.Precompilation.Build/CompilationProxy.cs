using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

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

        public static bool RunCs(string[] args)
        {
            var precompilationArgs = PrecompilationCommandLineParser.Parse(args, Directory.GetCurrentDirectory());


            AppDomain compilationDomain = null;
            try
            {
                var currentSetup = AppDomain.CurrentDomain.SetupInformation;
                var setup = new AppDomainSetup()
                {
                    ApplicationName = currentSetup.ApplicationName,
                    ApplicationBase = currentSetup.ApplicationBase,
                    ConfigurationFile = precompilationArgs.AppConfig,
                };

                if (setup.ConfigurationFile == null)
                {
                    setup.ConfigurationFile = new[] { "app.config", "web.config" }.Select(x => Path.Combine(precompilationArgs.BaseDirectory, x)).FirstOrDefault(File.Exists);
                    if (!string.IsNullOrWhiteSpace(setup.ConfigurationFile))
                    {
                        Console.WriteLine("WARNING: '" + setup.ConfigurationFile + "' used as fallback config file");
                    }
                }

                compilationDomain = AppDomain.CreateDomain(
                    AppDomainHelper.CsCompilationAppDomainName,
                    AppDomain.CurrentDomain.Evidence,
                    setup);

                var assemblyExt = new HashSet<string> { ".dll", ".exe" };
                var references = precompilationArgs.References.Concat(Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory).Where(r => assemblyExt.Contains(Path.GetExtension(r)))).ToArray();
                CompilationAssemblyResolver.Register(compilationDomain, references);


                var proxy = (CompilationProxy)compilationDomain.CreateInstanceAndUnwrap(
                    typeof(CompilationProxy).Assembly.FullName,
                    typeof(CompilationProxy).FullName);

                proxy.SetConsoleIo(Console.In, Console.Out, Console.Error);

                return proxy.RunCs(precompilationArgs);
            }
            finally
            {
                // runtime has exited, finish off by unloading the runtime appdomain
                if (compilationDomain != null) AppDomain.Unload(compilationDomain);
            }
        }

        public override object InitializeLifetimeService()
        {
            return null;
        }

        // ReSharper disable MemberCanBeMadeStatic.Local
        // making the methods below static would not be a good idea, they need to run in the compilation app domain
        private bool RunCs(PrecompilationCommandLineArgs precompilationArgs)
        {
            return new Compilation(precompilationArgs).RunAsync().Result;
        }
    }
}