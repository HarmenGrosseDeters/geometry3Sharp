using System;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class BufferUtilTests
    {
        // DeflateStream.Read returns partial reads on .NET Core, DecompressZLib used to read only once
        [Fact]
        public void ZLib_RoundTrip_LargeBuffer()
        {
            var rand = new Random(31337);
            byte[] data = new byte[4 * 1024 * 1024];
            for (int i = 0; i < data.Length; ++i)
                data[i] = (byte)((i % 251) ^ (rand.Next() & 0x0F));
            byte[] compressed = BufferUtil.CompressZLib(data, false);
            byte[] restored = BufferUtil.DecompressZLib(compressed);
            Assert.Equal(data, restored);
        }

        [Fact]
        public void ZLib_TruncatedData_Throws()
        {
            byte[] data = new byte[100000];
            for (int i = 0; i < data.Length; ++i)
                data[i] = (byte)(i * 7);
            byte[] compressed = BufferUtil.CompressZLib(data, false);
            byte[] truncated = new byte[compressed.Length / 2];
            Array.Copy(compressed, truncated, truncated.Length);
            Assert.ThrowsAny<Exception>(() => BufferUtil.DecompressZLib(truncated));
        }
    }
}
