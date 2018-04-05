using System.Configuration;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// Defines the <c>stackExchange.precompiler</c> <see cref="ConfigurationSection"/>.
    /// </summary>
    /// <seealso cref="ICompileModule"/>
    public class PrecompilerSection : ConfigurationSection
    {
        /// <summary>
        /// Gets the <see cref="CompileModulesCollection"/>.
        /// </summary>
        [ConfigurationProperty("modules", IsRequired = false)]
        [ConfigurationCollection(typeof(CompileModulesCollection))]
        public CompileModulesCollection CompileModules => (CompileModulesCollection)base["modules"];

        /// <summary>
        /// Gets the <c>stackExchange.precompiler</c> section from the <see cref="ConfigurationManager"/>.
        /// </summary>
        public static PrecompilerSection Current => (PrecompilerSection)ConfigurationManager.GetSection("stackExchange.precompiler");

        [ConfigurationProperty("razorCache", IsRequired = false)]
        public RazorCacheElement RazorCache => (RazorCacheElement)base["razorCache"];

    }
}