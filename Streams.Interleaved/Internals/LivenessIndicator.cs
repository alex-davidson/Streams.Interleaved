using System;
using System.Threading;

namespace Streams.Interleaved.Internals
{
    public class LivenessIndicator
    {
        private readonly WeakReference parentReference;
        /// <summary>
        /// When cancelled, aborts the entire block chain and forces an abrupt shutdown of the writer task.
        /// </summary>
        /// <remarks>
        /// May be invoked from user code, but the original intent is to force a rapid shutdown in the event of a bugcheck.
        /// </remarks>
        private readonly CancellationTokenSource abortTokenSource = new CancellationTokenSource();

        public LivenessIndicator(object parentLifecycle)
        {
            this.parentReference = new WeakReference(parentLifecycle);
        }

        public bool ParentIsLive
        {
            get { return parentReference.IsAlive; }
        }

        public CancellationToken AbortToken
        {
            get { return abortTokenSource.Token; }
        }

        public void AssertNotAborted()
        {
            if (abortTokenSource.IsCancellationRequested) throw new ObjectDisposedException(GetType().FullName, "The multiplexed stream was aborted.");
        }

        public void Abort()
        {
            abortTokenSource.Cancel();
        }
    }
}