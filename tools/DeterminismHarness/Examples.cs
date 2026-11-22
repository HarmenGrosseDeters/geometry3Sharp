// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The documented operations as cases (docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md): every public static method of
// the compiled examples of docs/OPERATIONS.md (docs/examples/*.cs, namespace g3.Examples) runs on fixed arguments, and
// every public mesh generator with its defaults. The results are traced by reflection: what a method returns and its out
// parameters, with the public members of result objects.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using g3;

namespace DeterminismHarness
{
    static class Examples
    {
        public static void AddTo(List<Case> list)
        {
            // ---- examples.<Class>.<Method>[.<enum value>]
            foreach (Type type in typeof(Examples).Assembly.GetTypes().Where(t => t.Namespace == "g3.Examples" && t.IsClass && t.IsAbstract && t.IsSealed && t.IsPublic)
                         .OrderBy(t => t.Name, StringComparer.Ordinal)) {
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                             .OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => Signature(m), StringComparer.Ordinal)) {
                    if (method.IsGenericMethodDefinition || method.IsSpecialName) continue;
                    string name = "examples." + type.Name.Replace("Example", "") + "." + method.Name;
                    // (overloads: the second and later get a number, in the order of their signatures)
                    seen[name] = seen.TryGetValue(name, out int k) ? k + 1 : 1;
                    if (seen[name] > 1) name += "." + seen[name];
                    ParameterInfo firstEnum = method.GetParameters().FirstOrDefault(p => p.ParameterType.IsEnum);
                    MethodInfo m = method;
                    if (firstEnum == null) list.Add(new Case(name, serial => Call(m, null)));
                    else foreach (object value in Enum.GetValues(firstEnum.ParameterType).Cast<object>().Distinct()) {
                        object v = value;
                        list.Add(new Case(name + "." + v.ToString().ToLowerInvariant(), serial => Call(m, v)));
                    }
                }
            }
            // ---- generators.<Class>: every public mesh generator with a parameterless constructor, with its defaults
            foreach (Type type in typeof(MeshGenerator).Assembly.GetTypes()
                         .Where(t => t.IsPublic && !t.IsAbstract && typeof(MeshGenerator).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                         .OrderBy(t => t.FullName, StringComparer.Ordinal)) {
                Type g = type;
                list.Add(new Case("generators." + g.Name, serial => {
                    var t = new Trace();
                    try {
                        var gen = (MeshGenerator)Activator.CreateInstance(g);
                        gen.Generate();
                        t.Add(gen.MakeDMesh());
                    } catch (Exception e) { Threw(t, e); }
                    return t;
                }));
            }
        }

