namespace Nova.Tests.UnitTests
{
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Covers a real, reported bug: TurnGenerator built several of its own messages (Stargate
    /// jump/arrival/overgate loss, Wormhole transit, Warp 10 destruction, Cheap Engines failure)
    /// with Event set to the TurnGenerator instance itself (a leftover placeholder, not a
    /// domain object), and CheckForMinefields' own minefield-hit message set Event to the literal
    /// string "Minefield" while never setting Type at all. Message.ToXml()'s switch is keyed on
    /// Type, so every one of these fell into its default case, calling Report.Error(...) - "Nova
    /// has encountered an error, but will continue anyway" - on every single save after any of
    /// these ordinary, frequent events. Asserts ToXml() no longer reports an error for any of
    /// them, and that a real Minefield reference now round-trips correctly (the one message type
    /// here that previously had a working case, just fed a string instead of an object).
    /// </summary>
    [TestFixture]
    public class MessageEventSerializationTest
    {
        private string? lastReportedError;

        [SetUp]
        public void SetUp()
        {
            lastReportedError = null;
            PlatformHooks.ShowError = message => lastReportedError = message;
        }

        [TearDown]
        public void TearDown()
        {
            PlatformHooks.ShowError = _ => { };
        }

        [TestCase("Stargate")]
        [TestCase("Wormhole")]
        [TestCase("Warp 10")]
        [TestCase("Cheap Engines")]
        public void ToXml_MessageWithNoEventObject_DoesNotReportAnError(string messageType)
        {
            Message message = new Message { Audience = 1, Text = "test", Type = messageType };

            message.ToXml(new XmlDocument());

            Assert.That(lastReportedError, Is.Null);
        }

        [Test]
        public void ToXml_MinefieldMessage_RoundTripsTheActualMinefieldKey()
        {
            Minefield minefield = new Minefield { Key = 0x2A };
            Message message = new Message { Audience = 1, Text = "hit a minefield", Type = "Minefield", Event = minefield };

            XmlElement element = message.ToXml(new XmlDocument());

            Assert.That(lastReportedError, Is.Null);
            XmlNode? eventNode = element.SelectSingleNode("Event");
            Assert.That(eventNode, Is.Not.Null);
            Assert.That(eventNode!.FirstChild!.Value, Is.EqualTo(minefield.Key.ToString()));
        }
    }
}
