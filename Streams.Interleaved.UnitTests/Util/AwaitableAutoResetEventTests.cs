using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Streams.Interleaved.Util;

namespace Streams.Interleaved.UnitTests.Util
{
    [TestFixture]
    public class AwaitableAutoResetEventTests
    {
        [Test]
        public void EventSetOnceTriggersOnlyOneAwaiter()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var awaiters = new[]{
                autoResetEvent.One(),
                autoResetEvent.One(),
                autoResetEvent.One()
            };

            Assume.That(awaiters.Select(a => a.IsCompleted), Is.All.False);

            autoResetEvent.SetAndWait();

            Assert.That(awaiters.Count(a => a.IsCompleted), Is.EqualTo(1));
        }

        [Test]
        public void EventSetTwiceTriggersTwoAwaiters()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var awaiters = new[]{
                autoResetEvent.One(),
                autoResetEvent.One(),
                autoResetEvent.One()
            };

            Assume.That(awaiters.Select(a => a.IsCompleted), Is.All.False);

            autoResetEvent.SetAndWait();
            autoResetEvent.SetAndWait();

            Assert.That(awaiters.Count(a => a.IsCompleted), Is.EqualTo(2));
        }

        [Test]
        public void ResultOfTimedOutAwaiterIsFalse()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var awaiter = autoResetEvent.One(TimeSpan.FromMilliseconds(10));
            
            Assert.That(awaiter.Result, Is.False);
        }

        [Test]
        public void ResultOfTriggeredAwaiterIsTrue()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var awaiter = autoResetEvent.One();
            autoResetEvent.SetAndWait();

            Assert.That(awaiter.Result, Is.True);
        }

        [Test]
        public void ResetDoesNotDetachActiveAwaiters()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var awaiter = autoResetEvent.One();
            autoResetEvent.Reset();
            autoResetEvent.SetAndWait();

            Assert.That(awaiter.Result, Is.True);
        }

        [Test, Repeat(5)]
        public void ResetDoesNotMaskPreviousSetForExistingAwaiters()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var awaiter = autoResetEvent.One(TimeSpan.FromMilliseconds(50));
            autoResetEvent.Set();
            autoResetEvent.Reset();

            Assert.That(awaiter.Result, Is.True);
        }

        [Test]
        public void TimedOutAwaiterDoesNotConsumeTrigger()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var shortAwaiter = autoResetEvent.One(TimeSpan.FromMilliseconds(10));
            var longAwaiter = autoResetEvent.One();
            shortAwaiter.Wait();

            autoResetEvent.SetAndWait();

            Assert.That(shortAwaiter.Result, Is.False);
            Assert.That(longAwaiter.Result, Is.True);
        }

        [Test]
        public void CancelledAwaiterDoesNotConsumeTrigger()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var cancelToken = new CancellationTokenSource();
            var shortAwaiter = autoResetEvent.One(cancelToken.Token);
            var longAwaiter = autoResetEvent.One();
            cancelToken.Cancel();
            Assert.Catch<AggregateException>(() => shortAwaiter.Wait());

            autoResetEvent.SetAndWait();

            Assert.That(longAwaiter.Result, Is.True);
        }

        [Test]
        public void CancelledAwaiterIsCancelled()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var cancelToken = new CancellationTokenSource();
            var awaiter = autoResetEvent.One(cancelToken.Token);
            cancelToken.Cancel();

            Assert.Throws<TaskCanceledException>(async () => await awaiter);
            Assert.That(awaiter.IsCanceled, Is.True);
        }

        [Test]
        public void NewAwaiterGetsExistingSetNotification()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            autoResetEvent.Set();
            var awaiter = autoResetEvent.One(TimeSpan.FromMilliseconds(10));

            Assert.That(awaiter.Result, Is.True);
        }

        [Test]
        public void CancellableTimedOutAwaiterReturnsFalse()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var cancelToken = new CancellationTokenSource();
            var awaiter = autoResetEvent.One(TimeSpan.FromMilliseconds(10), cancelToken.Token);
            awaiter.Wait();

            Assert.That(awaiter.Result, Is.False);
        }

        [Test]
        public void CancelledTimeLimitedAwaiterIsCancelled()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var cancelToken = new CancellationTokenSource();
            var awaiter = autoResetEvent.One(TimeSpan.FromMilliseconds(500), cancelToken.Token);
            cancelToken.Cancel();

            Assert.Throws<TaskCanceledException>(async () => await awaiter);
            Assert.That(awaiter.IsCanceled, Is.True);
        }

        [Test]
        public void StressTest()
        {
            const int initialEventCount = 500;

            var autoResetEvent = new AwaitableAutoResetEvent();

            var waiters = Enumerable.Range(0, initialEventCount).Select(i => autoResetEvent.One()).ToArray();

            var sentEvents = 0;
            while (!Task.WhenAll(waiters).Wait(TimeSpan.Zero))
            {
                autoResetEvent.SetAndWait();
                sentEvents++;
            }

            Assert.AreEqual(initialEventCount, sentEvents);
            Assert.False(autoResetEvent.One(TimeSpan.Zero).Result);

        }
    }
}