        static string Signature(MethodInfo m) { return string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name)); }

        static void Threw(Trace t, Exception e)
        {
            if (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            t.Section("threw"); t.Add(1); AddString(t, e.GetType().FullName);
            t.Notes.Add("threw " + e.GetType().Name + ": " + e.Message.Replace('\n', ' '));
        }

        // ---------------------------------------------------------------- arguments
        // The same on every runtime: meshes and points use + - * / and sqrt only (Inputs), numbers are chosen by the
        // parameter's name. A parameter the table does not know makes the case record "unsupported" (and a note), so a new
        // example shows up in the comparison instead of being skipped silently.
        static bool Argument(ParameterInfo p, int meshIndex, object enumValue, out object value)
        {
            Type t = p.ParameterType; string n = p.Name.ToLowerInvariant();
            value = null;
            if (t.IsEnum) { value = enumValue ?? Enum.GetValues(t).GetValue(0); return true; }
            if (t == typeof(DMesh3)) {
                // (the second mesh of a method overlaps the first)
                value = meshIndex == 0 ? Inputs.BumpySphere(8) : Inputs.Sphere(new Vector3d(0.6, 0.2, 0.1), 0.7, 6);
                return true;
            }
            if (t == typeof(bool)) { value = false; return true; }
            if (t == typeof(string)) { value = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "g3-determinism-" + Guid.NewGuid().ToString("N")); return true; }
            if (t == typeof(int)) {
                value = n.Contains("source") ? 0 : n.Contains("target") ? 37 : n.Contains("triangle") ? 200 : n.Contains("count") ? 400 : 3;
                return true;
            }
            if (t == typeof(double)) {
                value = n.Contains("offset") || n.Contains("distance") ? 0.05 : n.Contains("cell") ? 0.1 : n.Contains("edge") ? 0.15
                    : n.Contains("deviation") ? 0.01 : n.Contains("epsilon") ? 0.05 : n.Contains("radius") ? 0.5 : 1.0;
                return true;
            }
            if (t == typeof(Vector3d)) {
                value = n.Contains("normal") ? new Vector3d(0, 0, 1) : n.Contains("origin") ? new Vector3d(0, 0, 0.125) : new Vector3d(0.3, 0.2, 0.1);
                return true;
            }
            if (t.IsAssignableFrom(typeof(List<Vector3d>))) { value = SpherePoints(400); return true; }
            return false;
        }

        /// <summary> points on the unit sphere from the splitmix64 stream, normalized (sqrt and division only) </summary>
        static List<Vector3d> SpherePoints(int count)
        {
            var rng = new Rng(20261122);
            var pts = new List<Vector3d>(count);
            while (pts.Count < count) {
                var v = new Vector3d(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1));
                double l2 = v.x * v.x + v.y * v.y + v.z * v.z;
                if (l2 < 0.01 || l2 > 1) continue;
                double l = Math.Sqrt(l2);
                pts.Add(new Vector3d(v.x / l, v.y / l, v.z / l));
            }
            return pts;
        }

        static Trace Call(MethodInfo method, object enumValue)
        {
            var t = new Trace();
            ParameterInfo[] ps = method.GetParameters();
            var args = new object[ps.Length];
            int meshes = 0; string directory = null;
            for (int i = 0; i < ps.Length; ++i) {
                if (ps[i].IsOut) continue;
                if (ps[i].HasDefaultValue && !ps[i].ParameterType.IsEnum) { args[i] = ps[i].DefaultValue; continue; }
                if (!Argument(ps[i], ps[i].ParameterType == typeof(DMesh3) ? meshes++ : 0, enumValue, out args[i])) {
                    t.Section("unsupported"); t.Add(1);
                    t.Notes.Add("not run: no argument for " + ps[i].ParameterType.Name + " " + ps[i].Name);
                    return t;
                }
                if (ps[i].ParameterType == typeof(string)) directory = (string)args[i];
            }
            try {
                if (directory != null) System.IO.Directory.CreateDirectory(directory);
                object result = method.Invoke(null, args);
                var visited = new HashSet<object>(ReferenceComparer.Instance);
                if (method.ReturnType != typeof(void)) Add(t, "return", result, 0, visited);
                for (int i = 0; i < ps.Length; ++i)
                    // (meshes that the method changes in place are results too)
                    if (ps[i].IsOut || ps[i].ParameterType == typeof(DMesh3)) Add(t, ps[i].Name, args[i], 0, visited);
            } catch (Exception e) {
                Threw(t, e);
            } finally {
                if (directory != null) try { System.IO.Directory.Delete(directory, true); } catch (System.IO.IOException) { }
            }
            return t;
        }

        // ---------------------------------------------------------------- results
        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            bool IEqualityComparer<object>.Equals(object a, object b) { return ReferenceEquals(a, b); }
            int IEqualityComparer<object>.GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        const int MaxDepth = 3, MaxElements = 100000;

        // timings, counters of work and memory, and free text are outside the observable result (DETERMINISM_PROOF.md section 2)
        static readonly string[] Diagnostic = { "second", "millisecond", "elapsed", "time", "timing", "stat", "diagnostic", "phase", "memory",
            "alloc", "profile", "message", "error", "reason", "log", "text", "name", "path", "description" };

        // counters documented as approximate: PointCloudSurface.BandNodes ("two threads may evaluate the same node, so it
        // differs a little from run to run (the mesh does not)")
        static readonly string[] ApproximateCounters = { "BandNodes" };

        static bool IsDiagnostic(string member)
        {
            string n = member.ToLowerInvariant();
            return Diagnostic.Any(d => n.Contains(d)) || ApproximateCounters.Contains(member);
        }

        static void AddString(Trace t, string s)
        {
            // FNV-1a of the UTF-16 code units: string.GetHashCode is seeded per process on .NET
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            t.Add((long)h);
        }

        /// <summary>
        /// Traces a value under a label (the member path): numbers, enums, strings (hashed), meshes, the g3 value types by
        /// their fields, sequences element by element (dictionaries in the order of their keys where the keys are numbers or
        /// strings, else in enumeration order, which is then part of the result), and other objects of the library or the
        /// examples by their public fields and properties in the order of their names, to MaxDepth.
        /// </summary>
        static void Add(Trace t, string label, object v, int depth, HashSet<object> visited)
        {
            t.Section(label);
            if (v == null) { t.Add(-1); return; }
            Type type = v.GetType();
            if (v is double d) { t.Add(d); return; }
            if (v is float f) { t.Add((double)f); return; }
            if (v is bool b) { t.Add(b); return; }
            if (v is string s) { AddString(t, s); return; }
            if (type.IsEnum) { t.Add(Convert.ToInt64(v, CultureInfo.InvariantCulture)); return; }
            if (type.IsPrimitive) { t.Add(v is ulong ul ? (long)ul : Convert.ToInt64(v, CultureInfo.InvariantCulture)); return; }
            if (v is DMesh3 mesh) { if (visited.Add(mesh)) t.Add(mesh, label); else t.Add(-2); return; }
            if (v is Delegate || v is Type || v is MemberInfo || v is System.Threading.CancellationToken) return;
            if (depth > MaxDepth) return;
            if (!type.IsValueType && !visited.Add(v)) { t.Add(-2); return; }
            if (v is IDictionary dict) {
                var keys = dict.Keys.Cast<object>().ToList();
                if (keys.All(k => k is string)) keys = keys.OrderBy(k => (string)k, StringComparer.Ordinal).ToList();
                else if (keys.All(k => k is int || k is long)) keys = keys.OrderBy(k => Convert.ToInt64(k, CultureInfo.InvariantCulture)).ToList();
                t.Add(keys.Count);
                foreach (object k in keys.Take(MaxElements)) { Add(t, label + ".key", k, depth + 1, visited); Add(t, label + "[]", dict[k], depth + 1, visited); }
                return;
            }
            if (v is IEnumerable seq) {
                int n = 0;
                foreach (object e in seq) { if (n++ >= MaxElements) break; Add(t, label + "[]", e, depth + 1, visited); }
                t.Section(label + ".count"); t.Add(n);
                return;
            }
            string ns = type.Namespace ?? "";
            if (!(ns == "g3" || ns == "gs" || ns.StartsWith("g3.", StringComparison.Ordinal))) return;       // (BCL objects: not followed)
            foreach (FieldInfo fi in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name, StringComparer.Ordinal)) {
                if (IsDiagnostic(fi.Name)) continue;
                Add(t, label + "." + fi.Name, fi.GetValue(v), depth + 1, visited);
            }
            // (properties of the g3 value types are derived values, eg Vector3d.Normalized: the fields are the value)
            if (type.IsValueType) return;
            foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(x => x.Name, StringComparer.Ordinal)) {
                if (!pi.CanRead || pi.GetIndexParameters().Length != 0 || IsDiagnostic(pi.Name)) continue;
                object pv;
                try { pv = pi.GetValue(v, null); }
                catch (TargetInvocationException e) { t.Section(label + "." + pi.Name + ".threw"); AddString(t, (e.InnerException ?? e).GetType().FullName); continue; }
                Add(t, label + "." + pi.Name, pv, depth + 1, visited);
            }
        }
    }
}
