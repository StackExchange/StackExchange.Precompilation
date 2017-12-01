using System;
using System.Web.Mvc;
using System.Web.WebPages;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// Base class for implementing <see cref="VirtualPathProviderViewEngine"/> derived types that provide custom profiling steps.
    /// </summary>
    public abstract class ProfiledVirtualPathProviderViewEngine : VirtualPathProviderViewEngine
    {
        /// <summary>
        /// Triggers when the engine performs a step that can be profiled.
        /// </summary>
        public Func<string, IDisposable> ProfileStep { get; set; }

        internal IVirtualPathFactory VirtualPathFactory => _virtualPathFactoryFactory.Value;

        protected abstract IVirtualPathFactory CreateVirtualPathFactory();

        private readonly Lazy<IVirtualPathFactory> _virtualPathFactoryFactory; // sorry, I had to...
        
        /// <inheritdoc />
        protected ProfiledVirtualPathProviderViewEngine()
        {
            _virtualPathFactoryFactory = new Lazy<IVirtualPathFactory>(CreateVirtualPathFactory);
        }
    }

    internal static class ProfileVirtualPathProviderViewEngineExtensions
    {
        /// <summary>
        /// Invokes the <see cref="ProfiledVirtualPathProviderViewEngine.ProfileStep"/> if it's set.
        /// </summary>
        public static IDisposable DoProfileStep(this ProfiledVirtualPathProviderViewEngine instance, string name) =>
            instance?.ProfileStep?.Invoke(name);
    }
}
