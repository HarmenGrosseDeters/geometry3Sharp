using System;
using System.Globalization;
using System.IO;
using System.Threading;
using g3;
using SharpFuzz;

namespace ReaderFuzzPilot;

public static class Program
{
    // Match the existing ReaderFuzzTests' explicit parse-error policy; unexpected exceptions remain crashes.
    public static void Check(Stream stream, bool verifySeed = false)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        try {
            var builder = new DMesh3Builder();
            var result = new STLFormatReader().ReadFile(stream, builder, ReadOptions.Defaults, null);
            CheckResult(result, builder, verifySeed);
        } catch (Exception e) when (!verifySeed && (e.GetType() == typeof(Exception) || e.GetType() == typeof(FormatException)
            || e.GetType() == typeof(EndOfStreamException) || e.GetType() == typeof(InvalidDataException)
            || e.GetType() == typeof(IOException) || e.GetType() == typeof(System.Xml.XmlException)
            || e.GetType() == typeof(NotSupportedException) || e.GetType() == typeof(OverflowException))) {
            // A malformed input may be rejected with these documented parse errors.
        }
    }

    static void CheckResult(IOReadResult result, DMesh3Builder builder, bool verifySeed)
    {
        if (result.code == IOCode.GenericReaderError && result.message != null && result.message.StartsWith("exception: ", StringComparison.Ordinal))
            throw new InvalidOperationException("STL reader caught an exception: " + result.message);
        if (verifySeed && (result.code != IOCode.Ok || builder.Meshes.Count != 1 || builder.Meshes[0].TriangleCount != 1))
            throw new InvalidOperationException("Seed did not produce one triangle");
    }

    sealed class UnexpectedFailureStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("synthetic unexpected failure");
        public override int ReadByte() => throw new InvalidOperationException("synthetic unexpected failure");
    }

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "policy-self-test") {
            var builder = new DMesh3Builder();
            CheckResult(new IOReadResult(IOCode.GenericReaderError, "bad input"), builder, false);
            try {
                CheckResult(new IOReadResult(IOCode.GenericReaderError, "exception: synthetic failure"), builder, false);
            } catch (InvalidOperationException) {
                try { Check(new UnexpectedFailureStream()); }
                catch (InvalidOperationException) { return 0; }
                throw new InvalidOperationException("Unexpected reader exception was silently accepted");
            }
            throw new InvalidOperationException("Caught reader exception was silently accepted");
        }
        if (args.Length == 2 && args[0] == "verify-seed") {
            try {
                using var stream = File.OpenRead(args[1]);
                Check(stream, true);
                return 0;
            } catch (Exception e) {
                Console.Error.WriteLine("Invalid seed: " + e.Message);
                return 1;
            }
        }
        if (args.Length == 2 && args[0] == "replay") {
            using var stream = File.OpenRead(args[1]);
            Check(stream);
            return 0;
        }
        if (args.Length != 0) {
            Console.Error.WriteLine("usage: ReaderFuzzPilot [replay <input> | verify-seed <input> | policy-self-test]");
            return 2;
        }
        Fuzzer.LibFuzzer.Run(data => { using var stream = new MemoryStream(data.ToArray(), false); Check(stream); });
        return 0;
    }
}
