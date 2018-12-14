using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace StackExchange.Precompilation
{

    class CompilationAssemblyResolver : MarshalByRefObject
    {
        internal static void Register(AppDomain domain, string[] references)
        {
            CompilationAssemblyResolver resolver =
                domain.CreateInstanceFromAndUnwrap(
                  Assembly.GetExecutingAssembly().Location,
                  typeof(CompilationAssemblyResolver).FullName) as CompilationAssemblyResolver;
            resolver.RegisterDomain(domain);
            resolver.Setup(references);
        }

        private AppDomain domain;
        private readonly ConcurrentDictionary<string, Lazy<Assembly>> resolvedAssemblies = new ConcurrentDictionary<string, Lazy<Assembly>>();

        private void Setup(string[] references)
        {
            void Resolve(AssemblyName name, Func<Assembly> loader)
            {
                var resolved = new Lazy<Assembly>(loader, LazyThreadSafetyMode.ExecutionAndPublication);
                var keyName = new AssemblyName(ApplyPolicy(name.FullName));
                resolvedAssemblies.AddOrUpdate(keyName.FullName, resolved, (key, existing) => existing); // TODO log conflicting binds?
                resolvedAssemblies.AddOrUpdate(keyName.Name, resolved, (key, existing) => existing); // TODO log conflicting partial binds?
            }

            // load runtime references from tools/*.dll

            var location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Directory.EnumerateFiles(location, "*.dll")
                .AsParallel()
                .ForAll(dll =>
                {
                    try
                    {
                        var assemblyName = AssemblyName.GetAssemblyName(dll);
                        Resolve(assemblyName, () => Assembly.LoadFile(dll));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("hidden: failed to resolve assembly {0}: {1}", dll, ex.Message);
                    }
                });

            // load all the other references
            references
                .AsParallel()
                .Select(x =>
                {
                    try
                    {
                        return AssemblyName.GetAssemblyName(x);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"warning: Couldn't load reference from '{x}' - '{ex.Message}'");
                        return null;
                    }
                })
                .Where(x => x != null)
                .ForAll(name => Resolve(name, () =>
                {
                    var path = new Uri(name.CodeBase).LocalPath;
                    try
                    {
                        return Assembly.LoadFile(path);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"warning: Couldn't load reference '{name.FullName}' from '{path}' - '{ex.Message}'");
                        return null;
                    }
                }));
        }

        private void RegisterDomain(AppDomain domain)
        {
            this.domain = domain;
            this.domain.AssemblyResolve += ResolveAssembly;
        }

        private string ApplyPolicy(string name)
        {
            while (true) {
                var newName = domain.ApplyPolicy(name);
                if (newName == name) return name;
                name = newName;
            }
        }

        private Assembly ResolveAssembly(object sender, ResolveEventArgs e)
        {
            var name = ApplyPolicy(e.Name);
            var assemblyName = new AssemblyName(name);

            return resolvedAssemblies.GetOrAdd(assemblyName.FullName, NullAssembly).Value ??
                   resolvedAssemblies.GetOrAdd(assemblyName.Name, NullAssembly).Value;
        }

        private static Lazy<Assembly> NullAssembly(string key) => new Lazy<Assembly>(() => null, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
