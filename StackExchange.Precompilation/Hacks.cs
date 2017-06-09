using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Reflection;
using System.Web.Mvc;

namespace StackExchange.Precompilation
{
    static class Hacks
    {
        private static readonly Action<WebViewPage, string> WebViewPage_OverridenLayoutPathSetter = 
            (Action<WebViewPage, string>)typeof(WebViewPage)
            .GetProperty("OverridenLayoutPath", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetMethod
            .CreateDelegate(typeof(Action<WebViewPage, string>));

        /// <summary>
        /// Sets the WebViewPage.OverridenLayoutPath internal property, the only way to handle
        /// <see cref="ViewResult.MasterName" /> values.
        /// </summary>
        /// <remarks>
        /// Using reflection to get a mis-spelled internal property setter and calling it via reflection.
        /// What could possibly go wrong?
        /// </remarks>
        public static void SetOverriddenLayoutPath(WebViewPage webViewPage, string overridenLayoutPath) =>
            WebViewPage_OverridenLayoutPathSetter.Invoke(webViewPage, overridenLayoutPath);

        /// <summary>
        /// Bear with me, so, in case a the view engine is being executed in a page targeting net45+ (including net46*)
        /// on a system that has net47+ installed, mscorlib contained referenced by BuildManager already
        /// contains ValueTuple, but due to this package referencing CodeAnalysis.Common, which also pulls in
        /// the System.ValueTuple package, that gets copied to /bin, and is therefore also picked up as a
        /// reference in build manager.
        /// This causes compilation.GetTypeByMetadataName("System.ValueTuple`2") to return null due to an
        /// ambigous match, resulting in the lovely CS8137 and CS8179 warnings, at runtime.
        /// The contents of the /bin directory get included due to the default <![CDATA[<add name="*" />]]> entry.
        /// <![CDATA[<remove name="*" />]]> doesn't work since it fails to load the assembly generated for global.asax.cs
        /// <![CDATA[<remove name="System.ValueTuple" />]]> doesn't work, since the wildcard takes preference
        /// https://referencesource.microsoft.com/#System.Web/Configuration/CompilationSection.cs,119d7e4aae57b4b6
        /// <para />
        /// So when this is the case, we need to remove the reference to the System.ValueTuple.dll
        /// </summary>
        /// <param name="compilation"></param>
        /// <returns></returns>
        public static CSharpCompilation MakeValueTuplesWorkWhenRunningOn47RuntimeAndTargetingNet45Plus(CSharpCompilation compilation)
        {
            var mscorlibAssembly = typeof(object).Assembly;
            var valueTupleAssembly = typeof(ValueTuple).Assembly;
            if (mscorlibAssembly != valueTupleAssembly &&
                compilation.GetAssemblyOrModuleSymbol(RoslynRazorViewEngine.ResolveReference(mscorlibAssembly)) is IAssemblySymbol mscorlib)
            {
                compilation = compilation.RemoveReferences(RoslynRazorViewEngine.ResolveReference(valueTupleAssembly));
            }

            return compilation;
        }
    }
}
