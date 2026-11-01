// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.IO;

namespace G3PredicateCompiler
{
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--table") {
                Console.Write(PredicateCompiler.Table(File.ReadAllText(args[1])));
                return 0;
            }
            if (args.Length != 2) {
                Console.Error.WriteLine("usage: PredicateCompiler <spec> <output.cs> | PredicateCompiler --table <spec>");
                return 2;
            }
            string code = PredicateCompiler.Compile(File.ReadAllText(args[0]));
            File.WriteAllText(args[1], code);
            Console.WriteLine($"wrote {args[1]}");
            return 0;
        }
    }
}
