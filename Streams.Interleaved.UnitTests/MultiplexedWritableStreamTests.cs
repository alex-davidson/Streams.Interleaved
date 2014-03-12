using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Streams.Interleaved.UnitTests
{
    [TestFixture]
    public class MultiplexedWritableStreamTests
    {
        [Test]
        public void WritingToComponentStreamAfterAbortThrowsAnException()
        {
            var manifest = new MultiplexedStreamManifest();
            Assume.That(manifest.AddStream("Test"), Is.EqualTo(0));
            var ms = new MemoryStream();
            var stream = new MultiplexedWritableStream(ms, manifest);

            var component = stream.GetStream(0);

            stream.Abort();

            Assert.Throws<ObjectDisposedException>(() => component.Write(new byte[42], 0, 42));
        }

        [Test]
        public void AcquiringComponentStreamAfterAbortThrowsAnException()
        {
            var manifest = new MultiplexedStreamManifest();
            Assume.That(manifest.AddStream("Test"), Is.EqualTo(0));
            var ms = new MemoryStream();
            var stream = new MultiplexedWritableStream(ms, manifest);

            stream.Abort();
            Assert.Throws<ObjectDisposedException>(() => stream.GetStream(0));
        }

        [Test, Timeout(1000)]
        public void AppendingToComponentStreamAfterAbortThrowsAnException()
        {
            var manifest = new MultiplexedStreamManifest();
            Assume.That(manifest.AddStream("Test"), Is.EqualTo(0));
            var ms = new MemoryStream();
            var stream = new MultiplexedWritableStream(ms, manifest);

            var component = stream.GetStream(0);

            component.Write(new byte[42], 0, 42); // IMPLEMENTATION DETAIL: Ensure that the block is already allocated.

            stream.Abort();

            Assert.Throws<ObjectDisposedException>(() =>
            {
                while (true) component.Write(new byte[2745], 0, 2745);
            });
        }
    }
}
