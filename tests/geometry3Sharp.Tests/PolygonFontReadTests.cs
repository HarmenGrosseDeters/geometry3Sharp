using System;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1615: PolygonFont2d.ReadFont allocated from sizes in the file before it read the data (ReadBytes(name length), an array of the
    /// polygon count): 12 bytes cost 954 MB, 16 bytes 1.5 GB, int.MaxValue an OutOfMemoryException. Every size is now checked against
    /// the rest of the stream (and a stream without a length reads in chunks) before it is allocated from.
    /// </summary>
    public class PolygonFontReadTests
    {
        const int Version = 3;      // PolygonFont2d.SerializerVersion

        static byte[] Bytes(Action<BinaryWriter> write)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            write(w);
            w.Flush();
            return ms.ToArray();
        }

        /// <summary> a stream that cannot seek and has no length (a network or pipe stream) </summary>
        sealed class ForwardOnly : Stream
        {
            readonly Stream inner;
            public ForwardOnly(byte[] data) { inner = new MemoryStream(data, false); }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { return inner.Read(buffer, offset, count); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }

        public static System.Collections.Generic.IEnumerable<object[]> HostileFonts()
        {
            yield return new object[] { "name length 1e9 (12 bytes)", Bytes(w => { w.Write(Version); w.Write(1); w.Write(1000000000); }) };
            yield return new object[] { "name length int.Max", Bytes(w => { w.Write(Version); w.Write(1); w.Write(int.MaxValue); }) };
            yield return new object[] { "name length -1", Bytes(w => { w.Write(Version); w.Write(1); w.Write(-1); }) };
            yield return new object[] { "polygon count 2e8 (16 bytes)", Bytes(w => { w.Write(Version); w.Write(1); w.Write(0); w.Write(200000000); }) };
            yield return new object[] { "polygon count int.Max", Bytes(w => { w.Write(Version); w.Write(1); w.Write(0); w.Write(int.MaxValue); }) };
            yield return new object[] { "polygon count 0 (a character without polygons)", Bytes(w => { w.Write(Version); w.Write(1); w.Write(0); w.Write(0); }) };
            yield return new object[] { "polygon count -1", Bytes(w => { w.Write(Version); w.Write(1); w.Write(0); w.Write(-1); }) };
            yield return new object[] { "character count int.Max", Bytes(w => { w.Write(Version); w.Write(int.MaxValue); }) };
        }

        // the allocation of the thread that reads (the reads are serial): 1 MB is a thousand times less than any of the damaged sizes
        const long AllocationLimit = 1 << 20;

        [Theory]
        [MemberData(nameof(HostileFonts))]
        public void DamagedSizes_ThrowQuickly_WithLittleAllocation(string name, byte[] data)
        {
#if NETCOREAPP
            long before = GC.GetAllocatedBytesForCurrentThread();
#endif
            Exception e = Record.Exception(() => PolygonFont2d.ReadFont(new MemoryStream(data, false)));
#if NETCOREAPP
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < AllocationLimit, name + ": allocated " + allocated + " bytes");
#endif
            Assert.NotNull(e);
            Assert.False(e is OutOfMemoryException, name + ": " + e.GetType().Name);
            Assert.True(e is EndOfStreamException || e is InvalidDataException || e.GetType() == typeof(Exception), name + ": " + e.GetType().Name);
        }

        /// <summary> a stream without a length cannot be checked against the rest: memory then follows the data that is there </summary>
        [Theory]
        [MemberData(nameof(HostileFonts))]
        public void DamagedSizes_OnAStreamWithoutLength_AllocateWithTheData(string name, byte[] data)
        {
#if NETCOREAPP
            long before = GC.GetAllocatedBytesForCurrentThread();
#endif
            Exception e = Record.Exception(() => PolygonFont2d.ReadFont(new ForwardOnly(data)));
#if NETCOREAPP
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            // (a name length below 1 MB is read in one piece; the chunked read allocates at most one chunk beyond the data)
            Assert.True(allocated < 4L * AllocationLimit, name + ": allocated " + allocated + " bytes");
#endif
            Assert.NotNull(e);
            Assert.False(e is OutOfMemoryException, name + ": " + e.GetType().Name);
        }

        static PolygonFont2d SampleFont()
        {
            var font = new PolygonFont2d();
            var square = new Polygon2d(new[] { new Vector2d(0, 0), new Vector2d(4, 0), new Vector2d(4, 4), new Vector2d(0, 4) });
            var hole = new Polygon2d(new[] { new Vector2d(1, 1), new Vector2d(1, 3), new Vector2d(3, 3), new Vector2d(3, 1) });
            var withHole = new GeneralPolygon2d(square); withHole.AddHole(hole, false, false);
            font.AddCharacter("A", new[] { withHole });
            font.AddCharacter("B", new[] { new GeneralPolygon2d(square), new GeneralPolygon2d(hole) });
            return font;
        }

        /// <summary> a valid font reads back unchanged, from a stream with and without a length </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ValidFont_RoundTrips(bool forwardOnly)
        {
            PolygonFont2d font = SampleFont();
            byte[] data = Bytes(w => PolygonFont2d.Store(font, w));
            PolygonFont2d back = PolygonFont2d.ReadFont(forwardOnly ? (Stream)new ForwardOnly(data) : new MemoryStream(data, false));
            Assert.Equal(font.Characters.Keys.OrderBy(k => k), back.Characters.Keys.OrderBy(k => k));
            foreach (var pair in font.Characters) {
                GeneralPolygon2d[] a = pair.Value.Polygons, b = back.Characters[pair.Key].Polygons;
                Assert.Equal(a.Length, b.Length);
                for (int i = 0; i < a.Length; ++i) {
                    Assert.Equal(a[i].Outer.Vertices.ToArray(), b[i].Outer.Vertices.ToArray());
                    Assert.Equal(a[i].Holes.Count, b[i].Holes.Count);
                    for (int h = 0; h < a[i].Holes.Count; ++h)
                        Assert.Equal(a[i].Holes[h].Vertices.ToArray(), b[i].Holes[h].Vertices.ToArray());
                }
            }
            // stored again, the same bytes
            Assert.Equal(data, Bytes(w => PolygonFont2d.Store(back, w)));
        }

        /// <summary> a font cut short anywhere throws a stream error, never another exception type </summary>
        [Fact]
        public void TruncatedFont_ThrowsAStreamError_AtEveryLength()
        {
            byte[] data = Bytes(w => PolygonFont2d.Store(SampleFont(), w));
            for (int len = 0; len < data.Length; ++len) {
                Exception e = Record.Exception(() => PolygonFont2d.ReadFont(new MemoryStream(data.Take(len).ToArray(), false)));
                Assert.NotNull(e);
                Assert.True(e is EndOfStreamException || e is InvalidDataException || e.GetType() == typeof(Exception), "length " + len + ": " + e.GetType().Name);
            }
        }
    }
}
