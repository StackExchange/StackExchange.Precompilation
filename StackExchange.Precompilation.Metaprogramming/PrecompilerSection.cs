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
        public CompileModulesCollection CompileModules { get { return (CompileModulesCollection)base["modules"]; } }

        /// <summary>
        /// Gets the <c>stackExchange.precompiler</c> section from the <see cref="ConfigurationManager"/>.
        /// </summary>
        public static PrecompilerSection Current
        {
            get
            {
                return (PrecompilerSection)ConfigurationManager.GetSection("stackExchange.precompiler");
            }
        }
    }
}