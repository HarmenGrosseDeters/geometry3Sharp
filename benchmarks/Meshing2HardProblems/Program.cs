using System;

namespace Meshing2HardProblems
{
    /// <summary>
    /// The prototypes and measurements of the research spike on two hard problems of the 2D mesher
    /// (docs/research/MESHING2D_HP6_HP7_SPIKE_2026-11.md). See README.md.
    /// </summary>
    internal static class Program
    {
        const string Usage = @"Meshing2HardProblems <command>
  hp6 [--phi 40,20,10,5,1,0.1] [--bounds 20,25] [--filter fan] [--meshers g3-default,g3-cq,ruppert,mpw,collar] [--vertex-limit 60000]
        small input angles: the library's mesher and the reference rules on procedural inputs, the triangles below the bound by class
  hp7 [--size small|large] [--filter parcels]
        the oracle's time by check, against the mesher and against the prototype checker of a certifying design
  selfcheck
        the prototype checker rejects corrupted meshes and certificates
";
        static string Arg(string[] args, string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        static int Main(string[] args)
        {
            if (args.Length == 0) { Console.Error.WriteLine(Usage); return 2; }
            switch (args[0]) {
                case "hp6": return Hp6.Run(args, Arg);
                case "hp7": return Hp7.Run(args, Arg);
                case "selfcheck": return Hp7.SelfCheck();
                default: Console.Error.WriteLine(Usage); return 2;
            }
        }
    }
}
