using System;
using System.Runtime.Serialization;

namespace StackExchange.Precompiler
{
    [Serializable]
    public class CompilationFailedException : Exception
    {
        public CompilationFailedException(){}
        public CompilationFailedException(string message) : base(message){}
        public CompilationFailedException(string message, Exception innerException) : base(message, innerException){}
        protected CompilationFailedException(SerializationInfo info, StreamingContext context) : base(info, context){}
        public override void GetObjectData(SerializationInfo info, StreamingContext context) { base.GetObjectData(info, context); }
    }
}