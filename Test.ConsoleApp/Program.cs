using System;
using System.Runtime.CompilerServices;
using Test.Module;

namespace Test.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(PathMapTest().Dump());
#if NET462
            Console.WriteLine(typeof(AliasTest).FullName);
#endif
        }

        // path mapping test, configured via <PathMap> property in the .csproj
        static string PathMapTest([CallerFilePath] string path = null) =>
            path.StartsWith("X:\\Test\\")
                ? path
                : throw new InvalidOperationException($"CallerFilePath was expected to start with X:\\Test\\ but was {path}.");
    }
}
