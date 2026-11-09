using System;
using System.Threading;

namespace ism7mqtt.ISM7
{
    /*
    Helper class to generate thread-safe ids.

    The ids only have to be unique within one connection to the ism7 (they correlate requests with responses),
    so every connection owns its own generator and starts at 1 - exactly like a freshly started ism7mqtt.
    Nothing grows beyond the lifetime of one connection, no matter how often ism7mqtt reconnects.
    */
    public class IdGenerator
    {
        private int _nextBundleId = 0;
        private int _nextSequenceId = 1;

        public int GetNextBundleId() => Interlocked.Increment(ref _nextBundleId);
        public string GetNextBundleIdString() => GetNextBundleId().ToString();

        public int GetNextSequenceId() => Interlocked.Increment(ref _nextSequenceId);
    }
}
