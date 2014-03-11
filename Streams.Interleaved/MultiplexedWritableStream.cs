using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace Streams.Interleaved
{
    public class MultiplexedWritableStream : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MultiplexedWritableStream));


        private readonly Stream underlying;
        private readonly MultiplexedStreamManifest manifest;
        /// <summary>
        /// Head of the available block chain.
        /// </summary>
        private long blockCount;
        /// <summary>
        /// Boundary of the available and committable block chains.
        /// </summary>
        private long commitPointer;
        /// <summary>
        /// Tail of the committable block chain.
        /// </summary>
        private long tailPointer;
        private object allocateLock = new object();
        private object commitLock = new object();
        private CountdownEvent allWritersClosed = new CountdownEvent(1);
        private bool disposed;

        private void AssertNotDisposed()
        {
            lock (allocateLock)
            {
                if (abortTokenSource.IsCancellationRequested) throw new ObjectDisposedException(GetType().FullName, "The multiplexed stream was aborted.");
                if (disposed) throw new ObjectDisposedException(GetType().FullName);
            }
        }

        /// <summary>
        /// Active block chain. Blocks are allocated at the start and committed from the end.
        /// </summary>
        readonly Queue<WritableBlock> activeBlocks = new Queue<WritableBlock>();
        private readonly AutoResetEvent commitReady = new AutoResetEvent(false);
        readonly ConcurrentQueue<WritableBlock> committableBlocks = new ConcurrentQueue<WritableBlock>();
        private readonly CancellationTokenSource abortTokenSource = new CancellationTokenSource();

        private readonly Task backgroundWriter;

        private readonly Dictionary<uint, ComponentStream> allocators = new Dictionary<uint, ComponentStream>();

        public MultiplexedWritableStream(Stream underlying, MultiplexedStreamManifest manifest)
        {
            this.underlying = underlying;
            this.manifest = manifest;
            backgroundWriter = BackgroundWriter();
        }

        public Stream GetStream(uint streamId)
        {
            var stream = new ComponentStream(this, streamId);
            lock (allocateLock)
            {
                AssertNotDisposed();
                allocators.Add(streamId, stream);
                allWritersClosed.AddCount();
            }
            return stream;
        }
        
        private async Task BackgroundWriter()
        {
            while (!allWritersClosed.IsSet)
            {
                WritableBlock committable;
                if (!committableBlocks.TryDequeue(out committable)) await Task.Yield();
                else
                {
                    commitPointer++;
                    log.DebugFormat("Commit block {0}", committable.Index);
                    Debug.Assert(commitPointer == committable.Index);
                    await committable.Flush(underlying, abortTokenSource.Token);
                }
            }
        }


        public void Abort()
        {
            abortTokenSource.Cancel();
            Close(TimeSpan.Zero);
        }

        public void Close()
        {
            Close(TimeSpan.MaxValue);
        }

        public bool Close(TimeSpan timeout)
        {
            lock (allocateLock)
            {
                if (!disposed)
                {
                    disposed = true;
                    allWritersClosed.Signal();
                }
            }
            return backgroundWriter.Wait(timeout);
        }

        private void CommitBlocks()
        {
            var committing = false;
            try
            {
                committing = Monitor.TryEnter(commitLock);
                if(!committing) return;
                do
                {
                    while (activeBlocks.Peek().Committable.IsCompleted)
                    {
                        var block = activeBlocks.Dequeue();
                        committableBlocks.Enqueue(block);
                        Interlocked.Increment(ref commitPointer);
                    }
                }
                while(commitReady.WaitOne(TimeSpan.Zero));
            }
            finally
            {
                Monitor.Exit(commitLock);
            }
        }

        private void FlushCommittableBlock(WritableBlock block)
        {
            log.DebugFormat("Flush block {0}", block.Index);
            if (ReferenceEquals(block, activeBlocks.Peek()))
            {
                commitReady.Set();
                var committing = false;
                try
                {
                    committing = Monitor.TryEnter(commitLock);
                    if (!committing) return;

                    if (!TryGetCommittableBlock(out block, block)) return;
                    if (activeBlocks.Peek() != block) return; // Current head block is not the one which claimed to be ready.
                    while (activeBlocks.Peek().Committable.IsCompleted)
                    {
                        var commitBlock = activeBlocks.Dequeue();
                        commitPointer++;
                        committableBlocks.Enqueue(commitBlock);
                    }
                }
                finally
                {
                    if (committing) Monitor.Exit(commitLock);
                }
            }
        }

        private bool TryGetCommittableBlock(out WritableBlock block, WritableBlock triggeringBlock = null)
        {
            block = null;
            var headBlock = activeBlocks.Peek();
            if (headBlock == null) return false; // No head block.
            if (!headBlock.Committable.IsCompleted) return false; // Head block is not committable.
            if (triggeringBlock != null)
            {
                if (triggeringBlock != headBlock) return false; // Head block is not the one which triggered this operation.
            }

            block = activeBlocks.Dequeue();
            Debug.Assert(ReferenceEquals(block, headBlock));
            return true;
        }

        private WritableBlock AllocateBlock(uint streamId)
        {
            var block = AppendNewBlock();

            block.Committable.ContinueWith(t => FlushCommittableBlock(t.Result));
            return block;
        }

        private WritableBlock AppendNewBlock()
        {
            lock (allocateLock)
            {
                AssertNotDisposed();
                var index = ++blockCount;
                var block = new WritableBlock(index, 4096);
                activeBlocks.Enqueue(block);
                return block;
            }
        }

        class ComponentStream : Stream
        {
            private readonly MultiplexedWritableStream parent;
            private readonly uint streamId;

            private WritableBlock block;

            public ComponentStream(MultiplexedWritableStream parent, uint streamId)
            {
                this.parent = parent;
                this.streamId = streamId;
            }

            public override bool CanRead
            {
                get { return false; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return true; }
            }

            public override void Flush()
            {
                // Explicit flush is not supported, but we silently ignore it because to do otherwise might break the caller.
            }

            private long length = 0;
            public override long Length { get { return length; } }

            public override long Position
            {
                get
                {
                    return Length;
                }
                set
                {
                    throw new NotSupportedException();
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                while (count > 0)
                {
                    if (block == null) block = parent.AllocateBlock(streamId);
                    var written = block.Write(buffer, offset, count);
                    length += written;
                    if (written >= count) return;
                    count -= written;
                    offset += written;
                    block = parent.AllocateBlock(streamId);
                }
            }
        }


        public void Dispose()
        {
        }
    }
}