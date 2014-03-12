using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Streams.Interleaved.Internals;

namespace Streams.Interleaved
{
    public class CommitBlocksToStream : IBlockCommitTarget, IDisposable
    {
        private readonly Stream underlying;

        public CommitBlocksToStream(Stream underlying)
        {
            this.underlying = underlying;
        }

        private volatile bool disposed;

        public bool CloseUnderlyingStreamWhenDisposed { get; set; }

        public async Task Flush(WritableBlock block, CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException(GetType().FullName, "The multiplexed stream was aborted.");
            var buffer = block.AcquireBuffer();
            Debug.Assert(buffer.Length <= Int32.MaxValue); // If we're using 2GB blocks sensibly, technology has come a long way and we should use longs instead.
            await buffer.CopyToAsync(underlying, (int)buffer.Length, token);
        }

        public void Dispose()
        {
            disposed = true;
            if (CloseUnderlyingStreamWhenDisposed)
            {
                underlying.Dispose();
            }
        }
    }
}