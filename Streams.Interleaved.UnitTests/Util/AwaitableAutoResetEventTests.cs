using System;
using System.Linq;
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

            autoResetEvent.Set();

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

            autoResetEvent.Set();
            autoResetEvent.Set();

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
            autoResetEvent.Set();

            Assert.That(awaiter.Result, Is.True);
        }

        [Test]
        public void ResetDoesNotDetachActiveAwaiters()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var awaiter = autoResetEvent.One();
            autoResetEvent.Reset();
            autoResetEvent.Set();

            Assert.That(awaiter.Result, Is.True);
        }

        [Test]
        public void TimedOutAwaiterDoesNotConsumeTrigger()
        {
            var autoResetEvent = new AwaitableAutoResetEvent();

            var shortAwaiter = autoResetEvent.One(TimeSpan.FromMilliseconds(10));
            var longAwaiter = autoResetEvent.One();
            shortAwaiter.Wait();

            autoResetEvent.Set();

            Assert.That(shortAwaiter.Result, Is.False);
            Assert.That(longAwaiter.Result, Is.True);
        }
    }
}
