using System.Web.Mvc;
using System.Web.WebPages.Razor;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// Register this class as your asp.net view factory to use precompiled views.
    /// </summary>
    /// <remarks>
    /// <example>
    /// The registration will appear like so:
    /// <code language="xml"> <![CDATA[<host factoryType="StackExchange.Precompilation.PrecompiledMvcWebRazorHostFactory, StackExchange.Precompilation"/>]]></code>
    /// and is typically found in Views\Web.config 
    /// </example>
    /// </remarks>
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
