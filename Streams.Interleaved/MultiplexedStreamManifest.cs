using System.Collections.Generic;
using System.IO;
using System.Linq;
using Streams.Interleaved.Schema;

namespace Streams.Interleaved
{
    public class MultiplexedStreamManifest : IMultiplexedStreamManifest
    {
        public MultiplexedStreamManifest(MultiplexedStreamManifestSchema schema) : this()
        {
            features.UnionWith(schema.FeatureFlags);
            entries.AddRange(schema.Entries);
        }

        public MultiplexedStreamManifest()
        {
        }

        private readonly HashSet<FeatureFlag> features = new HashSet<FeatureFlag>();
        public ICollection<FeatureFlag> Features { get { return features; } }
        
        /// <summary>
        /// Header size per block.
        /// </summary>
        /// <remarks>
        /// This should always be 8. If it's not, the writer is from the future where block headers contain
        /// more info, and the reader should have already failed because it didn't recognise a feature flag.
        /// </remarks>
        public uint HeaderSize = 8;

        private readonly List<ManifestEntry> entries = new List<ManifestEntry>();

        public IEnumerable<ManifestEntry> Entries
        {
            get { return entries.AsEnumerable(); }
        }

        public int AddStream(string descriptor)
        {
            var entry = new ManifestEntry { Identifier = entries.Count, Descriptor = descriptor };
            entries.Add(entry);
            return entry.Identifier;
        }

        IEnumerable<FeatureFlag> IMultiplexedStreamManifest.Features
        {
            get { return features.AsEnumerable(); }
        }

        public MultiplexedWritableStream Create(Stream underlying, bool closeUnderlyingWhenDisposed = false)
        {
            return new MultiplexedWritableStream(underlying, this) { CloseUnderlyingStreamWhenDisposed = closeUnderlyingWhenDisposed };
        }

        public MultiplexedStreamManifestSchema GetSchemaObject()
        {
            return new MultiplexedStreamManifestSchema()
            {
                FeatureFlags = Features.ToArray(),
                Entries = Entries.ToArray()
            };
        }
    }
}