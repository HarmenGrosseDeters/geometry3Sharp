using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using ReferenceParts;
using Xunit;

namespace g3cli.Tests
{
    /// <summary>
    /// The reference suite of functional parts (tests/geometry3Sharp.Tests/ReferencePartsTests, benchmarks/ReferenceParts) with the export
    /// through the CLI as a step of the chain: the mesh of the chain's last step is written as binary STL, converted to OBJ by `g3cli convert`
    /// and read back; the part's checks run on the result.
    /// </summary>
    public class ReferencePartsCliTests : IClassFixture<TestMeshes>
    {
        readonly TestMeshes m;
        public ReferencePartsCliTests(TestMeshes meshes) { m = meshes; }

        Chains.ExtraStep ConvertStep(string name)
        {
            return new Chains.ExtraStep() {
                Name = "g3cli",
                Run = mesh => {
                    string stl = Path.Combine(m.Dir, name + ".stl"), obj = Path.Combine(m.Dir, name + ".obj");
                    var res = new StandardMeshWriter().Write(stl, new List<WriteMesh> { new WriteMesh(mesh) }, new WriteOptions() { bWriteBinary = true });
                    Assert.Equal(IOCode.Ok, res.code);
                    var (exit, result) = TestMeshes.Run("convert", stl, obj);
                    Assert.True(exit == 0, result.ToJsonString());
                    Assert.Equal(mesh.TriangleCount, (int)result["triangles"]);
                    return Chains.Read(File.ReadAllBytes(obj), "obj");
                }
            };
        }

        [Fact]
        public void Flange_LibraryChain_ExportThroughG3cliConvert_KeepsThePin()
        {
            ReferencePart part = Parts.Flange(24, DefectSet.None(), PartFrame.Identity);
            ChainResult chain = Chains.RunLibraryChain(part, new[] { ConvertStep("flange-library") });
            Assert.Equal("construct,repair,subtract,reduce,stl,obj,g3cli", string.Join(",", chain.Steps.Select(s => s.Name)));
            Assert.Null(chain.FirstFailingStep());
            StepResult cli = chain.Steps.Last();
            Assert.True(cli.Ran);
            Assert.True(cli.Check("pinMain").Pass && cli.Check("pinCross").Pass && cli.Check("volume").Pass);
            // the mesh went through float32 twice and the pin value stays within a spacing of the library's own export
            Assert.True(System.Math.Abs(cli.Check("pinMain").Value - chain.Steps.First(s => s.Name == "stl").Check("pinMain").Value) < 1e-3);
        }

        [Fact]
        public void HousingRib_PipelineChain_ExportThroughG3cliConvert_KeepsTheRib()
        {
            ReferencePart part = Parts.HousingRib(DefectSet.None(), PartFrame.Identity, 0.4);
            ChainResult chain = Chains.RunPipelineChain(part, new[] { ConvertStep("rib-pipeline") });
            Assert.Null(chain.FirstFailingStep());
            Assert.Equal("g3cli", chain.Steps.Last().Name);
            Assert.True(System.Math.Abs(chain.Steps.Last().Check("ribThickness").Value - 0.4) < 1e-4);
        }
    }
}
