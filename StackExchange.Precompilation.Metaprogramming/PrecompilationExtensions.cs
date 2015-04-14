using System;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace StackExchange.Precompilation
{
    public static class PrecompilationExtensions
    {
        public const string CsCompilationAppDomainName = "csMoonSpeak";

        public static bool IsPrecompilation(this AppDomain appDomain)
        {
            return appDomain.FriendlyName == CsCompilationAppDomainName;
        }

        public static Location ToLocation(this string path)
        {
            return Location.Create(path, new TextSpan(), new LinePositionSpan());
        }
    }
}