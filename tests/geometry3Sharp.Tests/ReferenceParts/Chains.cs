using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using g3;
using gs;

namespace ReferenceParts
{
    /// <summary> one step of a chain: what it did and what the part's checks measured on its result </summary>
    public sealed class StepResult
    {
        public string Name;
        /// <summary> the step produced a mesh (false: it failed or the chain had stopped; Error says why) </summary>
        public bool Ran;
        public string Error = "";
        /// <summary> what the operation reported about itself (eg the repair's result path); descriptive </summary>
        public string Info = "";
        public DMesh3 Mesh;
        public int Triangles;
        public double Seconds;
        public List<CheckResult> Checks = new List<CheckResult>();

        public CheckResult Check(string name) { return Checks.First(c => c.Name == name); }
        /// <summary> the step ran and every functional check passed </summary>
        public bool Passes { get { return Ran && Checks.All(c => !c.Functional || c.Pass); } }
    }

    /// <summary> a chain of operations on one part, with the checks after every step </summary>
    public sealed class ChainResult
    {
        public ReferencePart Part;
        public string Chain;
        public List<StepResult> Steps = new List<StepResult>();
        internal string PendingInfo = "";

        /// <summary>
        /// the first step after the construction at which the functional check fails (or a step fails or is skipped); null if none. Step 0, the
        /// construction with its deliberate defects, is the starting point and not judged
        /// </summary>
        public StepResult FirstFailingStep(string check = null)
        {
            for (int i = 1; i < Steps.Count; ++i) {
                StepResult s = Steps[i];
                if (!s.Ran) return s;
                if (check == null ? !s.Passes : !s.Check(check).Pass) return s;
            }
            return null;
        }

        /// <summary> "-" if every step passes, else the name of the first failing step </summary>
        public string FirstFailingName(string check = null) { StepResult s = FirstFailingStep(check); return s == null ? "-" : s.Name; }
    }

    public static class Chains
    {
        /// <summary> a further step (eg the export through g3cli) that maps the mesh of the step before </summary>
        public sealed class ExtraStep
        {
            public string Name;
            public Func<DMesh3, DMesh3> Run;
        }

        // ---------------- the export steps ----------------

