using System;
using System.Threading;

namespace ism7mqtt.ISM7
{
    /*
    Helper class to generate thread-safe ids.
    */
    public static class IdGenerator
    {
        private static int _nextBundleId = 0;
        private static int _nextSequenceId = 1;

        public static int GetNextBundleId() => Interlocked.Increment(ref _nextBundleId);
        public static string GetNextBundleIdString() => GetNextBundleId().ToString();

        public static int GetNextSequenceId() => Interlocked.Increment(ref _nextSequenceId);
    }
}