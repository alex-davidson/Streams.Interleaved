namespace Streams.Interleaved.Schema
{
    public struct BlockHeader
    {
        /// <summary>
        /// Identifier of the stream which owns this block.
        /// </summary>
        public uint StreamIdentifier { get; set; }
        public int BlockSize { get; set; }
    }
}
