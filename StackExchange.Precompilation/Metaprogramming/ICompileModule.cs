namespace StackExchange.Precompilation
{
    /// <summary>
    /// Allows plugging into the compilation pipeline. Has to be registered in app/web.config
    /// </summary>
    /// <remarks>
    ///   <example>
    ///     <code language="xml"><![CDATA[
    /// <configuration>
    ///   <configSections>
    ///     <section name="stackExchange.precompiler" type="StackExchange.Precompilation.PrecompilerSection, StackExchange.Precompilation" />
    ///   </configSections>
    ///   <stackExchange.precompiler>
    ///     <modules>
    ///       <add type="MyAssembly.MyModule, MyAssembly" />
    ///     </modules>
    ///   </stackExchange.precompiler>
    /// </coniguration>]]>
    ///     </code>
    ///   </example>
    /// </remarks>
    public interface ICompileModule
    {
        /// <summary>
        /// Called before anything is emitted
        /// </summary>
        /// <param name="context"></param>
        void BeforeCompile(BeforeCompileContext context);

        /// <summary>
        /// Called after the compilation is emitted. Changing the compilation will not have any effect at this point
        /// but the assembly can be changed before it is saved on disk or loaded into memory.
        /// </summary>
        /// <param name="context"></param>
        void AfterCompile(AfterCompileContext context);
    }
}