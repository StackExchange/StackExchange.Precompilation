using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StackExchange.Precompilation
{
    // we need the values of the /reference and the /appconfig switches before we spin up the app domain
    // to get those we need to either spin up a new AppDomain and load Microsoft.CodeAnalysis.CSharp to use CSharpCommandLineParser or parse the args ourselves
    // https://msdn.microsoft.com/en-us/library/78f4aasd.aspx
    public class PrecompilationCommandLineParser
    {
        private static readonly Regex NormalizeBackslashes = new Regex(@"(?!\\+"")\\+", RegexOptions.Compiled);
        private static readonly Regex QuotesAfterSingleBackSlash = new Regex(@"(?<=(^|[^\\])((\\\\)+)?)""", RegexOptions.ExplicitCapture | RegexOptions.Compiled);
        private static readonly Regex Escaped = new Regex(@"\\(\\|"")", RegexOptions.Compiled);

        public static string[] SplitCommandLine(string commandLine)
        {
            return Split(commandLine)
                .TakeWhile(arg => !arg.StartsWith("#", StringComparison.Ordinal))
                .Select(dirty => NormalizeBackslashes.Replace(dirty, "$0$0"))
                .Select(normalized => QuotesAfterSingleBackSlash.Replace(normalized, ""))
                .Select(unquoted => Escaped.Replace(unquoted, "$1"))
                .Where(arg => !string.IsNullOrEmpty(arg))
                .ToArray();
        }

        private static IEnumerable<string> Split(string commandLine)
        {
            var isQuoted = false;
            var backslashCount = 0;
            var offset = 0;

            for (var i = 0; i < commandLine.Length; i++)
            {
                var c = commandLine[i];
                switch (c)
                {
                    case '\\':
                        backslashCount += 1;
                        break;
                    case '"':
                        if (backslashCount % 2 == 0) isQuoted = !isQuoted;
                        goto default;
                    case ' ':
                    case '\t':
                    case '\n':
                    case '\r':
                        if (!isQuoted)
                        {
                            yield return commandLine.Substring(offset, i - offset).Trim();
                            offset = i + 1;
                        }
                        goto default;
                    default:
                        backslashCount = 0;
                        break;
                }
            }

            yield return commandLine.Substring(offset).Trim();
        }

    }
}