#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Tests.UnitTests
{
    using System.IO;
    using System.Linq;
    using System.Xml;

    using Nova.Common;
    using Nova.Common.DataStructures;

    using NUnit.Framework;

    /// <summary>
    /// Covers a real save/load bug found while investigating whether minefields work end-to-end:
    /// a <see cref="Minefield"/> could be serialized (Intel.ToXml already writes AllMinefields)
    /// but never successfully deserialized. Two independent bugs, both in MineField.cs:
    ///
    /// 1. ToXml wrote the element as "Minefiled" (typo), but Intel.LoadFromXmlNode's load switch
    ///    checks for "minefield" (correct spelling) - the case never matched, so a saved minefield
    ///    was silently dropped on load rather than erroring loudly.
    /// 2. Minefield's own XML constructor called `base(node.SelectSingleNode("Item"))` instead of
    ///    `base(node)` (the pattern every other Mappable-derived class uses) - since the real
    ///    &lt;Item&gt; node is nested two levels down (Minefield/Mappable/Item, not a direct child
    ///    of Minefield), this always evaluated to null, which crashed the process outright via
    ///    Mappable's `Report.FatalError` -&gt; `Environment.Exit(1)` on the rare path that got far
    ///    enough to try (e.g. loading straight from a ServerData .sstate file, which doesn't
    ///    switch on the element name at all).
    ///
    /// This test would have failed on both counts before either fix.
    /// </summary>
    [TestFixture]
    public class MinefieldXmlRoundTripTest
    {
        [Test]
        public void Intel_RoundTripsAMinefield_ThroughSaveAndLoad()
        {
            Intel original = new Intel();

            Minefield minefield = new Minefield();
            minefield.Name = "Test Minefield";
            minefield.Owner = 1;
            minefield.Position = new NovaPoint(100, 200);
            minefield.NumberOfMines = 625;
            minefield.SafeSpeed = 5;

            original.AllMinefields.Add(minefield.Key, minefield);

            XmlDocument xmldoc = new XmlDocument();
            Global.InitializeXmlDocument(xmldoc);
            xmldoc.ChildNodes.Item(1).AppendChild(original.ToXml(xmldoc));

            using (MemoryStream stream = new MemoryStream())
            {
                xmldoc.Save(stream);
                stream.Position = 0;

                XmlDocument reloadDoc = new XmlDocument();
                reloadDoc.Load(stream);

                Intel reloaded = new Intel(reloadDoc);

                Assert.AreEqual(1, reloaded.AllMinefields.Count, "The minefield should have survived the round trip, not been silently dropped.");

                Minefield reloadedField = reloaded.AllMinefields.Values.First();
                Assert.AreEqual(minefield.Owner, reloadedField.Owner);
                Assert.AreEqual(minefield.Position.X, reloadedField.Position.X);
                Assert.AreEqual(minefield.Position.Y, reloadedField.Position.Y);
                Assert.AreEqual(625, reloadedField.NumberOfMines);
                Assert.AreEqual(5, reloadedField.SafeSpeed);
                Assert.AreEqual(25, reloadedField.Radius);
            }
        }
    }
}
