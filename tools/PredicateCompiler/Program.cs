// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.IO;

namespace G3PredicateCompiler
{
    static class Program
    {
        // PredicateCompiler --table <spec>: print the bound table
        // PredicateCompiler --doc <spec> <doc.md>: replace the generated table block of the design doc
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--table") {
                Console.Write(PredicateCompiler.Table(File.ReadAllText(args[1])));
                return 0;
            }
            if (args.Length == 3 && args[0] == "--doc") {
                string doc = File.ReadAllText(args[2]).Replace("\r\n", "\n");
                int b = doc.IndexOf(PredicateCompiler.TableBegin, StringComparison.Ordinal);
                int e = doc.IndexOf(PredicateCompiler.TableEnd, StringComparison.Ordinal);
                if (b < 0 || e < b) {
                    Console.Error.WriteLine($"{args[2]}: no generated table block");
                    return 1;
                }
                e += PredicateCompiler.TableEnd.Length + 1;
                File.WriteAllText(args[2], doc.Substring(0, b) + PredicateCompiler.Table(File.ReadAllText(args[1])) + doc.Substring(e));
                Console.WriteLine($"updated {args[2]}");
                return 0;
            }
            Console.Error.WriteLine("usage: PredicateCompiler --table <spec> | PredicateCompiler --doc <spec> <doc.md>");
            return 2;
        }
    }
}
