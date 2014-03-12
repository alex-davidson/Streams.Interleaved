using System;
using System.Threading;

namespace Streams.Interleaved.Util
{
    /// <summary>
    /// Paranoia is a virtue.
    /// </summary>
    /// <remarks>
    /// Paranoia is a virtue.
    /// Paranoia is a virtue.
    /// Paranoia is a virtue.
    /// Paranoia is a virtue.
    /// Paranoia is a virtue.
    /// </remarks>
    public static class RaceCondition
    {
        /// <summary>
        /// When running with jitter enabled, pause here for up to specified number of milliseconds.
        /// </summary>
        /// <param name="magnitudeInMilliseconds"></param>
        public static void Test(int magnitudeInMilliseconds)
        {
            if (!jitterChecks) return;

            Thread.Sleep(randomJitter.Next(0, magnitudeInMilliseconds));
        }

        private static readonly Random randomJitter = new Random();
        private static volatile bool jitterChecks = false;

        /// <summary>
        /// Turn on random jitter, with the intent of highlighting race conditions at locations which expect to exhibit it.
        /// </summary>
        public static void EnableJitterChecks()
        {
            jitterChecks = true;
        }
    }
}
