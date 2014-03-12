using NUnit.Framework;
using Streams.Interleaved.Internals;

namespace Streams.Interleaved.UnitTests.Helpers
{
    public static class WritingExtensions
    {
        public static int WriteByteCount(this WritableBlock block, int numberOfBytes)
        {
            var bytes = new byte[numberOfBytes];
            return block.Write(bytes, 0, bytes.Length);
        }

        public static void WriteAllBytes(this WritableBlock block, byte[] bytes)
        {
            Assume.That(block.Write(bytes, 0, bytes.Length), Is.EqualTo(bytes.Length));
        }
    }
}
