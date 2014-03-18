using System;

namespace Streams.Interleaved.Internals
{
    public class LivenessIndicatorSource 
    {
        public LivenessIndicatorSource()
        {
            lifecycleObject = new object();
            indicator = new LivenessIndicator(lifecycleObject);
        }

        private readonly LivenessIndicator indicator;
        private readonly object lifecycleObject;

        public StrongReference TakeStrongReference()
        {
            return new StrongReference(lifecycleObject, indicator);
        }

        public LivenessIndicator TakeWeakReference()
        {
            return indicator;
        }

        public struct StrongReference
        {
            public LivenessIndicator Indicator { get; private set; }
            private object reference;

            public StrongReference(object reference, LivenessIndicator indicator) : this()
            {
                Indicator = indicator;
                this.reference = reference;
            }

            public void Release()
            {
                GC.KeepAlive(reference);
                reference = null;
            }
        }
    }
}