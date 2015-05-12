using System;

namespace StackExchange.Precompilation
{
    /// <summary>
    /// Precompilation helper methods.
    /// </summary>
    public static class AppDomainHelper
    {
        /// <summary>
        /// The friednly name of the <see cref="AppDomain"/> hosting the compilation.
        /// </summary>
        public const string CsCompilationAppDomainName = "csMoonSpeak";

        /// <summary>
        /// </summary>
        /// <param name="appDomain"></param>
        /// <returns>Returns <c>true</c> if the <paramref name="appDomain"/> is a Precompilation domain.</returns>
        public static bool IsPrecompilation(this AppDomain appDomain)
        {
            return (appDomain ?? AppDomain.CurrentDomain).FriendlyName == CsCompilationAppDomainName;
        }
    }
}