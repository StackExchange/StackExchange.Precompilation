using System;

namespace StackExchange.Precompilation
{
    public static class PrecompilationExtensions
    {
        public const string CsCompilationAppDomainName = "csMoonSpeak";

        public static bool IsPrecompilation(this AppDomain appDomain)
        {
            return appDomain.FriendlyName == CsCompilationAppDomainName;
        }
    }
}