using System;

namespace StackExchange.Precompilation
{
    [Serializable]
    public class PrecompilationCommandLineArgs : MarshalByRefObject
    {
        /// <summary>
        /// Unprocessed arguments.
        /// </summary>
        public string[] Arguments { get; set; }

        /// <summary>
        /// The current directory in which the compilation was started.
        /// </summary>
        public string BaseDirectory { get; set; }

        /// <summary>
        /// The value of the /appconfig switch if present.
        /// </summary>
        public string AppConfig { get; set; }

        /// <summary>
        /// The values of the /reference switches.
        /// </summary>
        public string[] References { get; set; }
    }
}