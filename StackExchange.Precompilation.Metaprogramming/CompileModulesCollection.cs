using System.Configuration;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// A collection of <see cref="CompileModuleElement"/> instances.
    /// </summary>
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
}