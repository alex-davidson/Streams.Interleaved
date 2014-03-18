using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Streams.Interleaved.UnitTests.TestEnvironment;
using Streams.Interleaved.Util;

[assembly: UnobservedTaskExceptionsAreFatal]

namespace Streams.Interleaved.UnitTests.TestEnvironment
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class UnobservedTaskExceptionsAreFatal : TestActionAttribute
    {
        public override void BeforeTest(TestDetails testDetails)
        {
            // This seems to be the only way to reliably break things when an unobserved task exception occurs.
            // The ThrowUnobservedTaskExceptions configuration option does not appear to do anything under NUnit.
            TaskScheduler.UnobservedTaskException += (s, e) => { throw new StackOverflowException(); };
        }

        public override ActionTargets Targets
        {
            get
            {
                return ActionTargets.Suite;
            }
        }
    }
}
