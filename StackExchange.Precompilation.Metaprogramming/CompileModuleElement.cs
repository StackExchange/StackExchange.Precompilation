using System.Configuration;

namespace StackExchange.Precompilation
{
    public class CompileModuleElement : ConfigurationElement
    {
        [ConfigurationProperty("type", IsRequired = true, DefaultValue = null)]
        public string Type { get { return (string)base["type"]; } }
    }
}