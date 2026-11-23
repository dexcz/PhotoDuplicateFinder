using System.Diagnostics;
using System.Threading;

namespace PhotoDuplicateFinder
{
    internal sealed class ScanProgress
    {
        // The scanner publishes a new counter and clock together when the units of work change.
        internal sealed class Phase
        {
            public readonly string Name;
            public readonly int Total;
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            private int completed;

            public Phase(string name, int total)
            {
                Name = name;
                Total = total;
            }

            public int Completed { get { return Volatile.Read(ref completed); } }
            public void Advance() { Interlocked.Increment(ref completed); }
        }

        private volatile Phase current = new Phase("Finding photos", -1);
        public Phase Current { get { return current; } }

        public Phase Begin(string name, int total)
        {
            var phase = new Phase(name, total);
            current = phase;
            return phase;
        }
    }
}
