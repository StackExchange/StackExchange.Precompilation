using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace StackExchange.Precompilation
{
    // we need the values of the /reference and the /appconfig switches before we spin up the app domain
    // to get those we need to either spin up a new AppDomain and load Microsoft.CodeAnalysis.CSharp to use CSharpCommandLineParser or parse the args ourselves
    // https://msdn.microsoft.com/en-us/library/78f4aasd.aspx
    public class PrecompilationCommandLineParser
    {
        private static readonly Regex UnespacedBackslashes = new Regex(@"(?!\\+"")\\+", RegexOptions.Compiled);
        private static readonly Regex QuotesAfterSingleBackSlash = new Regex(@"(?<=(^|[^\\])((\\\\)+)?)""", RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        private static readonly Regex Escaped = new Regex(@"\\(\\|"")", RegexOptions.Compiled);

        public static string[] SplitCommandLine(string commandLine)
        {
            return Split(commandLine)
                .TakeWhile(arg => !arg.StartsWith("#", StringComparison.Ordinal))
                .Select(dirty => UnespacedBackslashes.Replace(dirty, "$0$0"))
                .Select(normalized => QuotesAfterSingleBackSlash.Replace(normalized, ""))
                .Select(unquoted => Escaped.Replace(unquoted, "$1"))
                .Where(arg => !string.IsNullOrEmpty(arg))
                .Select(str => str.Trim())
                .ToArray();
        }

        private static IEnumerable<string> Split(string commandLine)
        {
            var isQuoted = false;
            var backslashCount = 0;
            var splitIndex = 0;
            var length = commandLine.Length;

            for (var i = 0; i < length; i++)
            {
                var c = commandLine[i];
                switch (c)
                {
                    case '\\':
                        backslashCount += 1;
                        break;
                    case '\"':
                        if (backslashCount % 2 == 0) isQuoted = !isQuoted;
                        goto default;
                    case ' ':
                    case '\t':
                    case '\n':
                    case '\r':
                        if (!isQuoted)
                        {
                            var take = i - splitIndex;
                            if (take > 0)
                            {
                                yield return commandLine.Substring(splitIndex, take);
                            }
                            splitIndex = i + 1;
                        }
                        goto default;
                    default:
                        backslashCount = 0;
                        break;
                }
            }

            if (splitIndex < length)
            {
                yield return commandLine.Substring(splitIndex);
            }
        }

        private static readonly Regex Reference = new Regex(@"/r(eference)?:(?<alias>[\w_]+=)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static PrecompilationCommandLineArgs Parse(string[] arguments, string baseDirectory)
        {
            var result = new PrecompilationCommandLineArgs { Arguments = arguments, BaseDirectory = baseDirectory };
            if (arguments == null) return result;

            var loadedRsp = new HashSet<string>();
            var references = new HashSet<string>();
            for(var i = 0; i < arguments.Length; i++)
            {
                var arg = arguments[i];
                var reference = Reference.Match(arg);
                if(arg.StartsWith("@"))
                {
                    if (!loadedRsp.Add(arg = ParseFileFromArg(arg, '@'))) continue;
                    arguments = arguments.Concat(File.ReadAllLines(arg).SelectMany(SplitCommandLine)).ToArray();
                }
                else if(reference.Success)
                {
                    // don't care about reference aliases in the compilation appdomain
                    // https://msdn.microsoft.com/en-us/library/ms173212.aspx
                    references.Add(ParseFileFromArg(arg, reference.Groups["alias"].Success ? '=' : ':'));
                }
                else if(arg.StartsWith("/appconfig:"))
                {
                    result.AppConfig = ParseFileFromArg(arg);
                }
            }
            result.References = references.ToArray();
            return result;
        }

        private static string ParseFileFromArg(string arg, char delimiter = ':')
        {
            return Path.GetFullPath(arg.Substring(arg.IndexOf(delimiter) + 1));
        }
    }
}