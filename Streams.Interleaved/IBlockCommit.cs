using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Streams.Interleaved.Internals;

namespace Streams.Interleaved
{
    public interface IBlockCommitTarget
    {
        Task Flush(WritableBlock block, CancellationToken token);
    }
}