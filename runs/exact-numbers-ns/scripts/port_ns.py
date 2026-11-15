# Throwaway probe port of the limb kernels to netstandard2.0 (no PR). Usage: port_ns.py <worktree>
import re, sys, pathlib
root = pathlib.Path(sys.argv[1])
files = ["math/ExactLimbSigns.cs", "mesh_ops/ExactMeshBoolean.cs", "mesh_ops/ExactMeshArrangement.cs"]
for f in files:
    p = root / f
    s = p.read_text()
    # keep the SkipLocalsInit attribute NET-only (it is not in netstandard2.0)
    s = s.replace("#if NET\n        [SkipLocalsInit]\n#endif\n", "")
    s = re.sub(r"(?m)^[ \t]*#if NET[ \t]*\n", "", s)
    s = re.sub(r"(?m)^[ \t]*#endif[ \t]*\n", "", s)
    if f.endswith("ExactLimbSigns.cs"):
        old_big = "            var r = new BigInteger(new ReadOnlySpan<byte>(v.P, 8 * v.Len), true, false);"
        new_big = ("            var bb = new byte[8 * v.Len];\n"
                   "            unsafe { byte* src = (byte*)v.P; for (int k = 0; k < bb.Length; ++k) bb[k] = src[k]; }\n"
                   "            var r = new BigInteger(bb, true, false);")
        assert s.count(old_big) == 1; s = s.replace(old_big, new_big)
        old_load = ("            var bytes = new Span<byte>(dst, 8 * n);\n"
                    "            // (two's complement, sign-extended to n limbs)\n"
                    "            bytes.Fill(sign < 0 ? (byte)0xFF : (byte)0);\n"
                    "            int written;\n"
                    "            if (v.TryWriteBytes(bytes, out written, false, false) == false)\n"
                    "                throw new InvalidOperationException(\"ExactLimbSigns: an operand wider than its buffer\");")
        new_load = ("            // netstandard2.0 probe: ToByteArray (allocates), two's complement, little endian\n"
                    "            byte[] tb = v.ToByteArray();\n"
                    "            if (tb.Length > 8 * n) throw new InvalidOperationException(\"ExactLimbSigns: an operand wider than its buffer\");\n"
                    "            unsafe {\n"
                    "                byte* bd = (byte*)dst;\n"
                    "                for (int k = 0; k < 8 * n; ++k) bd[k] = k < tb.Length ? tb[k] : (sign < 0 ? (byte)0xFF : (byte)0);\n"
                    "            }")
        assert s.count(old_load) == 1, "load"; s = s.replace(old_load, new_load)
    p.write_text(s)
    print(f, "ok")
