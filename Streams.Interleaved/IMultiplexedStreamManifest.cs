using System.Collections.Generic;
using Streams.Interleaved.Schema;

namespace Streams.Interleaved
{
    public interface IMultiplexedStreamManifest
    {
        IEnumerable<FeatureFlag> Features { get; }
        IEnumerable<ManifestEntry> Entries { get; }
    }
}