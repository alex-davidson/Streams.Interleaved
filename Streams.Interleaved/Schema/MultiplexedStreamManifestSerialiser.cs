using System.IO;
using System.Linq;
using System.Text;

namespace Streams.Interleaved.Schema
{
    public class MultiplexedStreamManifestSerialiser
    {
        public void Write(Stream stream, MultiplexedStreamManifestSchema schema)
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                Write(writer, schema);
            }
        }

        public IMultiplexedStreamManifest Read(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                var schema = new MultiplexedStreamManifestSchema();
                Read(reader, schema);
                return schema;
            }
        }

        private void Write(BinaryWriter writer, MultiplexedStreamManifestSchema schema)
        {

        }

        private void Read(BinaryReader reader, MultiplexedStreamManifestSchema schema)
        {

        }
    }
}