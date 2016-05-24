extern alias aliastest;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Test.Module;

namespace Test.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(nameof(aliastest::System.Xml.Linq.Extensions));
            Console.ReadLine().Dump();
        }
    }
}
