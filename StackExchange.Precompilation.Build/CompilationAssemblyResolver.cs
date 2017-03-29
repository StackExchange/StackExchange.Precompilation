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
        private readonly ConcurrentBag<Tuple<AssemblyName, Func<Assembly>>> assemblyLoaders = new ConcurrentBag<Tuple<AssemblyName, Func<Assembly>>>();
        private readonly ConcurrentDictionary<string, Lazy<Assembly>> resolvedAssemblies = new ConcurrentDictionary<string, Lazy<Assembly>>();

        private void Setup(string[] references)
        {
            void Resolve(AssemblyName name, Func<Assembly> loader)
            {
                assemblyLoaders.Add(Tuple.Create(name, loader));

                var keyName = new AssemblyName(ApplyPolicy(name.FullName));
                resolvedAssemblies.AddOrUpdate(keyName.FullName, ResolvedAssembly(loader), (key, existing) => existing); // TODO log conflicting binds?
                resolvedAssemblies.AddOrUpdate(keyName.Name, ResolvedAssembly(loader), (key, existing) => existing); // TODO log conflicting partial binds?
            }

            // load the embedded compile-time references, we're gonna need them for sure
            const string prefix = "embedded_ref://";
            var thisAssembly = typeof(CompilationAssemblyResolver).Assembly;
            thisAssembly.GetManifestResourceNames()
                .AsParallel()
                .Where(x => x.StartsWith(prefix))
                .ForAll(resourceKey => 
                {
                    using (var resource = thisAssembly.GetManifestResourceStream(resourceKey))
                    using (var ms = new MemoryStream())
                    {
                        if (resource != null)
                        {
                            resource.CopyTo(ms);
                            var rawAssembly = ms.ToArray();
                            var assembly = Assembly.Load(rawAssembly);
                            var name = assembly.GetName();
                            name.CodeBase = resourceKey;
                            Resolve(name, () => assembly);
                        }
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
                .ForAll(name => Resolve(name, () => Assembly.LoadFile(new Uri(name.CodeBase).LocalPath)));
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
            var requestedAssembly = new AssemblyName(ApplyPolicy(e.Name));

            return resolvedAssemblies.GetOrAdd(requestedAssembly.FullName, NullAseembly).Value ??
                   resolvedAssemblies.GetOrAdd(requestedAssembly.Name, NullAseembly).Value;
        }

        private static Lazy<Assembly> NullAseembly(string key) => new Lazy<Assembly>(() => null, LazyThreadSafetyMode.ExecutionAndPublication);

        private static Lazy<Assembly> ResolvedAssembly(Func<Assembly> loader) => new Lazy<Assembly>(loader, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
