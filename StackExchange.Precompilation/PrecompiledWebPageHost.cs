using System.CodeDom;
using System.Web.Mvc.Razor;
using System.Web.Razor.Generator;

namespace StackExchange.Precompilation

{
    /// <summary>
    /// Decorates generated web page classes with a <see cref="CompiledFromFileAttribute"/>, so they can be picked up by the <see cref="PrecompiledViewEngine"/>.
    /// </summary>
    public class PrecompiledWebPageHost : MvcWebPageRazorHost
    {
        public PrecompiledWebPageHost(string virtualPath, string physicalPath)
            : base(virtualPath, physicalPath)
        {
        }

        /// <summary>
        /// Adds a <see cref="CompiledFromFileAttribute"/> to the <see cref="CodeGeneratorContext.GeneratedClass"/>.
        /// </summary>
        /// <param name="context"></param>
        public override void PostProcessGeneratedCode(CodeGeneratorContext context)
        {
            context.GeneratedClass.CustomAttributes.Add(
                new CodeAttributeDeclaration(
                    new CodeTypeReference(typeof(CompiledFromFileAttribute)),
                    new CodeAttributeArgument(new CodePrimitiveExpression(context.SourceFile))
                ));
            base.PostProcessGeneratedCode(context);
        }
    }
}