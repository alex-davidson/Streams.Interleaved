namespace Streams.Interleaved.Schema
{
    public struct ManifestEntry
    {
        /// <summary>
        /// Stream index.
        /// </summary>
        public int Identifier { get; set; }
        /// <summary>
        /// Serialised application-readable 'key' for the stream.
        /// </summary>
        /// <remarks>
        /// Strings offer a more robust separation between application logic and the MultiplexedStream binary format than
        /// do arbitrary binary-serialisable objects. Also maps cleanly to such uses as database table names, file paths, etc.
        /// </remarks>
        public string Descriptor { get; set; }
    }
}