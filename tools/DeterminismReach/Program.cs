// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Static reachability of the sources of cross-runtime differences (docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md).
//   DeterminismReach report <library.dll> [--docs <dir or file> ...] [--harness <harness.dll>] [--tsv <file>] [--why <Type> <source>]
//   DeterminismReach self-test
// The call graph is read from the IL of the compiled library (Mono.Cecil). An edge is a call, a constructor call or a
// method whose address is taken (a delegate or lambda that is created counts as called). A method that touches a type
// (a call, a constructor, a field, a conversion, a local of that type) also reaches the type's static constructor and
// its implementations of virtual members declared outside the library (IEnumerator.MoveNext of an iterator,
// IComparable.CompareTo, Equals, GetHashCode, ToString, Dispose): the BCL calls those back. Two graphs are kept:
//   static: a virtual or interface call reaches only the method named at the call site
//   cha:    it also reaches every override and implementation in the library (class hierarchy analysis)
// "static" is a lower bound of what an operation can reach (it misses dynamic dispatch), "cha" an upper bound for code
// inside the library (it counts every implementation of an interface, also those the operation is never given). Neither
// follows a delegate that a caller outside the library passes in, or reflection; hash-enum does not see an enumeration
// by LINQ or through IEnumerable, text does not see string.Format or interpolation, float sees conversions and
// constants only (README, "Limits").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DeterminismReach
{
    /// <summary>The categories of environment-dependent members, by the rule numbers of DETERMINISM_PROOF.md section 5.</summary>
    public static class Sources
    {
        public const string Libm = "libm", CoreMath = "coremath", Fma = "fma", Simd = "simd", Float = "float", Parallel = "parallel",
            Ids = "ids", CpuCount = "cpu-count", Clock = "clock", Random = "random", HashEnum = "hash-enum", Sort = "sort", Text = "text",
            MinMax = "minmax";

        public static readonly string[] All = { Libm, CoreMath, Fma, Simd, Float, Parallel, Ids, CpuCount, Clock, Random, HashEnum, Sort, Text, MinMax };

        static readonly HashSet<string> Transcendental = new HashSet<string>(StringComparer.Ordinal) {
            "Sin", "Cos", "Tan", "SinCos", "Asin", "Acos", "Atan", "Atan2", "Sinh", "Cosh", "Tanh", "Asinh", "Acosh", "Atanh",
            "Exp", "Log", "Log10", "Log2", "Pow", "Cbrt" };

        /// <summary>The category of a member declared outside the analyzed assembly, or null.</summary>
        public static string OfExternal(string declaringType, string name, int parameters, string firstParameterType)
        {
            if (declaringType == "System.Math" || declaringType == "System.MathF" || declaringType == "System.Double" || declaringType == "System.Single") {
                if (Transcendental.Contains(name)) return Libm;                                             // R1
                if (name == "FusedMultiplyAdd" || name.EndsWith("Estimate", StringComparison.Ordinal)) return Fma;  // R3
                if ((name == "Min" || name == "Max") && (firstParameterType == "System.Double" || firstParameterType == "System.Single")) return MinMax;  // R4
                if (name == "Parse" || name == "TryParse" || name == "ToString") return Text;                // R17
                return null;
            }
            if (declaringType.StartsWith("System.Runtime.Intrinsics", StringComparison.Ordinal)) return Simd;   // R3
            if (declaringType == "System.Numerics.Vector" || declaringType.StartsWith("System.Numerics.Vector`", StringComparison.Ordinal)
                || declaringType == "System.Numerics.Tensors.TensorPrimitives") return Simd;
            if (declaringType == "System.Threading.Tasks.Parallel" || declaringType == "System.Threading.ThreadPool"
                || (declaringType == "System.Threading.Thread" && name == ".ctor")
                || (declaringType.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal) && (name == "Run" || name == "StartNew"))) return Parallel;  // R12
            if (declaringType == "System.Threading.Interlocked") return Ids;                                // R13
            if (declaringType == "System.Environment" && name == "get_ProcessorCount") return CpuCount;     // R9
            if (declaringType == "System.Diagnostics.Stopwatch" || (declaringType == "System.Environment" && name.StartsWith("get_TickCount", StringComparison.Ordinal))
                || (declaringType == "System.DateTime" && (name == "get_Now" || name == "get_UtcNow"))) return Clock;  // R9
            if (declaringType == "System.Random" && name == ".ctor") return Random;                         // R9, R10
            if ((declaringType.StartsWith("System.Collections.Generic.Dictionary`2", StringComparison.Ordinal)
                 || declaringType.StartsWith("System.Collections.Generic.HashSet`1", StringComparison.Ordinal))
                && (name == "GetEnumerator" || name == "get_Keys" || name == "get_Values" || name == "CopyTo")) return HashEnum;  // R8
            if ((declaringType == "System.Array" || declaringType == "System.Collections.Generic.List`1" || declaringType == "System.MemoryExtensions")
                && name == "Sort") return Sort;                                                             // R11 (the unstable sorts)
            return null;
        }
    }

    /// <summary>The call graph of one assembly and the sources each method uses directly.</summary>
    public sealed class Graph
    {
        public readonly List<MethodDefinition> Methods = new List<MethodDefinition>();
        public readonly Dictionary<MethodDefinition, int> Index = new Dictionary<MethodDefinition, int>();
        public List<int>[] Static, Cha;
        public HashSet<string>[] Direct;
        readonly ModuleDefinition module;
        readonly Func<TypeDefinition, bool> terminal;

        /// <param name="terminalType">types whose methods are sources themselves and are not traversed (the correctly rounded CoreMath)</param>
        public Graph(ModuleDefinition module, Func<TypeDefinition, bool> terminalType)
        {
            this.module = module; terminal = terminalType;
            foreach (TypeDefinition t in AllTypes(module)) foreach (MethodDefinition m in t.Methods) { Index[m] = Methods.Count; Methods.Add(m); }
            int n = Methods.Count;
            Static = new List<int>[n]; Cha = new List<int>[n]; Direct = new HashSet<string>[n];
            Dictionary<MethodDefinition, List<MethodDefinition>> implementations = Implementations();
            var callbacks = new Dictionary<TypeDefinition, List<int>>();
            for (int i = 0; i < n; ++i) {
                Static[i] = new List<int>(); Cha[i] = new List<int>(); Direct[i] = new HashSet<string>(StringComparer.Ordinal);
                MethodDefinition m = Methods[i];
                if (terminal(m.DeclaringType)) { Direct[i].Add(Sources.CoreMath); continue; }
                if (!m.HasBody) continue;
                var touched = new HashSet<TypeDefinition>();
                foreach (ParameterDefinition p in m.Parameters) Touch(p.ParameterType, touched);
                Touch(m.ReturnType, touched);
                foreach (Instruction ins in m.Body.Instructions) {
                    if (ins.OpCode == OpCodes.Conv_R4 || ins.OpCode == OpCodes.Ldc_R4) Direct[i].Add(Sources.Float);   // R2
                    if (ins.Operand is TypeReference tr) Touch(tr, touched);
                    else if (ins.Operand is FieldReference fr) Touch(fr.DeclaringType, touched);
                    else if (ins.Operand is MethodReference mr) {
                        Touch(mr.DeclaringType, touched);
                        // (Array.Sort<Key>: the BCL calls Key.CompareTo)
                        if (mr is GenericInstanceMethod gm) foreach (TypeReference a in gm.GenericArguments) Touch(a, touched);
                    }
                    if (!(ins.Operand is MethodReference r)) continue;
                    MethodDefinition d = Resolve(r);
                    if (d == null) {
                        TypeReference dt = r.DeclaringType.GetElementType();
                        string first = r.Parameters.Count > 0 ? r.Parameters[0].ParameterType.FullName : "";
                        string s = Sources.OfExternal(dt.FullName, r.Name, r.Parameters.Count, first);
                        if (s != null) Direct[i].Add(s);
                        continue;
                    }
                    int j = Index[d];
                    Static[i].Add(j); Cha[i].Add(j);
                    // (a call on a type parameter is a constrained callvirt)
                    bool dispatch = ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Ldvirtftn || (ins.OpCode == OpCodes.Ldftn && d.IsVirtual);
                    if (dispatch && d.IsVirtual && implementations.TryGetValue(d, out List<MethodDefinition> impl))
                        foreach (MethodDefinition o in impl) Cha[i].Add(Index[o]);
                }
                foreach (TypeDefinition t in touched) {
                    if (t == m.DeclaringType && m.IsConstructor && m.IsStatic) continue;
                    if (!callbacks.TryGetValue(t, out List<int> list)) callbacks[t] = list = Callbacks(t);
                    Static[i].AddRange(list); Cha[i].AddRange(list);
                }
            }
        }

        void Touch(TypeReference t, HashSet<TypeDefinition> touched)
        {
            if (t == null) return;
            if (t is GenericInstanceType g) foreach (TypeReference a in g.GenericArguments) Touch(a, touched);
            if (t is TypeSpecification spec && !(t is GenericInstanceType)) { Touch(spec.ElementType, touched); return; }
            if (t is GenericParameter) return;
            TypeDefinition d = SafeResolve(t);
            if (d != null) touched.Add(d);
        }

        // what the runtime or the BCL may call on a type that a method touches: its static constructor, and its methods
        // that fill a virtual slot declared outside the module (an interface of the BCL, or a virtual method of object)
        List<int> Callbacks(TypeDefinition t)
        {
            var list = new List<int>();
            foreach (MethodDefinition m in t.Methods) {
                if (m.IsConstructor && m.IsStatic) { list.Add(Index[m]); continue; }
                if (!m.IsVirtual || m.IsAbstract) continue;
                bool external = false;
                foreach (MethodReference o in m.Overrides) if (SafeResolve(o) == null) external = true;      // explicit implementation of an outside interface
                if (!external) {
                    // an outside interface or base class of t (or of its bases) declares a member of this name and parameter count
                    for (TypeDefinition c = t; c != null && !external; c = SafeResolve(c.BaseType)) {
                        foreach (InterfaceImplementation ii in c.Interfaces)
                            if (SafeResolve(ii.InterfaceType) == null && Declares(ii.InterfaceType, m)) external = true;
                        if (c.BaseType != null && SafeResolve(c.BaseType) == null && DeclaresInHierarchy(c.BaseType, m)) external = true;
                    }
                }
                if (external) list.Add(Index[m]);
            }
            return list;
        }

        static bool Declares(TypeReference outside, MethodDefinition m)
        {
            TypeDefinition d;
            try { d = outside.Resolve(); } catch (AssemblyResolutionException) { d = null; }
            // (an outside type that cannot be loaded: assume it declares the member)
            if (d == null) return true;
            if (d.Methods.Any(x => x.Name == m.Name && x.Parameters.Count == m.Parameters.Count)) return true;
            return d.Interfaces.Any(i => Declares(i.InterfaceType, m));
        }

        static bool DeclaresInHierarchy(TypeReference outside, MethodDefinition m)
        {
            for (TypeReference c = outside; c != null;) {
                TypeDefinition d;
                try { d = c.Resolve(); } catch (AssemblyResolutionException) { return true; }
                if (d == null) return true;
                if (d.Methods.Any(x => x.IsVirtual && x.Name == m.Name && x.Parameters.Count == m.Parameters.Count)) return true;
                c = d.BaseType;
            }
            return false;
        }

        public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
        {
            var stack = new Stack<TypeDefinition>(module.Types);
            while (stack.Count > 0) { TypeDefinition t = stack.Pop(); yield return t; foreach (TypeDefinition nt in t.NestedTypes) stack.Push(nt); }
        }

        MethodDefinition Resolve(MethodReference r)
        {
            if (r is MethodDefinition def) return def.Module == module ? def : null;
            TypeReference dt = r.DeclaringType.GetElementType();
            if (dt.Scope != module && !(dt.Scope is ModuleDefinition)) return null;     // a member of another assembly
            try { MethodDefinition d = r.Resolve(); return (d != null && d.Module == module) ? d : null; } catch (AssemblyResolutionException) { return null; }
        }

        static string Signature(MethodReference m)
        {
            var sb = new StringBuilder(m.Name).Append('`').Append(m.GenericParameters.Count).Append('(');
            foreach (ParameterDefinition p in m.Parameters) sb.Append(Erase(p.ParameterType)).Append(',');
            return sb.Append(')').ToString();
        }

        // generic parameters and instantiations are compared by shape only: an over-approximation that never loses an implementation
        static string Erase(TypeReference t)
        {
            if (t is GenericParameter) return "?";
            if (t is ArrayType a) return Erase(a.ElementType) + "[]";
            if (t is ByReferenceType b) return Erase(b.ElementType) + "&";
            if (t is GenericInstanceType g) return g.ElementType.FullName;
            return t.ContainsGenericParameter ? "?" : t.FullName;
        }

        static bool SameSignature(MethodReference a, MethodReference b)
        {
            if (a.Parameters.Count != b.Parameters.Count || a.GenericParameters.Count != b.GenericParameters.Count) return false;
            for (int i = 0; i < a.Parameters.Count; ++i) {
                string x = Erase(a.Parameters[i].ParameterType), y = Erase(b.Parameters[i].ParameterType);
                if (x != y && x != "?" && y != "?" && !x.StartsWith("?", StringComparison.Ordinal) && !y.StartsWith("?", StringComparison.Ordinal)) return false;
            }
            return true;
        }

        // for every virtual or interface method of the module: the methods of the module that override or implement it
        Dictionary<MethodDefinition, List<MethodDefinition>> Implementations()
        {
            var result = new Dictionary<MethodDefinition, List<MethodDefinition>>();
            void add(MethodDefinition slot, MethodDefinition impl)
            {
                if (slot == null || slot == impl || slot.Module != module) return;
                if (!result.TryGetValue(slot, out List<MethodDefinition> l)) result[slot] = l = new List<MethodDefinition>();
                if (!l.Contains(impl)) l.Add(impl);
            }
            foreach (TypeDefinition t in AllTypes(module)) {
                if (t.IsInterface) continue;
                // the base classes and all interfaces of t, inside the module
                var bases = new List<TypeDefinition>(); var interfaces = new List<TypeDefinition>();
                for (TypeDefinition c = t; c != null; c = SafeResolve(c.BaseType)) {
                    if (c != t) bases.Add(c);
                    var pending = new Stack<TypeDefinition>(c.Interfaces.Select(i => SafeResolve(i.InterfaceType)).Where(x => x != null));
                    while (pending.Count > 0) {
                        TypeDefinition it = pending.Pop();
                        if (interfaces.Contains(it)) continue;
                        interfaces.Add(it);
                        foreach (InterfaceImplementation ii in it.Interfaces) { TypeDefinition x = SafeResolve(ii.InterfaceType); if (x != null) pending.Push(x); }
                    }
                }
                foreach (MethodDefinition m in t.Methods) {
                    foreach (MethodReference o in m.Overrides) add(SafeResolve(o), m);          // explicit implementations
                    if (!m.IsVirtual) continue;
                    // (every base class: an override of an override fills the slots of all of them)
                    foreach (TypeDefinition b in bases)
                        foreach (MethodDefinition bm in b.Methods)
                            if (bm.IsVirtual && bm.Name == m.Name && SameSignature(bm, m)) add(bm, m);
                }
                // an interface slot is filled by the most derived public method of that name, declared in t or inherited
                foreach (TypeDefinition it in interfaces)
                    foreach (MethodDefinition im in it.Methods) {
                        for (TypeDefinition c = t; c != null; c = SafeResolve(c.BaseType)) {
                            MethodDefinition hit = c.Methods.FirstOrDefault(m => m.Name == im.Name && !m.IsStatic && SameSignature(im, m));
                            if (hit != null) { add(im, hit); break; }
                        }
                    }
            }
            return result;
        }

        TypeDefinition SafeResolve(TypeReference t)
        {
            if (t == null) return null;
            try { TypeDefinition d = t.Resolve(); return (d != null && d.Module == module) ? d : null; } catch (AssemblyResolutionException) { return null; }
        }

        MethodDefinition SafeResolve(MethodReference m)
        {
            try { MethodDefinition d = m.Resolve(); return (d != null && d.Module == module) ? d : null; } catch (AssemblyResolutionException) { return null; }
        }

        /// <summary>The sources reachable from the roots, and for each the first method found that uses it (breadth first, so on a shortest path).</summary>
        public Dictionary<string, List<MethodDefinition>> Reach(IEnumerable<MethodDefinition> roots, bool cha)
        {
            List<int>[] edges = cha ? Cha : Static;
            var parent = new Dictionary<int, int>(); var queue = new Queue<int>();
            foreach (MethodDefinition r in roots) { int i = Index[r]; if (!parent.ContainsKey(i)) { parent[i] = -1; queue.Enqueue(i); } }
            var found = new Dictionary<string, List<MethodDefinition>>(StringComparer.Ordinal);
            while (queue.Count > 0) {
                int i = queue.Dequeue();
                foreach (string s in Direct[i]) {
                    if (found.ContainsKey(s)) continue;
                    var path = new List<MethodDefinition>();
                    for (int k = i; k != -1; k = parent[k]) path.Add(Methods[k]);
                    path.Reverse(); found[s] = path;
                }
                foreach (int j in edges[i]) if (!parent.ContainsKey(j)) { parent[j] = i; queue.Enqueue(j); }
            }
            return found;
        }

        /// <summary>
        /// The public surface of a type: its public and protected methods, constructors and accessors, and the public
        /// and protected methods it inherits from base classes of the module.
        /// </summary>
        public static IEnumerable<MethodDefinition> PublicMethods(TypeDefinition t)
        {
            for (TypeDefinition c = t; c != null;) {
                foreach (MethodDefinition m in c.Methods)
                    if (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly) {
                        if (c != t && m.IsConstructor && !m.IsStatic) continue;      // (a base constructor is reached through the type's own)
                        yield return m;
                    }
                TypeDefinition b = null;
                if (c.BaseType != null) try { b = c.BaseType.Resolve(); } catch (AssemblyResolutionException) { }
                c = (b != null && b.Module == t.Module) ? b : null;
            }
        }
    }

    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "self-test") return SelfTest.Run();
            if (args.Length >= 2 && args[0] == "report") return Report(args);
            if (args.Length >= 2 && args[0] == "tier") return Tier(args);
            Console.Error.WriteLine("usage: report <library.dll> [--docs <dir or file> ...] [--harness <harness.dll>] [--tsv <file>] [--why <Type> <source>] | tier <library.dll> --docs <dir or file> ... --baseline <file> [--tests <dir>] [--update] | self-test");
            return 2;
        }

        static bool IsPublic(TypeDefinition t)
        {
            for (TypeDefinition c = t; c != null; c = c.DeclaringType) if (!(c.IsPublic || c.IsNestedPublic)) return false;
            return true;
        }

        public static bool IsCoreMath(TypeDefinition t)
        {
            for (TypeDefinition c = t; c != null; c = c.DeclaringType) if (c.Namespace == "g3.CoreMathPort") return true;
            return false;
        }

        // the tier rule (TierRule.cs): the documented public types and whether they reach the platform libm on the cha edges
        static int Tier(string[] args)
        {
            var docs = new List<string>(); string baselinePath = null, tests = null; bool update = false;
            for (int i = 2; i < args.Length; ++i) {
                if (args[i] == "--docs" && i + 1 < args.Length) docs.Add(args[++i]);
                else if (args[i] == "--baseline" && i + 1 < args.Length) baselinePath = args[++i];
                else if (args[i] == "--tests" && i + 1 < args.Length) tests = args[++i];
                else if (args[i] == "--update") update = true;
                else { Console.Error.WriteLine("unknown argument " + args[i]); return 2; }
            }
            if (baselinePath == null || docs.Count == 0) { Console.Error.WriteLine("tier needs --docs and --baseline"); return 2; }
            if (!File.Exists(args[1])) { Console.Error.WriteLine("no library " + args[1]); return 2; }
            if (!update && !File.Exists(baselinePath)) { Console.Error.WriteLine("no baseline " + baselinePath + " (--update writes it)"); return 2; }
            foreach (string d in docs) if (!Directory.Exists(d) && !File.Exists(d)) { Console.Error.WriteLine("no documentation " + d); return 2; }
            ModuleDefinition module = ModuleDefinition.ReadModule(args[1]);
            var graph = new Graph(module, IsCoreMath);
            var documentedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string d in docs)
                foreach (string file in Directory.Exists(d) ? Directory.GetFiles(d, "*.md") : new[] { d })
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), "`([A-Z][A-Za-z0-9_]*)[`(.<]")) documentedNames.Add(m.Groups[1].Value);
            var documented = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (TypeDefinition t in Graph.AllTypes(module).Where(t => IsPublic(t) && !IsCoreMath(t) && Graph.PublicMethods(t).Any() && documentedNames.Contains(SimpleName(t)))) {
                bool libm = graph.Reach(Graph.PublicMethods(t).ToList(), true).ContainsKey(Sources.Libm);
                // (two types of one simple name: libm if either)
                documented[SimpleName(t)] = libm || (documented.TryGetValue(SimpleName(t), out bool before) && before);
            }
            TierBaseline baseline = File.Exists(baselinePath) ? TierBaseline.Parse(File.ReadAllText(baselinePath)) : new TierBaseline();
            // (`--update` keeps the arguments of the types that are still documented; it drops a listed type that now reaches libm
            // without an argument, and a stale one: the diff of the list is the review)
            if (update) {
                TierBaseline updated = TierRule.Update(documented, baseline);
                File.WriteAllText(baselinePath, updated.Format(TierHeader), new UTF8Encoding(false));
                Console.WriteLine($"tier 1: {updated.Tier1.Count} types written to {baselinePath}");
                return 0;
            }
            TierRule.Result r = TierRule.Check(documented, baseline);
            Console.WriteLine($"tier 1: {baseline.Tier1.Count} listed types, {documented.Count} documented public types, {documented.Count(k => !k.Value)} of them without a libm edge (cha)");
            foreach (string v in r.Violations) Console.WriteLine("VIOLATION: " + v + " is tier 1 and now reaches the platform libm: `DeterminismReach report <library.dll> --why " + v + " libm` prints the path");
            foreach (string s in r.Stale) Console.WriteLine("STALE: " + s + " is listed but is no longer a documented public type (renamed or removed): --update");
            // the argument of a type that reaches libm names its test: a class of that name has to exist under --tests
            int missingTests = 0;
            if (tests != null) {
                string source = string.Join("\n", Directory.GetFiles(tests, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
                foreach (var kv in baseline.Tier1.Where(kv => kv.Value.Length > 0 && documented.ContainsKey(kv.Key))) {
                    string[] names = Regex.Matches(kv.Value.Substring("argument:".Length), "[A-Z][A-Za-z0-9_]*(?:Tests|Test)[A-Za-z0-9_]*").Cast<Match>().Select(m => m.Value).ToArray();
                    if (!names.Any(n => Regex.IsMatch(source, "class\\s+" + n + "\\b"))) { missingTests++; Console.WriteLine("NO TEST: the argument of " + kv.Key + " names no test class that exists under " + tests); }
                }
            }
            foreach (string a in r.ArgumentsNotNeeded) Console.WriteLine("note: " + a + " has an argument but reaches no libm any more: the argument can go");
            if (r.Candidates.Count > 0) Console.WriteLine("note: " + r.Candidates.Count + " documented types without a libm edge are not listed (--update adds them): " + string.Join(", ", r.Candidates));
            return r.Ok && missingTests == 0 ? 0 : 1;
        }

        // (Cecil names a generic type `Name`1`; the documentation says `Name<`)
        static string SimpleName(TypeDefinition t) { int k = t.Name.IndexOf('`'); return k < 0 ? t.Name : t.Name.Substring(0, k); }

        const string TierHeader = "# Tier 1 of the golden-master policy (docs/design/DETERMINISM.md, \"Golden masters, two tiers\"): documented operations whose\n"
            + "# reachable set (the upper bound `cha` of tools/DeterminismReach) has no platform libm. `DeterminismReach tier` fails when a listed\n"
            + "# type gains a libm edge. A line `Type<TAB>argument: ...` is a type that reaches libm and is tier 1 by an argument and a test.\n"
            + "# Regenerate with `DeterminismReach tier <library.dll> --docs docs/OPERATIONS.md --docs docs/guides --baseline tools/DeterminismReach/tier1.txt --update`.\n";

        static int Report(string[] args)
        {
            var docs = new List<string>(); string harness = null, tsv = null, whyType = null, whySource = null;
            for (int i = 2; i < args.Length; ++i) {
                if (args[i] == "--docs" && i + 1 < args.Length) docs.Add(args[++i]);
                else if (args[i] == "--harness" && i + 1 < args.Length) harness = args[++i];
                else if (args[i] == "--tsv" && i + 1 < args.Length) tsv = args[++i];
                else if (args[i] == "--why" && i + 2 < args.Length) { whyType = args[++i]; whySource = args[++i]; }
                else { Console.Error.WriteLine("unknown argument " + args[i]); return 2; }
            }
            ModuleDefinition module = ModuleDefinition.ReadModule(args[1]);
            var graph = new Graph(module, IsCoreMath);
            List<TypeDefinition> types = Graph.AllTypes(module).Where(t => IsPublic(t) && !IsCoreMath(t) && Graph.PublicMethods(t).Any()).OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();

            if (whyType != null) {
                TypeDefinition t = types.FirstOrDefault(x => x.Name == whyType || x.FullName == whyType);
                if (t == null) { Console.Error.WriteLine("no public type " + whyType); return 2; }
                foreach (bool cha in new[] { false, true }) {
                    Dictionary<string, List<MethodDefinition>> r = graph.Reach(Graph.PublicMethods(t), cha);
                    Console.WriteLine((cha ? "cha: " : "static: ") + (r.TryGetValue(whySource, out List<MethodDefinition> path)
                        ? string.Join(" -> ", path.Select(m => m.DeclaringType.Name + "." + m.Name)) : "not reached"));
                }
                return 0;
            }

            // the operations: public types named in the documentation (`Name` in backticks), and the types the harness uses
            var documented = new HashSet<string>(StringComparer.Ordinal);
            foreach (string d in docs)
                foreach (string file in Directory.Exists(d) ? Directory.GetFiles(d, "*.md") : new[] { d })
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), "`([A-Z][A-Za-z0-9_]*)[`(.<]")) documented.Add(m.Groups[1].Value);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            if (harness != null) {
                ModuleDefinition h = ModuleDefinition.ReadModule(harness);
                foreach (TypeReference r in h.GetTypeReferences()) if (r.Scope.Name == module.Assembly.Name.Name) covered.Add(r.GetElementType().FullName.Replace('/', '+'));
            }

            var rows = new List<(TypeDefinition type, Dictionary<string, List<MethodDefinition>> st, Dictionary<string, List<MethodDefinition>> cha, bool doc, bool cov)>();
            foreach (TypeDefinition t in types) {
                List<MethodDefinition> roots = Graph.PublicMethods(t).ToList();
                rows.Add((t, graph.Reach(roots, false), graph.Reach(roots, true), documented.Contains(t.Name), covered.Contains(t.FullName.Replace('/', '+'))));
            }

            if (tsv != null) {
                var sb = new StringBuilder("type\tdocumented\tharness\tstatic\tcha\tlibm witness (static)\n");
                foreach (var r in rows) {
                    string w = r.st.TryGetValue(Sources.Libm, out List<MethodDefinition> p) ? string.Join(" -> ", p.Select(m => m.DeclaringType.Name + "." + m.Name)) : "";
                    sb.Append(r.type.FullName).Append('\t').Append(r.doc ? 1 : 0).Append('\t').Append(r.cov ? 1 : 0).Append('\t')
                      .Append(string.Join(",", Sources.All.Where(r.st.ContainsKey))).Append('\t').Append(string.Join(",", Sources.All.Where(r.cha.ContainsKey))).Append('\t').Append(w).Append('\n');
                }
                File.WriteAllText(tsv, sb.ToString(), new UTF8Encoding(false));
            }

            int methods = graph.Methods.Count, withBody = graph.Methods.Count(m => m.HasBody);
            Console.WriteLine($"library: {module.Assembly.Name.Name}, {methods} methods ({withBody} with a body), {types.Count} public types, {rows.Count(r => r.doc)} of them named in the documentation, {rows.Count(r => r.cov)} used by the harness");
            Console.WriteLine();
            Console.WriteLine("| source | methods using it directly | public types reaching it (static) | (cha) | documented types (static) | (cha) | documented, reaching it (static), not in the harness |");
            Console.WriteLine("|---|---|---|---|---|---|---|");
            foreach (string s in Sources.All) {
                int direct = graph.Direct.Count(d => d.Contains(s));
                Console.WriteLine($"| {s} | {direct} | {rows.Count(r => r.st.ContainsKey(s))} | {rows.Count(r => r.cha.ContainsKey(s))} | {rows.Count(r => r.doc && r.st.ContainsKey(s))} | {rows.Count(r => r.doc && r.cha.ContainsKey(s))} | {rows.Count(r => r.doc && !r.cov && r.st.ContainsKey(s))} |");
            }
            Console.WriteLine();
            Console.WriteLine("Documented types that reach the platform libm on static edges (type: shortest call path; * = used by the harness):");
            foreach (var r in rows.Where(r => r.doc && r.st.ContainsKey(Sources.Libm)))
                Console.WriteLine("- " + r.type.Name + (r.cov ? "*" : "") + ": " + string.Join(" -> ", r.st[Sources.Libm].Select(m => m.DeclaringType.Name + "." + m.Name)));
            Console.WriteLine();
            Console.WriteLine("Documented types that do not reach the platform libm on static edges (+ = they do with cha; * = used by the harness):");
            Console.WriteLine(string.Join(", ", rows.Where(r => r.doc && !r.st.ContainsKey(Sources.Libm)).Select(r => r.type.Name + (r.cha.ContainsKey(Sources.Libm) ? "+" : "") + (r.cov ? "*" : ""))));
            return 0;
        }
    }
}
