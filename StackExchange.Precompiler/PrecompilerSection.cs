using System.Configuration;

namespace StackExchange.Precompiler
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

    public class CompileModulesCollection : ConfigurationElementCollection
    {
        protected override ConfigurationElement CreateNewElement()
        {
            return new CompileModuleElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((CompileModuleElement)element).Type;
        }
    }

    public class CompileModuleElement : ConfigurationElement
    {
        [ConfigurationProperty("type", IsRequired = true, DefaultValue = null)]
        public string Type { get { return (string)base["type"]; } }
    }
}