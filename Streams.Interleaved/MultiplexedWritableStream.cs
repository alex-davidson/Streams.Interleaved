using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Streams.Interleaved.Internals;
using Streams.Interleaved.Schema;
using Streams.Interleaved.Util;

namespace Streams.Interleaved
{
    /// <summary>
    /// Write-only stream-multiplexing wrapper.
    /// </summary>
    public class MultiplexedWritableStream : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MultiplexedWritableStream));

        public bool CloseUnderlyingStreamWhenDisposed
        {
            get { return commitTarget.CloseUnderlyingStreamWhenDisposed; }
            set { commitTarget.CloseUnderlyingStreamWhenDisposed = value; }
        }


        private readonly Dictionary<uint, WritableComponentStream> allocators = new Dictionary<uint, WritableComponentStream>();
        private readonly CommitBlocksToStream commitTarget;
        private readonly WritableBlockChain blockChain;
        private MultiplexedStreamManifestSchema manifest;
        private readonly CountdownEvent allWritersClosed = new CountdownEvent(1);

        private readonly Lock streamLock = new Lock();
        private bool disposing;

        public MultiplexedWritableStream(Stream underlying, MultiplexedStreamManifest manifest)
        {
            this.manifest = manifest.GetSchemaObject();
            commitTarget = new CommitBlocksToStream(underlying);
            blockChain = new WritableBlockChain(commitTarget);
        }

        
        /// <summary>
        /// Returns a writable stream with the specified identifier.
        /// </summary>
        /// <remarks>
        /// Only one stream instance may be requested per identifier.
        /// </remarks>
        /// <param name="streamId"></param>
        /// <returns></returns>
        public Stream GetStream(uint streamId)
        {
            using (streamLock.Acquire())
            {
                if (disposing) throw new ObjectDisposedException(GetType().FullName, "The multiplexed stream has been disposed.");
                blockChain.AssertNotDisposed();

                var stream = new WritableComponentStream(blockChain, streamId);
                allocators.Add(streamId, stream);
                allWritersClosed.AddCount();
                return stream;
            }
        }

        public void Abort()
        {
            blockChain.Abort();
        }


        public void Dispose()
        {
            using (streamLock.Acquire())
            {
                if (!disposing)
                {
                    disposing = true;
                    allWritersClosed.Signal();
                }
            }
            allWritersClosed.Wait();
            try
            {
                blockChain.Dispose();
            }
            finally
            {
                commitTarget.Dispose();
            }
        }
    }
}