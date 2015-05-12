using System.Configuration;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// A compile module configuration element.
    /// </summary>
    /// <seealso cref="ICompileModule"/>
    public class CompileModuleElement : ConfigurationElement
    {
        /// <summary>
        /// The type of the <see cref="ICompileModule"/> to be loaded at compile time.
        /// </summary>
        [ConfigurationProperty("type", IsRequired = true, DefaultValue = null)]
        public string Type { get { return (string)base["type"]; } }
    }
}