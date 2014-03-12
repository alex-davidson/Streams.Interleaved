using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Streams.Interleaved.Schema
{
    public class MultiplexedStreamManifestSchema : IMultiplexedStreamManifest
    {
        /// <summary>
        /// Magic number, providing a quick file format check.
        /// If this is wrong, this isn't a multiplexed stream we're dealing with.
        /// If it's right, this /may/ be a multiplexed stream.
        /// </summary>
        public readonly uint Magic = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("MUXS"), 0);
        /// <summary>
        /// Feature flags depended upon by this stream.
        /// </summary>
        /// <remarks>
        /// Note: only includes flags which affect serialisation. Does not need to represent all features
        /// supported by the writer.
        /// </remarks>
        public FeatureFlag[] FeatureFlags;
        /// <summary>
        /// Header size per block.
        /// </summary>
        /// <remarks>
        /// This should always be 8. If it's not, the writer is from the future where block headers contain
        /// more info, and the reader should have already failed because it didn't recognise a feature flag.
        /// </remarks>
        public uint HeaderSize = 8;

        public ManifestEntry[] Entries = new ManifestEntry[0];

        IEnumerable<FeatureFlag> IMultiplexedStreamManifest.Features
        {
            get { return FeatureFlags.AsEnumerable(); }
        }

        IEnumerable<ManifestEntry> IMultiplexedStreamManifest.Entries
        {
            get { return Entries.AsEnumerable(); }
        }
    }
}