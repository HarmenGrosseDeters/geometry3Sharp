using System.Threading;
using Xunit;

namespace g3.Tests
{
    // PROBE ONLY (branch probe/blame-hang, never merged): a test that hangs forever, to see what vstest --blame-hang writes on net48.
    public class BlameHangProbeTests
    {
        [Fact]
        public void Hangs_Forever()
        {
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
