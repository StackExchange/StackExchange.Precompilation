extern alias aliastest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Test.Module;

namespace Test.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(PathMapTest());
            Console.WriteLine(nameof(aliastest::System.Xml.Linq.Extensions));
            Console.ReadLine().Dump();
        }

        // path mapping test, configured via <PathMap> property in the .csproj
        static string PathMapTest([CallerFilePath] string path = null) =>
            path.StartsWith("https://example.org/") ? path : throw new InvalidOperationException();
    }
}
