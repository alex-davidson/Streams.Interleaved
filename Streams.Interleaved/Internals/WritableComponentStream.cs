using System;
using System.IO;

namespace Streams.Interleaved.Internals
{
    public class WritableComponentStream : Stream
    {
        private readonly WritableBlockChain chain;
        private readonly uint streamId;

        private WritableBlock block;

        public WritableComponentStream(WritableBlockChain chain, uint streamId)
        {
            this.chain = chain;
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
            // Is this correct? Technically we should probably do something pretty urgent when Flush() is
            // called, but possibly we're far enough from the bare metal that it's appropriate to ignore it?
            block.RequestCommit();
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
                if (block == null) block = chain.AllocateBlock(streamId);
                var written = block.Write(buffer, offset, count);
                length += written;
                if (written >= count) return;
                count -= written;
                offset += written;
                block = chain.AllocateBlock(streamId);
            }
        }
    }
}