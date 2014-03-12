using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Streams.Interleaved.Util;

namespace Streams.Interleaved.Internals
{
    public class WritableBlock
    {
        private readonly int commitThreshold;
        public uint StreamId { get; private set; }

        public WritableBlock(uint streamId, int commitThreshold)
        {
            this.commitThreshold = commitThreshold;
            StreamId = streamId;
            this.buffer = new MemoryStream();
        }

        private readonly TaskCompletionSource<WritableBlock> committable = new TaskCompletionSource<WritableBlock>();
        private readonly MemoryStream buffer;
        private readonly Lock writeLock = new Lock();
        private volatile bool commitRequested;
        private volatile bool committed;
        private int flushable;

        public long Size { get { return buffer.Length; } }

        public Task<WritableBlock> Committable
        {
            get { return committable.Task; }
        }

        public MemoryStream AcquireBuffer()
        {
            if (!committed) throw new InvalidOperationException("Block has not been committed.");
            if(Interlocked.Exchange(ref flushable, 0) != 1) throw new InvalidOperationException("Block has already been flushed.");
            Debug.Assert(buffer.Position == 0);
            return buffer;
        }

        public void RequestCommit()
        {
            commitRequested = true;
            TryCommit();
        }

        private void Commit()
        {
            Debug.Assert(writeLock.Acquired);
            if (committed) return;

            Debug.Assert(ShouldCommit());

            buffer.Position = 0;
            flushable = 1;
            committed = true;
            committable.SetResult(this);
        }

        private void TryCommit()
        {
            using (writeLock.TryAcquire())
            {
                if (!writeLock.Acquired) return;
                Commit();
            }
        }

        private bool ShouldCommit()
        {
            return buffer.Length >= commitThreshold || commitRequested;
        }
        
        public int Write(byte[] source, int offset, int count)
        {
            if(committed) return 0;
            using (writeLock.Acquire())
            {
                if (ShouldCommit())
                {
                    Commit();
                    return 0;
                }
                buffer.Write(source, offset, count);
            }
            // Catch the case where the commit request failed to lock because we were writing.
            // This cannot live within the previous lock due to a possible race condition, between
            // checking 'commitRequested' and releasing the lock. We MUST release the lock before
            // testing the flag.
            if (ShouldCommit()) TryCommit();
            RaceCondition.Test(2);
            return count;
        }
    }
}