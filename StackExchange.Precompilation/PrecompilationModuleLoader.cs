using System;
using System.Collections.Generic;
using System.Linq;

namespace StackExchange.Precompilation
{
    internal class PrecompilationModuleLoader
    {
        /// <summary>Fires when a <see cref="CompileModuleElement" /> cannot be resolved to an actual <see cref="System.Type" />.</summary>
        /// <remarks>Register the handlers before touching <see cref="LoadedModules" />.</remarks>
        public event Action<CompileModuleElement, Exception> ModuleInitializationFailed;

        /// <summary>Gets a cached collection of loaded modules.</summary>
        public ICollection<ICompileModule> LoadedModules => _loadedModules.Value;

        private readonly Lazy<ICollection<ICompileModule>> _loadedModules;
        private readonly PrecompilerSection _configuration;

        public PrecompilationModuleLoader(PrecompilerSection configuration)
        {
            _configuration = configuration;
            _loadedModules = new Lazy<ICollection<ICompileModule>>(() =>
            {
                var result = new List<ICompileModule>();
                if (_configuration == null || _configuration.CompileModules == null)
                {
                    return result;
                }

                foreach(var module in _configuration.CompileModules.Cast<CompileModuleElement>())
                {
                    try
                    {
                        var type = Type.GetType(module.Type, true);
                        if (Activator.CreateInstance(type, true) is ICompileModule cm)
                        {
                            result.Add(cm);
                        }
                        else
                        {
                            throw new TypeLoadException($"{module.Type} is not an {nameof(ICompileModule)}.");
                        }
                    }
                    catch(Exception ex)
                    {
                        ModuleInitializationFailed?.Invoke(module, ex);
                    }
                }
                return result;
            });
        }
    }
}