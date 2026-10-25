// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.IO;
using System.Text;

namespace g3cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // stdout carries only the JSON result (or the MCP messages); anything else the library
            // prints goes to stderr
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
            Console.SetOut(Console.Error);
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            int code = Cli.Run(args, stdout, stdin);
            stdout.Flush();
            return code;
        }
    }
}