        /// <summary> binary STL (float32 coordinates) in memory and back through the reader (which welds identical vertices) </summary>
        public static DMesh3 StlRoundTrip(DMesh3 mesh)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, true)) {
                var res = new STLWriter().Write(w, new List<WriteMesh> { new WriteMesh(mesh) }, new WriteOptions() { bWriteBinary = true });
                if (res.code != IOCode.Ok) throw new InvalidOperationException("STL write: " + res.message);
            }
            return Read(ms.ToArray(), "stl");
        }

        /// <summary> OBJ text in memory and back </summary>
        public static DMesh3 ObjRoundTrip(DMesh3 mesh)
        {
            var sw = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            var res = new OBJWriter().Write(sw, new List<WriteMesh> { new WriteMesh(mesh) }, WriteOptions.Defaults);
            if (res.code != IOCode.Ok) throw new InvalidOperationException("OBJ write: " + res.message);
            return Read(System.Text.Encoding.UTF8.GetBytes(sw.ToString()), "obj");
        }

        public static DMesh3 Read(byte[] bytes, string extension)
        {
            var builder = new DMesh3Builder();
            var reader = new StandardMeshReader() { MeshBuilder = builder };
            IOReadResult r = reader.Read(new MemoryStream(bytes), extension, ReadOptions.Defaults);
            if (r.code != IOCode.Ok) throw new InvalidOperationException("." + extension + " read: " + r.code + " " + r.message);
            if (builder.Meshes.Count == 0) return new DMesh3();
            if (builder.Meshes.Count == 1) return builder.Meshes[0];
            var merged = new DMesh3();
            var editor = new MeshEditor(merged);
            foreach (DMesh3 m in builder.Meshes) editor.AppendMesh(m);
            return merged;
        }

        // ---------------- the chains ----------------

        static StepResult Step(ChainResult chain, string name, bool afterCut, Func<DMesh3> run)
        {
            var s = new StepResult() { Name = name };
            var sw = Stopwatch.StartNew();
            try {
                s.Mesh = run();
            } catch (Exception e) {
                s.Error = e.GetType().Name + ": " + e.Message;
            }
            s.Seconds = sw.Elapsed.TotalSeconds;
            s.Info = chain.PendingInfo; chain.PendingInfo = "";
            s.Ran = s.Mesh != null;
            if (s.Ran) {
                s.Triangles = s.Mesh.TriangleCount;
                foreach (PartCheck c in chain.Part.Checks) {
                    CheckResult r;
                    try { r = c.Run(chain.Part, s.Mesh, afterCut); }
                    catch (Exception e) { r = new CheckResult() { Name = c.Name, Functional = c.Functional, Value = double.NaN, Detail = "check threw " + e.GetType().Name + ": " + e.Message }; }
                    s.Checks.Add(r);
                }
            }
            chain.Steps.Add(s);
            return s;
        }

        static void Skipped(ChainResult chain, string name, string reason)
        {
            chain.Steps.Add(new StepResult() { Name = name, Ran = false, Error = reason });
        }

        /// <summary>
        /// repair (MeshRobustRepair, defaults) -> subtract the cut-out (ExactMeshBoolean, closed inputs) -> reduce (Reducer, MaxDeviation with
        /// MaxDeviationTwoSided and PreventSelfIntersections, down to a tenth of the triangles or as far as the deviation allows) -> STL
        /// (float32) round trip -> OBJ round trip -> the extra steps. A step that fails stops the chain; the later steps are listed as skipped.
        /// </summary>
        public static ChainResult RunLibraryChain(ReferencePart part, IList<ExtraStep> extra = null)
        {
            var chain = new ChainResult() { Part = part, Chain = "library" };
            double dev = part.Deviation * part.Scale;
            Step(chain, "construct", false, () => new DMesh3(part.Input));
            DMesh3 current = chain.Steps[0].Mesh;
            string repairNote = "";
            current = Run(chain, current, "repair", false, m => {
                var repair = new MeshRobustRepair(new DMesh3(m));
                bool ok = repair.Apply();
                repairNote = ok ? "" : "Apply false: " + repair.ResultReason;
                chain.PendingInfo = "path " + repair.ResultPath + (repair.UsedAlphaWrap ? ", alpha wrap" : "") + (repair.UsedSolidify ? ", solidify" : "");
                return repair.Mesh;
            });
            if (repairNote.Length > 0 && chain.Steps.Last().Ran) chain.Steps.Last().Error = repairNote;
            current = Run(chain, current, "subtract", true, m => {
                var b = new ExactMeshBoolean(m, part.Cutter, ExactMeshBoolean.Operations.Difference) { AllowOpenMeshes = false };
                if (!b.Compute()) throw new InvalidOperationException("boolean " + b.Status + ": " + b.Error);
                return b.Result;
            });
            current = Run(chain, current, "reduce", true, m => {
                var copy = new DMesh3(m, true);
                var reducer = new Reducer(copy) { MaxDeviation = dev, MaxDeviationTwoSided = true, PreventSelfIntersections = true };
                reducer.ReduceToTriangleCount(Math.Max(12, copy.TriangleCount / 10));
                return new DMesh3(copy, true);
            });
            current = Run(chain, current, "stl", true, StlRoundTrip);
            current = Run(chain, current, "obj", true, ObjRoundTrip);
            if (extra != null)
                foreach (ExtraStep e in extra)
                    current = Run(chain, current, e.Name, true, e.Run);
            return chain;
        }

        /// <summary>
        /// SolidBooleanPipeline (InputRepair AutoRepairThenRobust, Difference of the part minus the cut-out, MaxDeviation with MaxDeviationTwoSided,
        /// TargetFraction 0.1) in one step -> STL round trip -> OBJ round trip -> the extra steps
        /// </summary>
        public static ChainResult RunPipelineChain(ReferencePart part, IList<ExtraStep> extra = null)
        {
            var chain = new ChainResult() { Part = part, Chain = "pipeline" };
            double dev = part.Deviation * part.Scale;
            Step(chain, "construct", false, () => new DMesh3(part.Input));
            DMesh3 current = chain.Steps[0].Mesh;
            current = Run(chain, current, "pipeline", true, m => {
                var p = new SolidBooleanPipeline(new[] { m, part.Cutter }, ExactMeshBoolean.Operations.Difference) {
                    InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust,
                    MaxDeviation = dev, MaxDeviationTwoSided = true, TargetFraction = 0.1
                };
                if (!p.Compute()) throw new InvalidOperationException("pipeline " + p.Status + ": " + p.Error + (p.BooleanStatus != ExactMeshBoolean.BooleanStatus.NotComputed ? " (" + p.BooleanStatus + ")" : ""));
                return p.Result;
            });
            current = Run(chain, current, "stl", true, StlRoundTrip);
            current = Run(chain, current, "obj", true, ObjRoundTrip);
            if (extra != null)
                foreach (ExtraStep e in extra)
                    current = Run(chain, current, e.Name, true, e.Run);
            return chain;
        }

        // runs one step on the mesh of the step before; after a failed step the later ones are skipped
        static DMesh3 Run(ChainResult chain, DMesh3 current, string name, bool afterCut, Func<DMesh3, DMesh3> f)
        {
            if (current == null) { Skipped(chain, name, "skipped: an earlier step failed"); return null; }
            DMesh3 input = current;
            StepResult s = Step(chain, name, afterCut, () => f(input));
            return s.Ran ? s.Mesh : null;
        }
    }
}
