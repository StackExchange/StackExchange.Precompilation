using System;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// Decorates a precompiled MVC assembly. Used to calculate relative view paths.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public class CompiledFromDirectoryAttribute : Attribute
    {
        /// <summary>
        /// Gets the source directory path.
        /// </summary>
        public string SourceDirectory { get; private set; }

        /// <summary></summary>
        /// <param name="sourceDirectory"></param>
        public CompiledFromDirectoryAttribute(string sourceDirectory)
        {
            SourceDirectory = sourceDirectory;
        }
    }
}