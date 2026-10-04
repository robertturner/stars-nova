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
    using Nova.Ai;
    using Nova.Common;

    using NUnit.Framework;

    /// <summary>
    /// Unit tests for the AI's personality dispatch (docs/behavior-specs-10/ai-opponent-behavior.md
    /// §1/§1a): how Nova's `-n` code maps onto the spec's categories, the per-category colonist
    /// loads of §12/§13, and the CLI option that carries the code into the AI process. (The
    /// retracted §4 threat assessment's "trait value" mapping went with the code it fed.)
    /// </summary>
    [TestFixture]
    public class DefaultAiPersonalityTest
    {
        [Test]
        public void ForPersonality_MapsTheDisabledAndPassiveCodesToCategoriesSixAndSeven()
        {
            Assert.AreEqual(AiCategory.NoDriver, AiCategory.ForPersonality(DefaultAi.DisabledPersonality));
            Assert.AreEqual(AiCategory.EconomyOnly, AiCategory.ForPersonality(DefaultAi.PassivePersonality));
        }

        [Test]
        public void ForPersonality_MapsTheStandardRangeOntoCategoriesZeroToFive()
        {
            for (int code = DefaultAi.MinStandardPersonality; code <= DefaultAi.MaxStandardPersonality; code++)
            {
                Assert.AreEqual(code - DefaultAi.MinStandardPersonality, AiCategory.ForPersonality(code));
            }

            Assert.AreEqual(AiCategory.Macinti, AiCategory.ForPersonality(DefaultAi.MaxStandardPersonality + 5), "clamped");
        }

        [Test]
        public void TheDefaultPersonalityPlaysAutomitrons()
        {
            Assert.AreEqual(AiCategory.Automitrons, AiCategory.ForPersonality(DefaultAi.StandardPersonality));
        }

        [Test]
        public void ColonistLoadUnits_FollowsTheSection13Table()
        {
            Assert.AreEqual(10, AiCategory.ColonistLoadUnits(AiCategory.Robotoids, 5000));
            Assert.AreEqual(25, AiCategory.ColonistLoadUnits(AiCategory.Turindrones, 5000));
            Assert.AreEqual(150, AiCategory.ColonistLoadUnits(AiCategory.Automitrons, 5000));
            Assert.AreEqual(25, AiCategory.ColonistLoadUnits(AiCategory.Rototills, 5000));
            Assert.AreEqual(250, AiCategory.ColonistLoadUnits(AiCategory.Cybertrons, 5000));
            Assert.AreEqual(25, AiCategory.ColonistLoadUnits(AiCategory.Macinti, 5000));
            Assert.AreEqual(12, AiCategory.ColonistLoadUnits(AiCategory.Macinti, 125), "min(25, population / 10)");
        }

        [Test]
        public void OnlyCategoriesZeroAndFive_ScrapATargetlessColonyFleet()
        {
            for (int category = 0; category <= 7; category++)
            {
                bool expected = category == AiCategory.Robotoids || category == AiCategory.Macinti;
                Assert.AreEqual(expected, AiCategory.ScrapsTargetlessColonyFleet(category), "category " + category);
            }
        }

        [Test]
        public void CommandArguments_RoundTripsTheAiPersonalityOption()
        {
            CommandArguments arguments = new CommandArguments();
            arguments.Add(CommandArguments.Option.AiPersonality, 5);

            Assert.IsTrue(arguments.Contains(CommandArguments.Option.AiPersonality));
            Assert.AreEqual("5", arguments[CommandArguments.Option.AiPersonality]);
        }

        [Test]
        public void CommandArguments_AiPersonalityOptionIsAbsentByDefault()
        {
            CommandArguments arguments = new CommandArguments();
            Assert.IsFalse(arguments.Contains(CommandArguments.Option.AiPersonality));
        }
    }
}
