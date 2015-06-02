using System;
using System.Web.Mvc;

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

        /// <summary>
        /// Triggers the <see cref="ProfileStep"/>
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        protected IDisposable DoProfileStep(string name)
        {
            return ProfileStep?.Invoke(name);
        }
    }
}
