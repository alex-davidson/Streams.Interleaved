using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Streams.Interleaved.Internals;
using Streams.Interleaved.UnitTests.Helpers;

namespace Streams.Interleaved.UnitTests.Internals
{
    [TestFixture]
    public class WritableBlockChainTests
    {
        class CommitRecorder : IBlockCommitTarget
        {
            public CommitRecorder()
            {
                Written = new List<WritableBlock>();
            }

            public IList<WritableBlock> Written { get; private set; }

            public Task Flush(WritableBlock block, System.Threading.CancellationToken token)
            {
                Written.Add(block);

                return Task.FromResult<object>(null);
            }

            public static CommitRecorder None { get { return new CommitRecorder(); } }
        }
        
        [Test]
        public void CanAllocateWritableBlock()
        {
            var chain = new WritableBlockChain(CommitRecorder.None);

            var block = chain.AllocateBlock(0);
            block.WriteByteCount(5);
        }

        [Test]
        public void CanAllocateMultipleActiveBlocks()
        {
            var chain = new WritableBlockChain(CommitRecorder.None);

            var a = chain.AllocateBlock(1);
            var b = chain.AllocateBlock(0);
            a.WriteByteCount(42);
            var c = chain.AllocateBlock(2);
            c.WriteByteCount(280);
            b.WriteByteCount(874);
        }

        [Test]
        public void BlocksAreCommittedInOrderOfAllocation()
        {
            WritableBlock a, b, c;

            var recorder = new CommitRecorder();
            using (var chain = new WritableBlockChain(recorder))
            {
                a = chain.AllocateBlock(1);
                b = chain.AllocateBlock(0);
                a.WriteByteCount(42);
                c = chain.AllocateBlock(2);
                c.WriteByteCount(280);
                b.WriteByteCount(874);

                b.RequestCommit();
                c.RequestCommit();
                a.RequestCommit();
            }
            CollectionAssert.AreEqual(new[] { a, b, c }, recorder.Written);
        }

        [Test]
        public void ClosingTheChainAutomaticallyCommitsAllocatedBlocks()
        {
            var recorder = new CommitRecorder();
            var chain = new WritableBlockChain(recorder);

            var a = chain.AllocateBlock(1);
            var b = chain.AllocateBlock(0);
            a.WriteByteCount(42);
            b.WriteByteCount(874);

            chain.CloseAsync();

            Assert.True(a.Committable.IsCompleted);
            Assert.True(b.Committable.IsCompleted);
        }
    }
}
