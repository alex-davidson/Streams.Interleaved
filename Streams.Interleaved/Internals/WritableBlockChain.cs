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
        private static readonly ILog log = LogManager.GetLogger(typeof(WritableBlockChain));

        public WritableBlockChain(IBlockCommitTarget committer)
        {
            var livenessIndicatorSource = new LivenessIndicatorSource();
            lifetime = livenessIndicatorSource.TakeStrongReference();

            commitQueue = new CommitQueue(committer, livenessIndicatorSource.TakeWeakReference());
        }

        public int ActiveBlocks { get { return activeBlocks.Count; } }
        public int BlocksPendingCommit { get { return commitQueue.Count; } }
        public long TotalBlocks { get { return activeBlocks.Count + commitQueue.Count; } }
        public long CommitPayload { get { return commitQueue.Payload; } }
        
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
        /// Confers permission to enqueue to activeBlocks and update the blockCount pointer.
        /// </summary>
        private readonly Lock allocateLock = new Lock();
        /// <summary>
        /// Confers permission to dequeue from activeBlocks, enqueue to the commitQueue, and update the commitPointer.
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
        private readonly CommitQueue commitQueue;

        /// <summary>
        /// Indicates that the head of the activeBlocks queue was committed.
        /// </summary>
        /// <remarks>
        /// This flag is used as a trigger only. By the time something reacts to this, the block in question
        /// may already have been moved to the commit queue.
        /// </remarks>
        private readonly AutoResetEvent commitReady = new AutoResetEvent(false);
        
        private LivenessIndicatorSource.StrongReference lifetime;

        public void AssertNotDisposed()
        {
            lifetime.Indicator.AssertNotAborted();
            if (closing) throw new ObjectDisposedException(GetType().FullName);
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
            lifetime.Indicator.Abort();
        }

        public void Close()
        {
            CloseAsync().Wait();
            lifetime.Release();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Triggers graceful shutdown of the block chain and asynchronously waits on completion.
        /// </summary>
        /// <remarks>
        /// The entire allocated chain is committed synchronously. The returned task completes once all blocks
        /// have been flushed to the commit target.
        /// Exceptions will be thrown asynchronously, ie. on the returned Task.
        /// </remarks>
        /// <returns></returns>
        public async Task CloseAsync()
        {
            lifetime.Indicator.AssertNotAborted();
            if (PreventFurtherAllocations())
            {
                // Prevent further allocations, then mark all active blocks for commit.
                foreach (var block in activeBlocks.ToArray())
                {
                    block.RequestCommit();
                    lifetime.Indicator.AssertNotAborted();
                }
            }
            else
            {
                // Another thread got here first. Wait until everything's marked for commit.
                while (activeBlocks.Count > 0)
                {
                    Thread.Yield();
                    lifetime.Indicator.AssertNotAborted();
                }
            }
            await commitQueue.FlushTask;
        }

        /// <summary>
        /// Attempts to mark the stream as 'closing'. Returns true on the thread which did it.
        /// </summary>
        /// <returns></returns>
        private bool PreventFurtherAllocations()
        {
            using (allocateLock.Acquire())
            {
                if(closing) return false;

                commitQueue.Complete(blockCount);
                closing = true;
                return true;
            }
        }

        public void Dispose()
        {
            Close();
        }

        ~WritableBlockChain()
        {
            lifetime.Release();
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
            commitReady.Set(); // Head block has possibly become committable.
            do
            {
                using (commitLock.TryAcquire())
                {
                    if (!commitLock.Acquired) return;
                    WritableBlock block;
                    while (TryDequeueCommittableHead(out block))
                    {
                        Advance("Commit block {0}", ref commitPointer);
                        commitQueue.Commit(block);
                    }
                    RaceCondition.Test(10);
                }
            }
            while (commitReady.WaitOne(TimeSpan.Zero));
        }

        private void NotifyBlockIsCommittable(long blockIndex, WritableBlock block)
        {
            log.DebugFormat("Committable block: {0}", blockIndex);
            if (IsHeadActiveBlock(block))
            {
                TryCommitBlocks();
            }
        }

        /// <summary>
        /// Implementation of a non-blocking commit queue with flushes performed on a background thread.
        /// </summary>
        public class CommitQueue
        {
            private readonly IBlockCommitTarget committer;
            private readonly LivenessIndicator livenessIndicator;
            private readonly Task backgroundWriter;
            private readonly AwaitableAutoResetEvent workNotification = new AwaitableAutoResetEvent();

            public CommitQueue(IBlockCommitTarget committer, LivenessIndicator livenessIndicator)
            {
                this.committer = committer;
                this.livenessIndicator = livenessIndicator;
                backgroundWriter = BackgroundWriter();
                backgroundWriter.ContinueWith(t => t.Exception);
            }

            public Task FlushTask { get { return backgroundWriter; } }

            /// <summary>
            /// Committable block chain. Blocks are enqueued when committed and dequeued when flushed.
            /// </summary>
            private readonly ConcurrentQueue<WritableBlock> committableBlocks = new ConcurrentQueue<WritableBlock>();

            private volatile bool allBlocksAreCommittable;

            public long Payload { get { return commitPayload; } }
            private long commitPayload = 0;

            /// <summary>
            /// Tail of the committable block chain. Updated only by the BackgroundWriter.
            /// </summary>
            private long tailPointer;

            /// <summary>
            /// Total number of blocks in the stream. This is set when the queue is completed
            /// and sets a target value for tailPointer.
            /// </summary>
            private long blockCount;

            public int Count { get { return committableBlocks.Count; } }

            private bool ThereExistUnflushedBlocks()
            {
                if (!allBlocksAreCommittable) return true;  // Stream is still open. Blocks can still be allocated.
                if (tailPointer < blockCount) return true;  // No more blocks can be allocated, but not all of the existing ones have been flushed.
                return false;
            }

            public void Commit(WritableBlock block)
            {
                Interlocked.Add(ref commitPayload, block.Size);
                committableBlocks.Enqueue(block);
                workNotification.Set();
            }

            private bool ShouldTerminate()
            {
                livenessIndicator.AbortToken.ThrowIfCancellationRequested(); // Abnormal loop termination condition. Entire stream is aborted.
                return !livenessIndicator.ParentIsLive;
            }

            private async Task BackgroundWriter()
            {
                while (ThereExistUnflushedBlocks()) // Graceful loop termination condition. Continue until everything allocated is flushed.
                {
                    await workNotification.One(TimeSpan.FromMilliseconds(50));
                    if (ShouldTerminate()) return;

                    WritableBlock committable;
                    while (committableBlocks.TryDequeue(out committable))
                    {
                        Advance("Flush block {0}", ref tailPointer);
                        try
                        {
                            await committer.Flush(committable, livenessIndicator.AbortToken);
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex);
                            livenessIndicator.Abort();
                            throw;
                        }
                        Interlocked.Add(ref commitPayload, -committable.Size);
                        if (ShouldTerminate()) return;
                    }
                }
            }

            public void Complete(long blockCount)
            {
                this.blockCount = blockCount;
                Interlocked.MemoryBarrier();
                allBlocksAreCommittable = true;
                workNotification.Set();
            }
        }

    }
}