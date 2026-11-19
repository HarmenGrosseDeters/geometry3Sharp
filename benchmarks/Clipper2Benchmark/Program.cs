using System;

namespace Clipper2Benchmark
{
    /// <summary>
    /// list: the operations. prof [filter] [seconds]: time, allocation and GC per call. sites [filter]: allocated bytes by type.
    /// alloc: the cost of allocating on this machine. ab [filter] [seconds]: Regions with one shared engine against one engine per
    /// polygon, interleaved rounds, results compared bit for bit. See README.md.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "list") {
                var l = Ops.All();
                for (int i = 0; i < l.Count; i++) Console.WriteLine($"{i}\t{l[i].Name}");
                return 0;
            }
            return Profile.Run(args);
        }
    }
}
