// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;

namespace ReaderFuzz
{
    /// <summary> An input contradicted a reader's contract: an exception outside the documented parse errors, a wrong result or too much allocation. </summary>
    public sealed class ReaderFuzzFinding : Exception
    {
        public ReaderFuzzFinding(string message) : base(message) { }
    }

    public sealed class FuzzTarget
    {
        public string Name;
        /// <summary> true: the seeds and mutations are text (numbers can be replaced by extreme tokens) </summary>
        public bool Text;
        public Func<List<byte[]>> MakeSeeds;
        /// <summary> returns "" or a description of the finding; throws only for a finding of its own kind that Check cannot name </summary>
        public Func<byte[], string> Check;

        public void Run(byte[] data)
        {
            string problem = Check(data);
            if (problem != "") throw new ReaderFuzzFinding(Name + ": " + problem);
        }
    }
}
