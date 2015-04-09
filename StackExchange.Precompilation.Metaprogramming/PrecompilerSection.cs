using System.Configuration;

namespace StackExchange.Precompilation
{
    public class PrecompilerSection : ConfigurationSection
    {
        [ConfigurationProperty("modules", IsRequired = false)]
        [ConfigurationCollection(typeof(CompileModulesCollection))]
        public CompileModulesCollection CompileModules { get { return (CompileModulesCollection)base["modules"]; } }

        public static PrecompilerSection Current
        {
            get
            {
                return (PrecompilerSection)ConfigurationManager.GetSection("stackExchange.precompiler");
            }
        }
    }
}