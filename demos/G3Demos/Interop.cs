using G3Demos.Demos;
using Microsoft.JSInterop;

namespace G3Demos
{
    /// <summary> entry for the browser check: runs one case of e2e/cases.json in WebAssembly (same code as the UI) </summary>
    public static class Interop
    {
        [JSInvokable]
        public static string RunCase(string demo, string json)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string s = Probe.RunCase(demo, json);
            return $"[{sw.ElapsedMilliseconds} ms] {s}";
        }
    }
}
