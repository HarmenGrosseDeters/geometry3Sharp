using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Clipper2Benchmark
{
    public class OpsBench
    {
        static readonly List<(string Name, Func<double> Run)> all = Ops.All();
        [Params(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28)]
        public int Op;
        [GlobalSetup] public void Setup() { if (Op >= all.Count) throw new InvalidOperationException("skip"); }
        [Benchmark] public double Run() => all[Op].Run();
    }

    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "list") {
                var l = Ops.All();
                for (int i = 0; i < l.Count; i++) Console.WriteLine($"{i}\t{l[i].Name}");
                return 0;
            }
            if (args.Length > 0 && args[0] == "bdn") {
                BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args.Skip(1).ToArray());
                return 0;
            }
            return Profile.Run(args);
        }
    }
}
