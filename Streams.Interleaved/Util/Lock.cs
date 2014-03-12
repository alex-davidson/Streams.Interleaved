using System;
using System.Threading;

namespace Streams.Interleaved.Util
{
    /// <summary>
    /// Wrapper for Monitor which provides 'using' semantics.
    /// </summary>
    /// <remarks>
    /// Avoids the 'finally { if(locked) unlock(); }' dance with Monitor.TryEnter.
    /// </remarks>
    public class Lock
    {
        public Lock()
        {
            syncObject = new object();
        }

        private readonly object syncObject;

        public IDisposable Acquire()
        {
            if (Acquired) throw new NotSupportedException("Re-entrancy is not permitted.");
            Monitor.Enter(syncObject);
            return new Scope(syncObject);
        }

        public IDisposable TryAcquire()
        {
            if (Acquired) throw new NotSupportedException("Re-entrancy is not permitted.");
            if (!Monitor.TryEnter(syncObject)) return new Scope();
            return new Scope(syncObject);
        }

        public bool Acquired { get { return Monitor.IsEntered(syncObject); } }

        struct Scope : IDisposable
        {
            private readonly object syncObject;

            public Scope(object syncObject)
            {
                this.syncObject = syncObject;
            }

            public void Dispose()
            {
                if(syncObject != null) Monitor.Exit(syncObject);
            }
        }
    }
}