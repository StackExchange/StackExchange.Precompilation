using System.Web.Mvc;
using System.Web.WebPages.Razor;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// Register this class as your asp.net view factory to use MoonSpeak.
    /// The registration will appear like so:
    /// <host factoryType="StackExchange.MoonSpeak.PrecompiledMvcWebRazorHostFactory, StackExchange.MoonSpeak.MVC5"/>
    /// and is typically found in Views\Web.config
    /// </summary>
    public class PrecompiledMvcWebRazorHostFactory : MvcWebRazorHostFactory
    {
        /// <summary>
        /// <see cref="System.Web.Mvc.MvcWebRazorHostFactory"/>
        /// </summary>
        /// <returns>Returns a <see cref="PrecompiledWebPageHost"/> instance.</returns>
        public override WebPageRazorHost CreateHost(string virtualPath, string physicalPath)
        {
            return new PrecompiledWebPageHost(virtualPath, physicalPath);
        }
    }
}
