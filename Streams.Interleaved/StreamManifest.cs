using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Streams.Interleaved
{
    public struct ManifestEntry
    {
        public int Identifier { get; set; }
        public string Descriptor { get; set; }
    }

    public enum FeatureFlag : uint
    {

    }

    public class MultiplexedStreamManifest
    {
        /// <summary>
        /// Magic number, providing a quick file format check.
        /// If this is wrong, this isn't a multiplexed stream we're dealing with.
        /// If it's right, this /may/ be a multiplexed stream.
        /// </summary>
        private uint magic;
        /// <summary>
        /// Feature flags depended upon by this stream.
        /// </summary>
        /// <remarks>
        /// Note: only includes flags which affect serialisation. Does not need to represent all features
        /// supported by the writer.
        /// </remarks>
        private FeatureFlag[] featureFlags;
        /// <summary>
        /// Block size, not including headers.
        /// </summary>
        private uint blockSize;
        /// <summary>
        /// Header size per block.
        /// </summary>
        /// <remarks>
        /// This should always be 4. If it's not, the writer is from the future where block headers contain
        /// more info, and the reader should have already failed because it didn't recognise a feature flag.
        /// </remarks>
        private uint headerSize = 4;

        readonly List<ManifestEntry> entries = new List<ManifestEntry>();

        public int AddStream(string descriptor)
        {
            var entry = new ManifestEntry { Identifier = entries.Count, Descriptor = descriptor };
            entries.Add(entry);
            return entry.Identifier;
        }

        public byte[] CreateEmptyBlock()
        {
            return new byte[blockSize + headerSize];
        }

        public int InitialiseBlock(byte[] buffer, uint streamId)
        {
            var header = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(streamId));
            Buffer.BlockCopy(header, 0, buffer, 0, header.Length);
            return header.Length;
        }

        public void WriteTo(Stream stream)
        {
        }
    }

    public struct StreamHeader
    {
        public uint Magic { get; set; }
        public ushort HeaderVersion { get; set; }
        public ushort HeaderMinorVersion { get; set; }
    }

    public struct BlockHeader
    {
        /// <summary>
        /// Identifier of the stream which owns this block.
        /// </summary>
        public uint StreamIdentifier { get; set; } 
    }
}
