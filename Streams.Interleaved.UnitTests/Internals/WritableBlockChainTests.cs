﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Streams.Interleaved.Internals;
using Streams.Interleaved.UnitTests.Helpers;

namespace Streams.Interleaved.UnitTests.Internals
{
    [TestFixture]
    public class WritableBlockChainTests
    {
        [Test]
        public void CanAllocateWritableBlock()
        {
            using (var chain = new WritableBlockChain(CommitRecorder.None))
            {
                var block = chain.AllocateBlock(0);
                block.WriteByteCount(5);
            }
        }

        [Test]
        public void CanAllocateMultipleActiveBlocks()
        {
            using (var chain = new WritableBlockChain(CommitRecorder.None))
            {
                var a = chain.AllocateBlock(1);
                var b = chain.AllocateBlock(0);
                a.WriteByteCount(42);
                var c = chain.AllocateBlock(2);
                c.WriteByteCount(280);
                b.WriteByteCount(874);
            }
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
        public void ClosingTheChainWillSynchronouslyCommitAllocatedBlocks()
        {
            var recorder = new CommitRecorder();
            using (var chain = new WritableBlockChain(recorder))
            {
                var a = chain.AllocateBlock(1);
                var b = chain.AllocateBlock(0);
                a.WriteByteCount(42);
                b.WriteByteCount(874);

                chain.CloseAsync();

                Assert.True(a.Committable.IsCompleted);
                Assert.True(b.Committable.IsCompleted);
            }
        }

        [Test, Repeat(100)]
        public void ClosingTheChainOnMultipleThreads_WaitsForCommitsToCompleteOnEveryThread()
        {
            var recorder = new CommitRecorder();
            using (var chain = new WritableBlockChain(recorder))
            {
                var a = chain.AllocateBlock(1);
                var b = chain.AllocateBlock(0);
                a.WriteByteCount(42);
                b.WriteByteCount(874);

                // This process's running time appears to be nonlinearly related to number of threads.
                // Shouldn't have many threads trying to close at once though, so optimise for small numbers.
                const int threadCount = 5;
                var barrier = new Barrier(threadCount);
                var threads =
                    Enumerable.Range(0, threadCount)
                        .Select(i => Task.Factory.StartNew(() =>
                        {
                            barrier.SignalAndWait();

                            chain.CloseAsync();
                            return a.Committable.IsCompleted && b.Committable.IsCompleted;
                        }))
                        .ToArray();

                Task.WaitAll(threads);

                CollectionAssert.DoesNotContain(threads.Select(t => t.Result), false);
            }
        }

        [Test]
        public void ExceptionThrownByFlush_AbortsTheChain()
        {
            var recorder = new FailingCommitTarget();
            var chain = new WritableBlockChain(recorder);

            var a = chain.AllocateBlock(0);
            a.WriteByteCount(42);
            
            Assert.Catch<Exception>(chain.Close);
        }

        [Test, Repeat(10)]
        [Description("In .NET 4.5 this won't kill the appdomain, but it's still undesirable.")]
        public void ExceptionThrownByFlush_DoesNotTriggerUnobservedTaskException()
        {
            var recorder = new FailingCommitTarget();
            QueueFailingCommitAndDetachInstance(recorder);
            recorder.WaitUntilCalled();
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        [Test, Timeout(1000)]
        public void CommitNotificationDoesNotBlockOnBackgroundWriter()
        {
            var target = new BlockingCommitTarget();
            var indicator = new LivenessIndicatorSource();
            var commitQueue = new WritableBlockChain.CommitQueue(target, indicator.TakeWeakReference());
            var block = new WritableBlock(0, 4096);
            commitQueue.Commit(block);


        }

        private static void QueueFailingCommitAndDetachInstance(IBlockCommitTarget target)
        {
            var chain = new WritableBlockChain(target);

            var a = chain.AllocateBlock(0);
            a.WriteByteCount(42);
            a.RequestCommit();
        }

        class CommitRecorder : IBlockCommitTarget
        {
            public CommitRecorder()
            {
                Written = new List<WritableBlock>();
            }

            public IList<WritableBlock> Written { get; private set; }

            public Task Flush(WritableBlock block, CancellationToken token)
            {
                Written.Add(block);

                return Task.FromResult<object>(null);
            }

            public static CommitRecorder None { get { return new CommitRecorder(); } }
        }

        class FailingCommitTarget : IBlockCommitTarget
        {
            private readonly ManualResetEventSlim called = new ManualResetEventSlim(false);

            public Task Flush(WritableBlock block, CancellationToken token)
            {
                try
                {
                    throw new IOException();
                }
                finally
                {
                    called.Set();
                }
            }

            public void WaitUntilCalled()
            {
                called.Wait();
            }
        }

        class BlockingCommitTarget : IBlockCommitTarget
        {
            private ManualResetEventSlim unblock = new ManualResetEventSlim(false);

            public async Task Flush(WritableBlock block, CancellationToken token)
            {
                unblock.Wait(token);
                await Task.Yield();
            }

            public void Unblock()
            {
                unblock.Set();
            }
        }
    }
}
