using System;
using System.Threading;

namespace ism7mqtt.ISM7
{
    /*
    Helper class to generate thread-safe ids.

    The ids only have to be unique within one connection to the ism7 (they correlate requests with responses),
    so every connection owns its own generator and starts again at the beginning - exactly like a freshly started ism7mqtt.
    Nothing grows beyond the lifetime of one connection, no matter how often ism7mqtt reconnects.
    */
    public class IdGenerator
    {
        // Our bundle ids start at 10: the fault list uses Wolf's fixed bundle id 3 (Ism7Client.FaultBundleId),
        // so pull, push and write bundles must never use the ids below it.
        private int _nextBundleId = 9;
        private int _nextSequenceId = 1;

        public int GetNextBundleId() => Interlocked.Increment(ref _nextBundleId);
        public string GetNextBundleIdString() => GetNextBundleId().ToString();

        public int GetNextSequenceId() => Interlocked.Increment(ref _nextSequenceId);
    }
}
