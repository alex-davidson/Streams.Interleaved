using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Streams.Interleaved.Util;

namespace Streams.Interleaved.Internals
{
    public class WritableBlockChain : IDisposable
    {
        private readonly IBlockCommitTarget committer;
        private static readonly ILog log = LogManager.GetLogger(typeof(WritableBlockChain));

        public WritableBlockChain(IBlockCommitTarget committer)
        {
            this.committer = committer;
            backgroundWriter = BackgroundWriter();
        }

        public int ActiveBlocks { get { return activeBlocks.Count; } }
        public int BlocksPendingCommit { get { return committableBlocks.Count; } }
        public long TotalBlocks { get { return activeBlocks.Count + committableBlocks.Count; } }
        public long CommitPayload { get { return commitPayload; } }
        private long commitPayload = 0;
        public long EstimateActivePayload()
        {
            return activeBlocks.ToArray().Sum(b => b.Size);
        }

        /// <summary>
        /// Head of the available block chain. Updated only within allocateLock.
        /// </summary>
        private long blockCount;
        /// <summary>
        /// Boundary of the available and committable block chains. Updated only within commitLock.
        /// </summary>
        private long commitPointer;
        /// <summary>
        /// Tail of the committable block chain. Updated only by the BackgroundWriter.
        /// </summary>
        private long tailPointer;

        /// <summary>
        /// Confers permission to enqueue to activeBlocks and update the blockCount pointer.
        /// </summary>
        private readonly Lock allocateLock = new Lock();
        /// <summary>
        /// Confers permission to dequeue from activeBlocks, enqueue to committableBlocks, and update the commitPointer.
        /// </summary>
        private readonly Lock commitLock = new Lock();


        /// <summary>
        /// Indicates when a request to shut down has been received.
        /// </summary>
        /// <remarks>
        /// This is set within the allocateLock, so once it's true we know that blockCount will no longer increment.
        /// </remarks>
        private volatile bool closing;

        /// <summary>
        /// Active block chain. Blocks are enqueued when allocated and dequeued when committed.
        /// </summary>
        private readonly ConcurrentQueue<WritableBlock> activeBlocks = new ConcurrentQueue<WritableBlock>();
        /// <summary>
        /// Committable block chain. Blocks are enqueued when committed and dequeued when flushed.
        /// </summary>
        private readonly ConcurrentQueue<WritableBlock> committableBlocks = new ConcurrentQueue<WritableBlock>();

        /// <summary>
        /// Indicates that the head of the activeBlocks queue was committed.
        /// </summary>
        /// <remarks>
        /// This flag is used as a trigger only. By the time something reacts to this, the block in question
        /// may already have been moved to the commit queue.
        /// </remarks>
        private readonly AutoResetEvent commitReady = new AutoResetEvent(false);
        /// <summary>
        /// When cancelled, aborts the entire block chain and forces an abrupt shutdown of the writer task.
        /// </summary>
        /// <remarks>
        /// May be invoked from user code, but the original intent is to force a rapid shutdown in the event of a bugcheck.
        /// </remarks>
        private readonly CancellationTokenSource abortTokenSource = new CancellationTokenSource();

        private readonly Task backgroundWriter;

        public void AssertNotAborted()
        {
            if (abortTokenSource.IsCancellationRequested) throw new ObjectDisposedException(GetType().FullName, "The multiplexed stream was aborted.");
        }

        public void AssertNotDisposed()
        {
            AssertNotAborted();
            if (closing) throw new ObjectDisposedException(GetType().FullName);
        }

        private bool ThereExistUnflushedBlocks()
        {
            if(!closing) return true;                   // Stream is still open. Blocks can still be allocated.
            if(blockCount > tailPointer) return true;   // No more blocks can be allocated, but not all of the existing ones have been flushed.
            return false;
        }

        private async Task BackgroundWriter()
        {
            while (ThereExistUnflushedBlocks()) // Graceful loop termination condition. Continue until everything allocated is flushed.
            {
                WritableBlock committable;
                if (committableBlocks.TryDequeue(out committable))
                {
                    Advance("Flush block {0}", ref tailPointer);
                    await committer.Flush(committable, abortTokenSource.Token);
                    Interlocked.Add(ref commitPayload, -committable.Size);
                }
                await Task.Yield();

                abortTokenSource.Token.ThrowIfCancellationRequested(); // Abnormal loop termination condition. Entire stream is aborted.
            }
        }

