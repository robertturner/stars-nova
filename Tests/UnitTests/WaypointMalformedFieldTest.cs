namespace Nova.Tests.UnitTests
{
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common.Waypoints;

    /// <summary>
    /// Covers a real, confirmed-live crash: an empty field inside a saved Waypoint (e.g.
    /// &lt;Destination/&gt; with no text child - `mainNode.FirstChild.Value` then throws
    /// NullReferenceException) used to call Report.FatalError, which calls Environment.Exit -
    /// killing the entire app on every single load of the affected save, forever, since nothing
    /// ever repaired the damaged field. Confirmed on a real device via nova-error.log showing the
    /// exact same exception logged on every load attempt from the point of first damage onward.
    /// Perform should just skip the bad field and keep going, matching every sibling XML loader
    /// in this codebase (see Waypoint.cs's own comment).
    /// </summary>
    [TestFixture]
    public class WaypointMalformedFieldTest
    {
        private static XmlNode BuildWaypointNode(string innerXml)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml($"<Waypoint>{innerXml}</Waypoint>");
            return doc.DocumentElement;
        }

        [Test]
        public void EmptyDestinationElement_DoesNotThrow_AndLeavesOtherFieldsIntact()
        {
            XmlNode node = BuildWaypointNode("<Destination/><WarpFactor>6</WarpFactor><NoTask />");

            Waypoint waypoint = null;
            Assert.DoesNotThrow(() => waypoint = new Waypoint(node), "An empty <Destination/> element must not crash the whole app.");

            Assert.That(waypoint.WarpFactor, Is.EqualTo(6), "Fields after the malformed one should still load normally.");
            Assert.That(waypoint.Task, Is.Not.Null, "Task must never be null, even if nothing in the XML set it.");
        }

        [Test]
        public void NoTaskShapedChildAtAll_StillDefaultsTaskToNoTask()
        {
            XmlNode node = BuildWaypointNode("<Destination>Nowhere</Destination><WarpFactor>2</WarpFactor>");

            Waypoint waypoint = new Waypoint(node);

            Assert.That(waypoint.Task, Is.Not.Null);
            Assert.That(waypoint.Task, Is.InstanceOf<NoTask>());
        }
    }
}
