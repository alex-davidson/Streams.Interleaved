using System;
using log4net.Appender;
using NUnit.Framework;
using Streams.Interleaved.UnitTests.TestEnvironment;

[assembly: LogToNUnit]

namespace Streams.Interleaved.UnitTests.TestEnvironment
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class LogToNUnit : TestActionAttribute
    {
        public override void BeforeTest(TestDetails testDetails)
        {
            log4net.Config.BasicConfigurator.Configure(new DebugAppender());
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