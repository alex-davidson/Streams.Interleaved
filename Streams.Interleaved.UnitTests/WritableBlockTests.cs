using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Streams.Interleaved.UnitTests
{
    [TestFixture]
    public class WritableBlockTests
    {
        [Test]
        public void SuccessfulWriteReturnsBytesWritten()
        {
            var block = new WritableBlock(0, 4096);

            Assert.AreEqual(42, block.Write(new byte[42], 0, 42));
        }

        [Test]
        public void BlockIsNotInitiallyCommittable()
        {
            var block = new WritableBlock(0, 4096);

            Assert.IsFalse(block.Committable.IsCompleted);
        }

        [Test]
        public void BlockBecomesCommittableWhenCommitIsRequested()
        {
            var block = new WritableBlock(0, 4096);
            block.Write(new byte[42], 0, 42);

            block.RequestCommit();

            Assert.IsTrue(block.Committable.IsCompleted);
        }

        [Test]
        public void BlockBecomesCommittableWhenCommitThresholdIsReached()
        {
            var block = new WritableBlock(0, 40);
            block.Write(new byte[42], 0, 42);

            Assert.IsTrue(block.Committable.IsCompleted);
        }
        
        [Test]
        public void CannotFlushUncommittedBlock()
        {
            var block = new WritableBlock(0, 4096);
            block.Write(new byte[42], 0, 42);

            Assert.Catch<InvalidOperationException>(() => block.Flush(new MemoryStream(), CancellationToken.None));
        }

        [Test]
        public void CanFlushCommittedBlockExactlyOnce()
        {
            var block = new WritableBlock(0, 4096);
            block.Write(new byte[42], 0, 42);

            block.RequestCommit();
            block.Flush(new MemoryStream(), CancellationToken.None);

            Assert.Catch<InvalidOperationException>(() => block.Flush(new MemoryStream(), CancellationToken.None));
            Assert.Catch<InvalidOperationException>(() => block.Flush(new MemoryStream(), CancellationToken.None));
        }

        [Test]
        public void CommittedBlockReturnsWriteFailure()
        {
            var block = new WritableBlock(0, 4096);
            Assume.That(block.Write(new byte[42], 0, 42), Is.Not.EqualTo(0));
            block.RequestCommit();

            Assert.AreEqual(0, block.Write(new byte[42], 0, 42));
        }

        [Test]
        public void FlushingCommittedBlockWritesAllContentToTargetStream()
        {
            var data = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();

            var block = new WritableBlock(0, 4096);
            Assume.That(block.Write(data, 0, 200), Is.EqualTo(200));

            block.RequestCommit();

            var ms = new MemoryStream();
            block.Flush(ms, CancellationToken.None).Wait();

            Assert.AreEqual(200, ms.Length);
            CollectionAssert.AreEqual(data, ms.ToArray());
        }

        [Test, Repeat(10000), Description("Asking a block to commit itself should not block, but it also shouldn't delay the commit until the next write.")]
        public void RequestingCommitOnNonemptyBlockDoesNotWaitUntilNextWrite()
        {
            var block = new WritableBlock(0, 4096);
            // Put something in the block to avoid any 'if empty, discard me' edge cases in future.
            block.Write(new byte[42], 0, 42);
            var run = new Barrier(2);

            // RequestCommit and Write contend on resources, so stress-test concurrency with thousands of repeats.
            var request = Task.Factory.StartNew(() => {
                run.SignalAndWait();
                block.RequestCommit();
            });
            var write = Task.Factory.StartNew(() =>
            {
                run.SignalAndWait();
                block.Write(new byte[42], 0, 42);
            });
            
            Task.WaitAll(request, write);
            Assert.True(block.Committable.IsCompleted);
        }

    }
}
