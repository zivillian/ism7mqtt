using System;
using System.Threading;

namespace ism7mqtt.ISM7
{
    /*
    Helper class to generate thread-safe ids.
    */
    public static class IdGenerator
    {
        // Our bundle ids start at 10: the fault list uses Wolf's fixed bundle id 3 (Ism7Client.FaultBundleId),
        // so pull, push and write bundles must never use the ids below it.
        private static int _nextBundleId = 9;
        private static int _nextSequenceId = 1;

        public static int GetNextBundleId() => Interlocked.Increment(ref _nextBundleId);
        public static string GetNextBundleIdString() => GetNextBundleId().ToString();

        public static int GetNextSequenceId() => Interlocked.Increment(ref _nextSequenceId);
    }
}