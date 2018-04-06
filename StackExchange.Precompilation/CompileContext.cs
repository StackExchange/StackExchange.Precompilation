using System;
using System.Collections.Generic;
using System.Linq;

namespace StackExchange.Precompilation
{

    internal class CompileContext
    {
        private readonly ICollection<ICompileModule> _modules;
        public BeforeCompileContext BeforeCompileContext { get; private set; }
        public AfterCompileContext AfterCompileContext { get; private set; }
        public CompileContext(ICollection<ICompileModule> modules)
        {
            _modules = modules;
        }
        public void Before(BeforeCompileContext context)
        {
            Apply(context, x => BeforeCompileContext = x, m => m.BeforeCompile);
        }
        public void After(AfterCompileContext context)
        {
            Apply(context, x => AfterCompileContext = x, m => m.AfterCompile);
        }
        private void Apply<TContext>(TContext ctx, Action<TContext> setter, Func<ICompileModule, Action<TContext>> actionGetter)
            where TContext : ICompileContext
        {
            setter(ctx);
            foreach(var module in _modules)
            {
                try
                {
                    var action = actionGetter(module);
                    action(ctx);
                }
                catch (Exception ex)
                {
                    var methodName = ctx is BeforeCompileContext ? nameof(ICompileModule.BeforeCompile) : nameof(ICompileModule.AfterCompile);
                    throw new PrecompilationModuleException($"Precompilation module '{module.GetType().FullName}.{methodName}({typeof(TContext)})' failed", ex);
                }
            }
        }
    }

    internal class PrecompilationModuleException : Exception
    {
        public PrecompilationModuleException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}