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
        public static bool RunCs(string[] args)
        {
            var precompilationArgs = PrecompilationCommandLineParser.Parse(args, Directory.GetCurrentDirectory());

            CompilationProxy proxy = null;
            AppDomain compilationDomain = null;
            try
            {
                var currentSetup = AppDomain.CurrentDomain.SetupInformation;
                var setup = new AppDomainSetup()
                {
                    ApplicationName = currentSetup.ApplicationName,
                    ApplicationBase = currentSetup.ApplicationBase,
                    ConfigurationFile = precompilationArgs.AppConfig,
                    DisallowBindingRedirects = true,
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
                compilationDomain.UnhandledException += (s, e) =>
                {
                    Console.WriteLine("error: " + e);
                };

                var references = precompilationArgs.References; //.Concat(Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory).Where(r => assemblyExt.Contains(Path.GetExtension(r)))).ToArray()
                CompilationAssemblyResolver.Register(compilationDomain, references);


                proxy = (CompilationProxy)compilationDomain.CreateInstanceAndUnwrap(
                    typeof(CompilationProxy).Assembly.FullName,
                    typeof(CompilationProxy).FullName);

                Console.CancelKeyPress += (s, e) => proxy?.ForceStop();
                return proxy.RunCs(precompilationArgs);
            }
            finally
            {
                proxy?.ForceStop();
                // runtime has exited, finish off by unloading the runtime appdomain
                if (compilationDomain != null) AppDomain.Unload(compilationDomain);
            }
        }

        public override object InitializeLifetimeService() => null;

        private CancellationTokenSource _cts;

        // ReSharper disable MemberCanBeMadeStatic.Local
        // making the methods below static would not be a good idea, they need to run in the _compilation app domain
        private bool RunCs(PrecompilationCommandLineArgs precompilationArgs)
        {
            _cts = new CancellationTokenSource();
            return new Compilation(precompilationArgs).RunAsync(_cts.Token).Result;
        }

        private void ForceStop()
        {
            var cts = _cts;
            _cts = null;
            cts?.Cancel();
            cts?.Dispose();
        }
    }
}