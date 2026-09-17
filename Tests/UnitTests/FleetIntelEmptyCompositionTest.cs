namespace Nova.Tests.UnitTests
{
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;

    /// <summary>
    /// Covers the second layer of defense for the crash described in
    /// SplitMergeTaskMissingTargetTest's own comment: even though that fix stops the specific
    /// path that used to fabricate an empty-Composition fleet, FleetIntel.ToXml/its XmlNode
    /// constructor should tolerate one regardless (belt-and-braces - a zero-ship fleet can
    /// legitimately, if briefly, exist for other reasons, e.g. the one-turn window before
    /// ServerData.CleanupFleets next runs). Fleet.Icon already returns null rather than throwing
    /// for an empty Composition; ToXml used to do an unguarded Icon.Source (NullReferenceException
    /// on save), and a naive fix of writing an empty Icon string would have just moved the crash
    /// to load instead (GetIconBySource can't parse an empty string, and the load path's
    /// enclosing try/catch treats that as fatal via Report.FatalError/Environment.Exit).
    /// </summary>
    [TestFixture]
    public class FleetIntelEmptyCompositionTest
    {
        [Test]
        public void ToXml_EmptyComposition_DoesNotThrowAndOmitsIcon()
        {
            FleetIntel report = new FleetIntel();
            report.Name = "New Fleet #1";
            report.Owner = 1;

            XmlDocument xmldoc = new XmlDocument();
            XmlElement xmlelement = null;

            Assert.DoesNotThrow(() => xmlelement = report.ToXml(xmldoc), "A report for a fleet with no ships (null Icon) must not throw while saving.");

            XmlNode iconNode = xmlelement.SelectSingleNode("Icon");
            Assert.That(iconNode, Is.Not.Null, "The Icon element itself should still be written (SaveData always creates the tag).");
            Assert.That(iconNode.FirstChild?.Value ?? string.Empty, Is.Empty, "With no ships there's no icon to save, so it should be empty rather than a bogus value.");
        }

        [Test]
        public void RoundTrip_EmptyIconElement_LoadsWithoutThrowingOrExitingTheProcess()
        {
            FleetIntel original = new FleetIntel();
            original.Name = "New Fleet #1";
            original.Owner = 1;

            XmlDocument xmldoc = new XmlDocument();
            XmlElement xmlelement = original.ToXml(xmldoc);
            xmldoc.AppendChild(xmlelement);

            FleetIntel reloaded = null;
            Assert.DoesNotThrow(() => reloaded = new FleetIntel(xmlelement), "Loading a report saved with an empty Icon element must not throw (or exit the process via Report.FatalError).");
            Assert.That(reloaded.Icon, Is.Null, "No icon can be resolved for a fleet with no ships - it should stay null, not a bogus lookup result.");
        }
    }
}
