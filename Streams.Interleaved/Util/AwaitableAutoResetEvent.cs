using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Streams.Interleaved.Util
{
    /// <summary>
    /// Asynchronous implementation of AutoResetEvent.
    /// </summary>
    /// <remarks>
    /// Each time the event is set, only one awaiter will be triggered. The event then resets.
    /// </remarks>
    public sealed class AwaitableAutoResetEvent
    {
        public AwaitableAutoResetEvent(bool initiallySet = false)
        {
            if (initiallySet) Set();
        }

        /// <summary>
        /// Sets the event, causing the first available wait to continue.
        /// </summary>
        /// <returns></returns>
        public void Set()
        {
            SetCurrentEventInstance(); // Do not wait for completion callbacks.
        }

        /// <summary>
        /// Sets the event, causing the first available wait to continue.
        /// If a waiter is already present, returns when it has finished executing.
        /// </summary>
        /// <remarks>
        /// Intended for testing. This should not be used unless you're absolutely
        /// sure you want to run the waiter synchronously.
        /// </remarks>
        /// <returns></returns>
        public void SetAndWait()
        {
            SetCurrentEventInstance().Wait(); // Wait for completion callbacks.
        }

        /// <summary>
        /// Reset the event, if set.
        /// </summary>
        public void Reset()
        {
            ResetInternal(current);
        }

        /// <summary>
        /// Returns a task which completes with true if it takes the event notification, or
        /// false if the time limit expires, or cancels if the specified token is cancelled.
        /// </summary>
        /// <param name="limit"></param>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public Task<bool> One(TimeSpan limit, CancellationToken cancelToken)
        {
            var timeout = new CancellationTokenSource();
            timeout.CancelAfter(limit);
            cancelToken.Register(timeout.Cancel);
            return One(timeout.Token).ContinueWith(t => !t.IsCanceled, cancelToken);
        }

        /// <summary>
        /// Returns a task which completes with true if it takes the event notification, or
        /// false if the time limit expires.
        /// </summary>
        /// <param name="limit"></param>
        /// <returns></returns>
        public Task<bool> One(TimeSpan limit)
        {
            var timeout = new CancellationTokenSource();
            timeout.CancelAfter(limit);
            return One(timeout.Token).ContinueWith(t => !t.IsCanceled);
        }
        /// <summary>
        /// Returns a cancellable task which completes with true if it takes the event
        /// notification, or cancels if the specified token is cancelled.
        /// </summary>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public Task<bool> One(CancellationToken cancelToken)
        {
            return new EventAwaiter(this, cancelToken).Event;
        }

        /// <summary>
        /// Returns a task which completes with true when it takes the event notification.
        /// </summary>
        /// <returns></returns>
        public Task<bool> One()
        {
            return One(CancellationToken.None);
        }



        private EventInstance current = new EventInstance();
        
        /// <summary>
        /// Replace the current event instance with a new, unset one if it has been set AND
        /// if the specified instance is the current one.
        /// </summary>
        /// <param name="instance"></param>
        private void ResetInternal(EventInstance instance)
        {
            if (!instance.IsSet) return;
            if (current != instance) return;
            var newInstance =  new EventInstance();
            Interlocked.CompareExchange(ref current, newInstance, instance); 
        }

        /// <summary>
        /// Sets the event and returns the associated completion object as an atomic operation.
        /// </summary>
        /// <returns></returns>
        private Task SetCurrentEventInstance()
        {
            return current.Set();
        }

        /// <summary>
        /// Get the current event instance's completion task as an atomic operation.
        /// </summary>
        /// <returns></returns>
        private Task<EventInstance> GetCompletionTask()
        {
            return current.Event;
        }

        /// <summary>
        /// Allow a single caller to take the specified instance's event notification.
        /// For all others this will return false. Also resets the event if the specified
        /// notification is still the current one.
        /// </summary>
        /// <param name="instance"></param>
        /// <returns></returns>
        private bool TryTakeEvent(EventInstance instance)
        {
            Debug.Assert(instance.IsSet);
            var result = instance.TryTakeEvent();
            if (result) ResetInternal(instance);
            return result;
        }

        sealed class EventInstance
        {
            /// <summary>
            /// Sets the event synchronously and returns the associated completion callback task.
            /// </summary>
            /// <returns></returns>
            public Task Set()
            {
                isSet = true;
                return Task.Run(() => tcs.TrySetResult(this));
            }

            private volatile bool isSet;
            /// <summary>
            /// Indicates that this event instance has been set and is eligible for
            /// replacement by the next Reset().
            /// </summary>
            public bool IsSet { get { return isSet; } }

            private readonly AutoResetEvent take = new AutoResetEvent(true);

            readonly TaskCompletionSource<EventInstance> tcs = new TaskCompletionSource<EventInstance>();

            public bool TryTakeEvent()
            {
                Debug.Assert(tcs.Task.IsCompleted);
                return take.WaitOne(TimeSpan.Zero);
            }

            public Task<EventInstance> Event { get { return tcs.Task; } }
        }

        sealed class EventAwaiter
        {
            private readonly AwaitableAutoResetEvent eventSource;
            private bool completed;

            public EventAwaiter(AwaitableAutoResetEvent eventSource, CancellationToken cancelToken)
            {
                this.eventSource = eventSource;
                tcs.Task.ContinueWith(t => GC.KeepAlive(this));
                cancelToken.Register(Cancel);
                WaitForTrigger();
            }

            private async void WaitForTrigger()
            {
                while (!completed)
                {
                    var instance = await eventSource.GetCompletionTask();
                    if (completed) return;
                    lock (this)
                    {
                        if (completed) return;
                        if (!eventSource.TryTakeEvent(instance)) continue; // Wait for the next one.
                        completed = true;
                    }
                    tcs.SetResult(true);
                }
            }

            /// <summary>
            /// Try to set the result of this awaiter, if it has not already been set.
            /// </summary>
            /// <param name="result"></param>
            /// <returns></returns>
            private void Cancel()
            {
                lock (this)
                {
                    if (completed) return;
                    completed = true;
                }
                tcs.TrySetCanceled();
            }

            private readonly TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            /// <summary>
            /// Task upon which a caller should wait. Completes if it took the event
            /// notification, or cancels otherwise (eg. timeout). Not necessarily guaranteed
            /// to complete.
            /// </summary>
            public Task<bool> Event { get { return tcs.Task; } }
        }

    }
}