        public WritableBlock AllocateBlock(uint streamId)
        {
            AssertNotDisposed();
            using (allocateLock.Acquire())
            {
                AssertNotDisposed();
                var index = Advance("Allocate block {0}", ref blockCount);
                var block = new WritableBlock(streamId, 4096);
                block.Committable.ContinueWith(t => NotifyBlockIsCommittable(index, t.Result));
                activeBlocks.Enqueue(block);
                return block;
            }
        }

        /// <summary>
        /// Debug-logs the specified counter's current value, then increments it. Returns the logged value.
        /// </summary>
        /// <param name="pointer"></param>
        /// <param name="messageFormat"></param>
        /// <returns></returns>
        private static long Advance(string messageFormat, ref long pointer)
        {
            var index = Interlocked.Increment(ref pointer) - 1; // Return the previous value. First block is 0.
            log.DebugFormat(messageFormat, index);
            return index;
        }

        public void Abort()
        {
            abortTokenSource.Cancel();
            using (allocateLock.Acquire())
            {
                closing = true;
            }
        }

        public void Close()
        {
            CloseAsync().Wait();
        }

        /// <summary>
        /// Triggers graceful shutdown of the block chain and asynchronously waits on completion.
        /// </summary>
        /// <remarks>
        /// The entire allocated chain is committed synchronously. The returned task completes once all blocks
        /// have been flushed to the commit target.
        /// </remarks>
        /// <returns></returns>
        public Task CloseAsync()
        {
            AssertNotAborted();
            if (closing)
            {
                // Another thread got here first. Wait until everything's marked for commit.
                var spinwait = new SpinWait();
                while (activeBlocks.Count > 0)
                {
                    spinwait.SpinOnce();
                    AssertNotAborted();
                }
            }
            else
            {
                // Prevent further allocations, then mark all active blocks for commit.
                using (allocateLock.Acquire())
                {
                    closing = true;
                }
                foreach (var block in activeBlocks.ToArray())
                {
                    block.RequestCommit();
                }
            }
            return backgroundWriter;
        }

        public void Dispose()
        {
            Close();
        }

        private bool TryDequeueCommittableHead(out WritableBlock block)
        {
            Debug.Assert(commitLock.Acquired);

            block = null;
            WritableBlock headBlock;

            if (!activeBlocks.TryPeek(out headBlock)) return false; // No blocks present.
            if (!headBlock.Committable.IsCompleted) return false;   // Head block is not ready for commit.

            if (!activeBlocks.TryDequeue(out block))
            {
                BugCheck(true, "Successfully peeked a committable block, but was unable to dequeue it. Is something else consuming activeBlocks?");
                return false;
            }
            if (ReferenceEquals(block, headBlock)) return true;

            BugCheck(false, "Peeked a committable block but dequeued another. Is something else consuming activeBlocks?");
            return false;
        }

        private bool IsHeadActiveBlock(WritableBlock block)
        {
            WritableBlock headBlock;
            if (!activeBlocks.TryPeek(out headBlock)) return false; // No blocks present.
            return ReferenceEquals(headBlock, block);
        }

        /// <summary>
        /// Logs a fatal error, raises a debug assertion, and optionally aborts the entire
        /// stream if the bug is severe enough to corrupt state unrecoverably.
        /// </summary>
        /// <param name="canRecover"></param>
        /// <param name="message"></param>
        private void BugCheck(bool canRecover, string message)
        {
            log.Fatal(message);
            if (!canRecover)
            {
                // Cannot recover from this. Kill the stream.
                Abort();
                Debug.Fail("FATAL: " + message);
            }
            else
            {
                Debug.Fail("BUG: " + message);
            }
        }

        private void TryCommitBlocks()
        {
            using (commitLock.TryAcquire())
            {
                if (!commitLock.Acquired) return;
                do
                {
                    WritableBlock block;
                    while (TryDequeueCommittableHead(out block))
                    {
                        Interlocked.Add(ref commitPayload, block.Size);
                        Advance("Commit block {0}", ref commitPointer);
                        committableBlocks.Enqueue(block);
                    }
                }
                while (commitReady.WaitOne(TimeSpan.Zero));
                RaceCondition.Test(10);
            }
        }

        private void NotifyBlockIsCommittable(long blockIndex, WritableBlock block)
        {
            log.DebugFormat("Committable block: {0}", blockIndex);
            if (IsHeadActiveBlock(block))
            {
                commitReady.Set(); // Head block has possibly become committable.
                TryCommitBlocks();
            }
        }
    }
}