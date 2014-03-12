using System;
using NUnit.Framework;
using Streams.Interleaved.UnitTests.TestEnvironment;
using Streams.Interleaved.Util;

[assembly: TestForRaceConditions]

namespace Streams.Interleaved.UnitTests.TestEnvironment
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class TestForRaceConditions : TestActionAttribute
    {
        public override void BeforeTest(TestDetails testDetails)
        {
            RaceCondition.EnableJitterChecks();
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
