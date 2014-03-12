using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Streams.Interleaved.Internals;
using Streams.Interleaved.UnitTests.Helpers;

namespace Streams.Interleaved.UnitTests.Internals
{
    [TestFixture]
    public class WritableBlockTests
    {
        [Test]
        public void SuccessfulWriteReturnsBytesWritten()
        {
            var block = new WritableBlock(0, 4096);

            Assert.AreEqual(42, block.WriteByteCount(42));
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
            block.WriteByteCount(42);

            block.RequestCommit();

            Assert.IsTrue(block.Committable.IsCompleted);
        }

        [Test]
        public void BlockBecomesCommittableWhenCommitThresholdIsReached()
        {
            var block = new WritableBlock(0, 40);
            block.WriteByteCount(42);

            Assert.IsTrue(block.Committable.IsCompleted);
        }
        
        [Test]
        public void CannotAcquireBufferOfUncommittedBlock()
        {
            var block = new WritableBlock(0, 4096);
            block.WriteByteCount(42);

            Assert.Catch<InvalidOperationException>(() => block.AcquireBuffer());
        }

        [Test]
        public void CanAcquireBufferOfCommittedBlockExactlyOnce()
        {
            var block = new WritableBlock(0, 4096);
            block.WriteByteCount(42);

            block.RequestCommit();
            var ms = block.AcquireBuffer();

            Assert.Catch<InvalidOperationException>(() => block.AcquireBuffer());
            Assert.Catch<InvalidOperationException>(() => block.AcquireBuffer());
        }

        [Test]
        public void CommittedBlockReturnsWriteFailure()
        {
            var block = new WritableBlock(0, 4096);
            block.WriteAllBytes(new byte[42]);
            block.RequestCommit();

            Assert.AreEqual(0, block.WriteByteCount(42));
        }

        [Test]
        public void AcquiredBufferOfCommittedBlockContainsAllContent()
        {
            var data = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();

            var block = new WritableBlock(0, 4096);
            block.WriteAllBytes(data);

            block.RequestCommit();

            var ms = block.AcquireBuffer();

            Assert.AreEqual(200, ms.Length);
            CollectionAssert.AreEqual(data, ms.ToArray());
        }

        [Test, Description("Asking a block to commit itself should not block, but it also shouldn't delay the commit until the next write.")]
        [Repeat(1000)] // RequestCommit and Write contend on resources, so stress-test concurrency with thousands of repeats.
        public void RequestingCommitOnNonemptyBlockDoesNotWaitUntilNextWrite()
        {
            var block = new WritableBlock(0, 4096);
            // Put something in the block to avoid any 'if empty, discard me' edge cases in future.
            block.WriteByteCount(42);
            var run = new Barrier(2);

            var request = Task.Factory.StartNew(() => {
                run.SignalAndWait();
                block.RequestCommit();
            });
            var write = Task.Factory.StartNew(() =>
            {
                run.SignalAndWait();
                block.WriteByteCount(42);
            });
            
            Task.WaitAll(request, write);
            Assert.True(block.Committable.IsCompleted);
        }

    }
}
