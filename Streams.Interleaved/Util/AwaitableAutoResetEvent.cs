using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
    public class AwaitableAutoResetEvent
    {
        public AwaitableAutoResetEvent(bool initiallySet = false)
        {
            if (initiallySet) Set();
        }

        private readonly object @lock = new object();
        private TaskCompletionSource<bool> current = new TaskCompletionSource<bool>();
        private volatile bool isSet;

        /// <summary>
        /// Triggers any live waits on this event.
        /// </summary>
        /// <returns></returns>
        public void Set()
        {
            if (isSet) return;

            var currentEvent = AcquireCurrentEventForSet();
            currentEvent.TrySetResult(true);
        }

        private TaskCompletionSource<bool> AcquireCurrentEventForSet()
        {
            lock (@lock)
            {
                isSet = true;
                return current;
            }
        }

        public void Reset()
        {
            if (!isSet) return;
            lock (@lock)
            {
                ResetInternal();
            }
        }

        private bool ResetInternal()
        {
            Debug.Assert(Monitor.IsEntered(@lock));
            if (!isSet) return false;
            isSet = false;
            current = new TaskCompletionSource<bool>();
            return true;
        }

        private bool TryTakeEvent()
        {
            if (!isSet) return false;
            lock (@lock)
            {
                return ResetInternal();
            }
        }

        private Task GetCompletionTask()
        {
            lock (@lock)
            {
                return current.Task;
            }
        }

        class EventAwaiter
        {
            private readonly AwaitableAutoResetEvent eventSource;
            private bool completed;

            public EventAwaiter(AwaitableAutoResetEvent eventSource)
            {
                this.eventSource = eventSource;
                tcs.Task.ContinueWith(t => GC.KeepAlive(this));
                WaitForTrigger();
            }

            private async void WaitForTrigger()
            {
                while (!tcs.Task.IsCompleted)
                {
                    await eventSource.GetCompletionTask();
                    if (tcs.Task.IsCompleted) return;
                    lock (this)
                    {
                        if (tcs.Task.IsCompleted) return;
                        if (!eventSource.TryTakeEvent()) continue; // Wait for the next one.
                        completed = true;
                    }
                    tcs.SetResult(true);
                }
            }

            protected bool TrySetResult(bool result)
            {
                lock (this)
                {
                    if (completed) return false;
                    completed = true;
                }
                tcs.SetResult(result);
                return true;
            }

            private readonly TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            public Task<bool> Event { get { return tcs.Task; } }
        }

        class TimeLimitedEventAwaiter : EventAwaiter
        {
            public TimeLimitedEventAwaiter(TimeSpan waitDuration, AwaitableAutoResetEvent eventSource)
                : base(eventSource)
            {
                WaitForTimeout(waitDuration);
            }

            private async void WaitForTimeout(TimeSpan waitDuration)
            {
                await Task.Delay(waitDuration);
                TrySetResult(false);
            }
        }

        public Task<bool> One(TimeSpan limit)
        {
            return new TimeLimitedEventAwaiter(limit, this).Event;
        }

        public Task<bool> One()
        {
            return new EventAwaiter(this).Event;
        }
    }
}
