using System.Configuration;

namespace StackExchange.Precompilation
{
    public class RazorCacheElement : ConfigurationElement
    {
        /// <summary>
        /// The type of the <see cref="ICompileModule"/> to be loaded at compile time.
        /// </summary>
        [ConfigurationProperty("directory", IsRequired = true, DefaultValue = null)]
        public string Directory => (string)base["directory"];
    }
}