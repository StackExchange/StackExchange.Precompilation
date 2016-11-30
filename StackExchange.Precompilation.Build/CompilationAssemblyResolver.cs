using System;
using System.Collections.Generic;
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
        private Dictionary<string, Lazy<Assembly>> fullLookup;
        private AppDomain domain;
        private void Setup(string[] references)
        {
            var referenceAssemblies = references.Select(x =>
            {
                try
                {
                    return Assembly.ReflectionOnlyLoadFrom(x);
                }
                catch (Exception ex)
                {
                    return null;
                }
            }).Where(x => x != null).ToArray();

            fullLookup = new Tuple<string, string>[] { }
                .Concat(referenceAssemblies.Select(x => Tuple.Create(x.FullName, x.Location)))
                .Concat(referenceAssemblies.ToLookup(x => x.GetName().Name).Select(x => Tuple.Create(x.Key, x.First().Location)))
                .ToDictionary(x => x.Item1, x => new Lazy<Assembly>(() => Assembly.LoadFile(x.Item2), LazyThreadSafetyMode.ExecutionAndPublication));
        }

        private void RegisterDomain(AppDomain domain)
        {
            this.domain = domain;
            this.domain.AssemblyResolve += ResolveAssembly;
        }

        private Assembly ResolveAssembly(object sender, ResolveEventArgs e)
        {
            var name = domain.ApplyPolicy(e.Name);
            Lazy<Assembly> a;
            if ((fullLookup.TryGetValue(name, out a)))
            {
                return a.Value;
            }

            return null;
        }
    }
}
