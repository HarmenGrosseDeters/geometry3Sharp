// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The tool's own test: the fixtures below have a known reachability; `DeterminismReach self-test` analyzes this assembly
// and fails (exit code 1) if a source is missed or invented. Each expectation fails when the edge kind it names is dropped
// from the graph (checked by hand: see the README).
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace DeterminismReach
{
    namespace Fixtures
    {
        public interface IShape { double Area(); }
        public abstract class Base { public abstract double Value(double x); public virtual double Twice(double x) { return 2 * x; } }

        public class Clean { public double Run(double x) { return Math.Sqrt(x * x + 1) + Math.Abs(x); } }
        public class DirectLibm { public double Run(double x) { return Math.Sin(x); } }
        public class ViaPrivate { public double Run(double x) { return helper(x) + 1; } static double helper(double x) { return deeper(x); } static double deeper(double x) { return Math.Atan2(x, 1); } }
        public class ViaLambda { public double Run(double[] xs) { return xs.Select(x => Math.Exp(x)).First(); } }
        public class ViaMethodGroup { public double Run(double x) { Func<double, double> f = target; return f(x); } static double target(double x) { return Math.Pow(x, 1.5); } }
        public class ViaConstructor { public double Run() { return new Holder().V; } class Holder { public double V; public Holder() { V = Math.Log(3); } } }
        public class ViaGeneric { public double Run(double x) { return Generic<int>.F(x); } static class Generic<T> { public static double F(double x) { return Math.Cos(x); } } }
        public class ViaGenericMethod { public double Run(double x) { return G<int>(x) + G<string>(x); } static double G<T>(double x) { return Math.Cosh(x); } }
        // an interface slot filled by an inherited method, overridden further down
        public class ShapeBase : IShape { public virtual double Area() { return 1; } }
        public class ShapeDerived : ShapeBase { public override double Area() { return Math.Tanh(1); } }
        public class UsesShapeBase { public double Run(ShapeBase s) { return s.Area(); } }

        // dynamic dispatch: found by cha only
        public class Circle : IShape { public double Area() { return Math.Tan(1); } }
        public class Square : IShape { public double Area() { return 4; } }
        public class UsesShape { public double Run(IShape s) { return s.Area(); } }
        public class Derived : Base { public override double Value(double x) { return Math.Asin(x); } }
        public class Deeper : Derived { public override double Twice(double x) { return Math.Acos(x); } }
        public class UsesBase { public double Run(Base b) { return b.Value(0.5); } public double Run2(Base b) { return b.Twice(0.5); } }
        public class UsesConstrained { public double Run<T>(T s) where T : IShape { return s.Area(); } }
        public struct ExplicitShape : IShape { double IShape.Area() { return Math.Sinh(1); } }

        // the other categories
        public class OtherSources
        {
            public float Float(double x) { return (float)x; }
            public double Fma(double a, double b, double c) { return Math.FusedMultiplyAdd(a, b, c); }
            public double MinMax(double a, double b) { return Math.Min(a, b); }
            public int IntMin(int a, int b) { return Math.Min(a, b); }
            public int Count() { return Environment.ProcessorCount; }
            public void Par(int[] a) { System.Threading.Tasks.Parallel.For(0, a.Length, i => a[i] = i); }
            public int Id(ref int c) { return System.Threading.Interlocked.Increment(ref c); }
            public int Enumerate(Dictionary<int, int> d) { int s = 0; foreach (KeyValuePair<int, int> kv in d) s += kv.Value; return s; }
            public void Sort(int[] a) { Array.Sort(a); }
            public double Parse(string s) { return double.Parse(s); }
            public int Rand() { return new Random(1).Next(); }
            public long Clock() { return System.Diagnostics.Stopwatch.GetTimestamp(); }
        }
        public class IntOnly { public int IntMin(int a, int b) { return Math.Min(a, b); } public int Lookup(Dictionary<int, int> d) { return d[1]; } }

        // a terminal type: counted as "coremath", and what it calls is not followed
        public static class Terminal { public static double Sin(double x) { return Math.Sin(x) + Math.FusedMultiplyAdd(x, x, x); } }
        public class UsesTerminal { public double Run(double x) { return Terminal.Sin(x); } }
    }

    static class SelfTest
    {
        public static int Run()
        {
            ModuleDefinition module = ModuleDefinition.ReadModule(typeof(SelfTest).Assembly.Location);
            var graph = new Graph(module, t => t.Name == "Terminal");
            int failed = 0, checks = 0;
            string reach(string type, bool cha)
            {
                TypeDefinition t = Graph.AllTypes(module).Single(x => x.Namespace == "DeterminismReach.Fixtures" && x.Name == type);
                return string.Join(",", Sources.All.Where(graph.Reach(Graph.PublicMethods(t), cha).ContainsKey));
            }
            void expect(string type, string st, string cha)
            {
                checks++;
                string a = reach(type, false), b = reach(type, true);
                if (a == st && b == cha) return;
                failed++;
                Console.WriteLine($"FAIL {type}: static '{a}' (expected '{st}'), cha '{b}' (expected '{cha}')");
            }
            expect("Clean", "", "");
            expect("DirectLibm", "libm", "libm");
            expect("ViaPrivate", "libm", "libm");
            expect("ViaLambda", "libm", "libm");
            expect("ViaMethodGroup", "libm", "libm");
            expect("ViaConstructor", "libm", "libm");
            expect("ViaGeneric", "libm", "libm");
            expect("ViaGenericMethod", "libm", "libm");
            expect("UsesShapeBase", "", "libm");
            expect("UsesShape", "", "libm");
            expect("UsesBase", "", "libm");
            expect("UsesConstrained", "", "libm");
            expect("Square", "", "");
            expect("OtherSources", "fma,float,parallel,ids,cpu-count,clock,random,hash-enum,sort,text,minmax", "fma,float,parallel,ids,cpu-count,clock,random,hash-enum,sort,text,minmax");
            expect("IntOnly", "", "");
            expect("UsesTerminal", "coremath", "coremath");

            // the witness is a shortest path from a public method to the method that uses the source
            {
                checks++;
                TypeDefinition t = Graph.AllTypes(module).Single(x => x.Name == "ViaPrivate");
                string path = string.Join(">", graph.Reach(Graph.PublicMethods(t), false)[Sources.Libm].Select(m => m.Name));
                if (path != "Run>helper>deeper") { failed++; Console.WriteLine("FAIL witness: " + path); }
            }
            // the explicit interface implementation and the override of an override are implementations of their slots
            {
                checks++;
                TypeDefinition t = Graph.AllTypes(module).Single(x => x.Name == "UsesBase");
                var r = graph.Reach(t.Methods.Where(m => m.Name == "Run2"), true);
                if (!r.ContainsKey(Sources.Libm) || r[Sources.Libm].Last().DeclaringType.Name != "Deeper") { failed++; Console.WriteLine("FAIL override of an inherited virtual method"); }
                checks++;
                TypeDefinition u = Graph.AllTypes(module).Single(x => x.Name == "UsesShape");
                var all = new HashSet<string>();
                int i = graph.Index[u.Methods.Single(m => m.Name == "Run")];
                foreach (int j in graph.Cha[i]) all.Add(graph.Methods[j].DeclaringType.Name);
                if (!all.SetEquals(new[] { "IShape", "Circle", "Square", "ExplicitShape", "ShapeBase", "ShapeDerived" })) { failed++; Console.WriteLine("FAIL implementations of IShape.Area: " + string.Join(",", all.OrderBy(x => x))); }
            }
            Console.WriteLine($"self-test: {checks} checks, {failed} failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
