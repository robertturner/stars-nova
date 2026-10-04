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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// The yearly random-event wrapper that runs at the end of the production hub
    /// (behavior-specs-9/turn-generation-engine.md §1 step 20 and §5a): the comet strike, the
    /// environment shift and the mineral deposit, in that order, each rolling on its own (all
    /// three may fire in the same year). The fourth event of the original wrapper, Mystery Trader
    /// creation, is <see cref="MysteryTraderStep.TryCreate"/>, called last. The whole wrapper is
    /// skipped when the "No Random Events" game option is set (§1b, bit 0x80).
    ///
    /// "Year" in every gate is the counter of turns generated so far - the original's displayed
    /// year minus 2400, here <c>TurnYear - Global.StartingYear</c> - read BEFORE the year
    /// increment (§5a "Year in every gate"); TurnGenerator runs this step in the production
    /// block, ahead of <c>TurnYear++</c>.
    ///
    /// Random draws, in order, so a test can script them (every draw is <c>Random.Next(int)</c>):
    /// <list type="bullet">
    /// <item>Comet: planet, chance (1 in 20), then (only if the event happens) size, the
    ///   message's axis-name shuffle (2 draws), the mineral permutation (2 draws), per hit
    ///   mineral concentration (+ huge bonus) and raw units, the 3 base raw draws, then per
    ///   changed axis the step (2 draws if huge) and the sign.</item>
    /// <item>Environment shift: planet, chance (1 in 20), then axis, step (plus re-draw on a 3),
    ///   sign.</item>
    /// <item>Mineral deposit: planet, chance (1 in 15 - s), then mineral, then (only when the
    ///   concentration is below 180) the 5-19 gain.</item>
    /// </list>
    /// The planet is drawn before any gate is tested and is never re-drawn (§5a "Target planet");
    /// the year gate and the population protection are tested after the chance roll.
    /// </summary>
    public class RandomEventsStep : ITurnStep
    {
        /// <summary>One population unit is 100 colonists; the protection test is 51+ units.</summary>
        private const int ProtectedColonists = 5100;

        /// <summary>The protection test applies only while the counter is below 20.</summary>
        private const int ProtectionYearLimit = 20;

        /// <summary>The comet and the deposit need counter 10 or more.</summary>
        private const int LateEventYearFloor = 10;

        private const int MinEnvironmentValue = 1;
        private const int MaxEnvironmentValue = 99;

        /// <summary>The comet caps a hit mineral's concentration at 200 (raw 0x3629).</summary>
        private const int CometConcentrationCap = 200;

        /// <summary>The deposit only adds to a concentration below 180.</summary>
        private const int DepositConcentrationLimit = 180;

        // Environment axes in storage order (§5a: the comet changes the first size + 1 of these).
        private static readonly string[] Axes = { "Gravity", "Temperature", "Radiation" };

        private static readonly string[] Minerals = { "Ironium", "Boranium", "Germanium" };

        // The injected test random, or null: each Process then takes the game's seeded
        // "RandomEvents" stream (ServerData.CreateRandom), so the events are repeatable.
        private readonly Random injectedRandom;
        private Random random;

        public RandomEventsStep() : this(null)
        {
        }

        /// <summary>Overload for deterministic testing - pass a Random whose Next(int) is
        /// scripted (see the class summary for the draw order).</summary>
        public RandomEventsStep(Random random)
        {
            this.injectedRandom = random;
            this.random = random;
        }

        /// <summary>
        /// The galaxy-size index s (0 Tiny .. 4 Huge) used by the deposit chance. The original
        /// stores it directly and derives the galaxy diameter as (s + 1) x 400 (§5a "How the
        /// size index is stored", new-game-setup.md §3). This port has no size index, only a free
        /// map width, so the index is recovered by inverting that formula on the map width and
        /// clamping to the original's 0-4 range: s = clamp(MapWidth / 400 - 1, 0, 4) (integer
        /// division, so a 400-799 ly map is Tiny, 800-1199 Small, ..., 2000+ Huge). The map
        /// height is ignored - the original's galaxies are square.
        /// </summary>
        public static int GalaxySizeIndex(int mapWidth)
        {
            return Math.Max(0, Math.Min(4, (mapWidth / 400) - 1));
        }

        public void Process(ServerData serverState)
        {
            random = injectedRandom ?? serverState.CreateRandom("RandomEvents");

            if (GameSettings.Data.NoRandomEvents)
            {
                return;
            }

            int counter = serverState.TurnYear - Global.StartingYear;
            int sizeIndex = GalaxySizeIndex(GameSettings.Data.MapWidth);

            // §5a: comet, environment shift, mineral deposit, then Mystery Trader creation
            // (behavior-specs-10 §5a; no draw at all below counter 40 or in an odd year).
            Comet(serverState, counter);
            EnvironmentShift(serverState, counter);
            MineralDeposit(serverState, counter, sizeIndex);
            MysteryTraderStep.TryCreate(serverState, random, counter);
        }

        /// <summary>
        /// Comet strike (§5a, FUN_10b8_33ee): 1 in 20 a year from counter 10 onward, with the
        /// shared population protection. Returns true when a comet struck.
        /// </summary>
        public bool Comet(ServerData serverState, int counter)
        {
            Star star = DrawPlanet(serverState);
            if (star == null)
            {
                return false;
            }

            if (random.Next(20) != 0 || counter < LateEventYearFloor || IsProtected(star, counter))
            {
                return false;
            }

            // Size class 0 small, 1 medium, 2 large, 3 huge, each 25%. "Huge" is read as size 3,
            // the only class the §5a table gives the larger concentration and axis steps.
            int size = random.Next(4);
            bool huge = size == 3;
            int count = Math.Min(size + 1, 3);

            // The axis names the owner's message quotes come from a separately shuffled list
            // (raw 0x3451-0x3496), so a small or medium comet can name an axis it did not change.
            int[] axisNameOrder = UniformPermutation();
            int[] mineralOrder = UniformPermutation();

            EmpireData owner = null;
            if (star.Owner != Global.Nobody)
            {
                serverState.AllEmpires.TryGetValue(star.Owner, out owner);
            }
            bool ownerIsAlternateReality = owner != null && owner.Race != null && owner.Race.HasTrait("AR");
            int killedPercent = 25 + (20 * size);
            string sizeName = new[] { "small", "medium-sized", "large", "huge" }[size];

            // Messages 131-138: every player is told. The owner (unless Alternate Reality) gets the
            // "struck your planet, killing N% of the colonists" tier; everyone else, and an AR
            // owner, gets the plain impact notice.
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                Message message = new Message();
                message.Audience = empire.Id;
                message.Type = "Comet";
                if (owner != null && empire.Id == owner.Id && !ownerIsAlternateReality)
                {
                    string axisNames = string.Join(", ", axisNameOrder.Take(count).Select(i => Axes[i]));
                    message.Text = "A " + sizeName + " comet has crashed into " + star.Name + ", killing "
                        + killedPercent.ToString(CultureInfo.InvariantCulture) + "% of the colonists there. "
                        + "The impact has altered the planet's " + axisNames
                        + ", enriched its mineral deposits and cancelled its production.";
                }
                else
                {
                    message.Text = "A " + sizeName + " comet has crashed into " + star.Name
                        + ", altering its environment and enriching its mineral deposits.";
                }
                serverState.AllMessages.Add(message);
            }

            // Colonist loss: 25 + 20 x size percent; none for an Alternate Reality owner.
            if (owner != null && !ownerIsAlternateReality)
            {
                long lost = (long)star.Colonists * killedPercent / 100;
                star.Colonists -= (int)lost;
            }

            // Minerals: size + 1 (max 3) hit minerals gain 50-99 concentration (huge: a further
            // 15-29), the result capped at 200, plus 3,000-19,999 raw units; then all three get a
            // 50-299 raw base, and each raw total / 16 (floored) is added to the surface stock in kT.
            int[] raw = new int[3];
            for (int i = 0; i < count; i++)
            {
                int mineral = mineralOrder[i];
                int gain = 50 + random.Next(50);
                if (huge)
                {
                    gain += 15 + random.Next(15);
                }

                // Literal reading of "has the result capped at 200": the stored value becomes
                // min(old + gain, 200), so a (rare) concentration already above 200 is lowered.
                SetConcentration(star, mineral, Math.Min(GetConcentration(star, mineral) + gain, CometConcentrationCap));
                raw[mineral] += 3000 + random.Next(17000);
            }

            for (int mineral = 0; mineral < 3; mineral++)
            {
                raw[mineral] += 50 + random.Next(250);
                AddSurface(star, mineral, raw[mineral] / 16);
            }

            // Environment: the first size + 1 axes in storage order (Gravity, Temperature,
            // Radiation) move 3-5 points (huge: the sum of two 3-5 draws, 6-10), the sign drawn
            // per axis, on both the current and original values, clamped 1..99.
            for (int axis = 0; axis < count; axis++)
            {
                int step = 3 + random.Next(3);
                if (huge)
                {
                    step += 3 + random.Next(3);
                }
                ShiftAxis(star, axis, random.Next(2) == 0 ? step : -step);
            }

            CleanupQueueAfterDisaster(star);
            return true;
        }

        /// <summary>
        /// Environment shift (§5a, FUN_10b8_37c8): 1 in 20 a year with no year floor, the shared
        /// population protection, one uniformly chosen axis. Returns true when the shift happened.
        /// </summary>
        public bool EnvironmentShift(ServerData serverState, int counter)
        {
            Star star = DrawPlanet(serverState);
            if (star == null)
            {
                return false;
            }

            if (random.Next(20) != 0 || IsProtected(star, counter))
            {
                return false;
            }

            int axis = random.Next(3);

            // 3 + random(3); a 3 is replaced by 6 + random(3): 4 or 5 (1/3 each), 6/7/8 (1/9 each).
            int step = 3 + random.Next(3);
            if (step == 3)
            {
                step = 6 + random.Next(3);
            }
            int signedStep = random.Next(2) == 0 ? step : -step;

            // Message 253 goes to the owner before the change is made; an unowned planet changes
            // silently.
            if (star.Owner != Global.Nobody)
            {
                Message message = new Message();
                message.Audience = star.Owner;
                message.Type = "Environment Shift";
                message.Text = "A shift in the environment of " + star.Name + " has changed its "
                    + Axes[axis] + ". The upheaval has cancelled all of the planet's production.";
                serverState.AllMessages.Add(message);
            }

            ShiftAxis(star, axis, signedStep);
            CleanupQueueAfterDisaster(star);
            return true;
        }

        /// <summary>
        /// Mineral deposit (§5a, FUN_10b8_38dc): 1 in (15 - s) a year from counter 10 onward, no
        /// protection test, no surface minerals and no queue cleanup. Returns true when the event
        /// fired (whether or not the concentration actually changed).
        /// </summary>
        public bool MineralDeposit(ServerData serverState, int counter, int sizeIndex)
        {
            Star star = DrawPlanet(serverState);
            if (star == null)
            {
                return false;
            }

            if (random.Next(15 - sizeIndex) != 0 || counter < LateEventYearFloor)
            {
                return false;
            }

            int mineral = random.Next(3);

            // Message 254 goes to the owner even when nothing changes.
            if (star.Owner != Global.Nobody)
            {
                Message message = new Message();
                message.Audience = star.Owner;
                message.Type = "Mineral Deposit";
                message.Text = "A new deposit of " + Minerals[mineral] + " has been discovered on " + star.Name + ".";
                serverState.AllMessages.Add(message);
            }

            int concentration = GetConcentration(star, mineral);
            if (concentration < DepositConcentrationLimit)
            {
                SetConcentration(star, mineral, concentration + 5 + random.Next(15));
            }

            return true;
        }

        /// <summary>
        /// The disaster queue cleanup (production-queue.md §8, FUN_10b8_371a), run only after the
        /// comet and the environment shift: every auto-build entry is kept in its original order
        /// and every other entry is deleted (with any progress invested in it).
        /// </summary>
        public static void CleanupQueueAfterDisaster(Star star)
        {
            if (star.ManufacturingQueue == null)
            {
                return;
            }

            // Ship and starbase orders go too, auto-build or not (ProductionQueue.CleanupAfterDisaster).
            star.ManufacturingQueue.CleanupAfterDisaster();
        }

        /// <summary>One uniform draw over every planet in the galaxy, owned or not (§5a).</summary>
        private Star DrawPlanet(ServerData serverState)
        {
            int planetCount = serverState.AllStars.Count;
            if (planetCount == 0)
            {
                return null;
            }

            return serverState.AllStars.Values.ElementAt(random.Next(planetCount));
        }

        /// <summary>The comet / environment-shift protection: owned, 51+ population units
        /// (5,100+ colonists) and counter below 20.</summary>
        private static bool IsProtected(Star star, int counter)
        {
            return star.Owner != Global.Nobody && star.Colonists >= ProtectedColonists && counter < ProtectionYearLimit;
        }

        /// <summary>A uniform permutation of {0, 1, 2} (Fisher-Yates, two draws).</summary>
        private int[] UniformPermutation()
        {
            int[] order = { 0, 1, 2 };
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }
            return order;
        }

        /// <summary>Applies a step to an axis's current and original value, each clamped 1..99.</summary>
        private static void ShiftAxis(Star star, int axis, int delta)
        {
            switch (axis)
            {
                case 0:
                    star.Gravity = ClampEnvironment(star.Gravity + delta);
                    star.OriginalGravity = ClampEnvironment(star.OriginalGravity + delta);
                    break;
                case 1:
                    star.Temperature = ClampEnvironment(star.Temperature + delta);
                    star.OriginalTemperature = ClampEnvironment(star.OriginalTemperature + delta);
                    break;
                default:
                    star.Radiation = ClampEnvironment(star.Radiation + delta);
                    star.OriginalRadiation = ClampEnvironment(star.OriginalRadiation + delta);
                    break;
            }
        }

        private static int ClampEnvironment(int value)
        {
            return Math.Max(MinEnvironmentValue, Math.Min(MaxEnvironmentValue, value));
        }

        private static int GetConcentration(Star star, int mineral)
        {
            switch (mineral)
            {
                case 0: return star.MineralConcentration.Ironium;
                case 1: return star.MineralConcentration.Boranium;
                default: return star.MineralConcentration.Germanium;
            }
        }

        private static void SetConcentration(Star star, int mineral, int value)
        {
            switch (mineral)
            {
                case 0: star.MineralConcentration.Ironium = value; break;
                case 1: star.MineralConcentration.Boranium = value; break;
                default: star.MineralConcentration.Germanium = value; break;
            }
        }

        private static void AddSurface(Star star, int mineral, int kilotons)
        {
            switch (mineral)
            {
                case 0: star.ResourcesOnHand.Ironium += kilotons; break;
                case 1: star.ResourcesOnHand.Boranium += kilotons; break;
                default: star.ResourcesOnHand.Germanium += kilotons; break;
            }
        }
    }
}
