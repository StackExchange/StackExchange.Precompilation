using System;
using System.Web.WebPages;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// <see cref="WebPageExecutingBase.VirtualPathFactory"/> is used for resolving virtual paths once a view has
    /// been resolved. Setting it to an <see cref="IVirtualPathFactory"/> assumes that all underlying virtual paths
    /// are resolvable (layouts partials etc) are resolvable using the given instance. This can lead to some side 
    /// effect when different view engines are combined, and the matching views are intermixed.
    /// </summary>
    /// <remarks>
    /// This is our version of the <see cref="VirtualPathFactoryManager"/>, which is meant for extending the pipeline,
    /// mentioned above, but is not extendable in the sense that it always falls back to <see cref="System.Web.Compilation.BuildManager"/>,
    /// which calls the old csc.exe in the framework dir, not the shiny one from our nuget package,
    /// and can thus cause unexpected behavior at runtime.
    /// </remarks>
    internal class PrecompilationVirtualPathFactory : IVirtualPathFactory
    {
        private readonly PrecompiledViewEngine _precompiled;
        private readonly RoslynRazorViewEngine _runtime;

        public PrecompilationVirtualPathFactory(PrecompiledViewEngine precompiled = null, RoslynRazorViewEngine runtime = null)
        {
            _precompiled = precompiled;
            _runtime = runtime;
        }

        public object CreateInstance(string virtualPath)
        {
            if (_precompiled?.TryLookupCompiledType(virtualPath) is Type precompiledType)
            {
                return Activator.CreateInstance(precompiledType);
            }
            else if (_runtime?.GetTypeFromVirtualPath(virtualPath) is Type runtimeType)
            {
                return Activator.CreateInstance(runtimeType);
            }
            else
            {
                return null;
            }
        }

        public bool Exists(string virtualPath)
        {
            if (_precompiled?.TryLookupCompiledType(virtualPath) != null)
            {
                return true;
            }
            else if (_runtime?.FileExists(virtualPath) == true)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

    }
}
