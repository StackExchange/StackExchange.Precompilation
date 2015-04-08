using System;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// Decorates a precompiled MVC page.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class CompiledFromFileAttribute : Attribute
    {
        /// <summary>
        /// Gets the source file path.
        /// </summary>
        public string SourceFile { get; private set; }

        /// <summary></summary>
        /// <param name="sourceFile"></param>
        public CompiledFromFileAttribute(string sourceFile)
        {
            SourceFile = sourceFile;
        }
    }
}
