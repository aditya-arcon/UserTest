// UserTest/Diagnostics/QueryMetricsTests.cs
using IDV_Backend.Diagnostics;
using NUnit.Framework;

namespace UserTest.Diagnostics
{
    public class QueryMetricsTests
    {
        [Test]
        public void Increment_And_Readback_Works()
        {
            var m = new QueryMetrics();
            Assert.That(m.GetCount("x"), Is.EqualTo(0));

            m.Increment("x");
            m.Increment("x");
            m.Increment("y");

            Assert.That(m.GetCount("x"), Is.EqualTo(2));
            Assert.That(m.GetCount("y"), Is.EqualTo(1));
            Assert.That(m.Snapshot().Count, Is.EqualTo(2));
        }
    }
}
