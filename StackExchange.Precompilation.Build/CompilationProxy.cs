using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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
                var setup = new AppDomainSetup
                {
                    ApplicationName = currentSetup.ApplicationName,
                    ApplicationBase = currentSetup.ApplicationBase,
                    ConfigurationFile = precompilationArgs.AppConfig,
                };

                if (setup.ConfigurationFile == null)
                {
                    setup.ConfigurationFile = new[] { "app.config", "web.config" }.FirstOrDefault(File.Exists);
                    if (!string.IsNullOrWhiteSpace(setup.ConfigurationFile))
                    {
                        Console.WriteLine("WARNING: '" + setup.ConfigurationFile + "' used as fallback config file");
                    }
                }

                compilationDomain = AppDomain.CreateDomain(
                    AppDomainHelper.CsCompilationAppDomainName,
                    AppDomain.CurrentDomain.Evidence,
                    setup);

                var proxy = (CompilationProxy) compilationDomain.CreateInstanceAndUnwrap(
                    typeof (CompilationProxy).Assembly.FullName,
                    typeof (CompilationProxy).FullName);

                proxy.SetConsoleIo(Console.In, Console.Out, Console.Error);

                // we need to make sure referenced assemblies get loaded before we touch any roslyn or asp.net mvc types
                proxy.HookAssemblyReslove(precompilationArgs.References);

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
            return new Compilation(precompilationArgs).Run();
        }

        private void HookAssemblyReslove(IEnumerable<string> references)
        {
            var referenceAssemblies = references.Select(x =>
            {
                try
                {
                    return Assembly.ReflectionOnlyLoadFrom(x);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WARNING: could not load reference '{0}' - {1} - {2}", x, ex.Message, ex.StackTrace);
                    return null;
                }
            }).Where(x => x != null).ToArray();
            var fullLookup = referenceAssemblies.ToDictionary(x => x.FullName);
            var shortLookup = referenceAssemblies.ToDictionary(x => x.GetName().Name);
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                Assembly a;
                if (fullLookup.TryGetValue(e.Name, out a) || shortLookup.TryGetValue(e.Name, out a))
                {
                    return Assembly.LoadFile(a.Location);
                }
                return null;
            };
        }
    }
}