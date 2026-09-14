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
    /// Unit tests for the AI's personality-dispatch code
    /// (docs/behavior-specs-3/ai-opponent-behavior.md section 1) - the pure trait-value mapping,
    /// and the CLI option that carries the code into the AI process. DefaultAi.DoMove's own
    /// Disabled/Passive/Standard branching is exercised via the AI CLI harness instead (see
    /// PROJECT-STATUS.md's mechanic-1-of-8 entry) rather than here, since it needs a fully
    /// initialized game save the same way mechanics 2/5's live verification did.
    /// </summary>
    [TestFixture]
    public class DefaultAiPersonalityTest
    {
        [Test]
        public void TraitValueForPersonality_IsOne_AtTheLowEndOfTheStandardRange()
        {
            Assert.AreEqual(1, DefaultAi.TraitValueForPersonality(DefaultAi.MinStandardPersonality));
        }

        [Test]
        public void TraitValueForPersonality_IsSix_AtTheHighEndOfTheStandardRange()
        {
            Assert.AreEqual(6, DefaultAi.TraitValueForPersonality(DefaultAi.MaxStandardPersonality));
        }

        [Test]
        public void TraitValueForPersonality_IncreasesMonotonically_AcrossTheStandardRange()
        {
            int previous = DefaultAi.TraitValueForPersonality(DefaultAi.MinStandardPersonality);
            for (int code = DefaultAi.MinStandardPersonality + 1; code <= DefaultAi.MaxStandardPersonality; code++)
            {
                int current = DefaultAi.TraitValueForPersonality(code);
                Assert.Greater(current, previous);
                previous = current;
            }
        }

        [Test]
        public void TraitValueForPersonality_ClampsBelowTheStandardRange()
        {
            Assert.AreEqual(
                DefaultAi.TraitValueForPersonality(DefaultAi.MinStandardPersonality),
                DefaultAi.TraitValueForPersonality(DefaultAi.PassivePersonality));
        }

        [Test]
        public void TraitValueForPersonality_ClampsAboveTheStandardRange()
        {
            Assert.AreEqual(
                DefaultAi.TraitValueForPersonality(DefaultAi.MaxStandardPersonality),
                DefaultAi.TraitValueForPersonality(DefaultAi.MaxStandardPersonality + 5));
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
