using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace StackExchange.Precompilation.Tests
{
    [TestFixture]
    public class CommandLineTests
    {
        // test cases from https://github.com/dotnet/roslyn/blob/9c66e81c1424d8f4999f70eb8b85f0e76f253c30/src/Compilers/Core/CodeAnalysisTest/CommonCommandLineParserTests.cs#L83
        [Test]
        [TestCase("", new string[0])]
        [TestCase("   \t   ", new string[0])]
        [TestCase("   abc\tdef baz    quuz   ", new[] { "abc", "def", "baz", "quuz" })]
        [TestCase(@"  ""abc def""  fi""ddle dee de""e  ""hi there ""dude  he""llo there""  ", new [] { @"abc def", @"fiddle dee dee", @"hi there dude", @"hello there" })]
        [TestCase(@"  ""abc def \"" baz quuz"" ""\""straw berry"" fi\""zz \""buzz fizzbuzz", new [] { @"abc def "" baz quuz", @"""straw berry", @"fi""zz", @"""buzz", @"fizzbuzz" })]
        [TestCase(@"  \\""abc def""  \\\""abc def"" ", new [] { @"\abc def", @"\""abc", @"def" })]
        [TestCase(@"  \\\\""abc def""  \\\\\""abc def"" ", new [] { @"\\abc def", @"\\""abc", @"def" })]
        [TestCase(@"  \\\\""abc def""  \\\\\""abc def"" q a r ", new [] { @"\\abc def", @"\\""abc", @"def q a r" })]
        [TestCase(@"abc #Comment ignored", new [] { @"abc" })]
        public static void SplitArguments(string input, string[] expected)
        {
            var actual = PrecompilationCommandLineParser.SplitCommandLine(input);

            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void ParseArguments()
        {
            Assert.DoesNotThrow(() => PrecompilationCommandLineParser.Parse(null, null));
            Assert.DoesNotThrow(() => PrecompilationCommandLineParser.Parse(new string[0], null));
            Assert.DoesNotThrow(() => PrecompilationCommandLineParser.Parse(null, ""));
            Assert.DoesNotThrow(() => PrecompilationCommandLineParser.Parse(new string[0], ""));
        }

        [Test]
        [TestCase("a b c", new string[0], (string)null)]
        [TestCase("a b /r:c.dll", new[] { "c.dll" }, (string)null)]
        [TestCase("a b /r:c.dll /r:d.dll", new[] { "c.dll", "d.dll" }, (string)null)]
        [TestCase("a b /r:c.dll /appconfig:moar.config /r:d.dll", new[] { "c.dll", "d.dll" }, "moar.config")]
        public void ParseArgumentCases(string cmdline, string[] references, string appconfig)
        {
            var dir = Guid.NewGuid().ToString();
            var args = PrecompilationCommandLineParser.SplitCommandLine(cmdline);
            var parsed = PrecompilationCommandLineParser.Parse(args, dir);
            Func<string, string> resolvePath = Path.GetFullPath;

            Assert.AreEqual(dir, parsed.BaseDirectory);
            Assert.AreEqual(appconfig == null ? null : resolvePath(appconfig), parsed.AppConfig);
            CollectionAssert.AreEqual(references.Select(resolvePath), parsed.References);
        }
    }
}
