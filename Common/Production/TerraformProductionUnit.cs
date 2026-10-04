#region Copyright Notice
// ============================================================================
// Copyright (C) 2010 stars-nova
//
// This file is part of Stars-Nova.
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

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Xml;

    /// <summary>
    /// One completed terraform step, for message 123 (production-queue.md 10i): the axis
    /// (0 gravity, 1 temperature, 2 radiation), whether the value went up, and the new value.
    /// </summary>
    public sealed class TerraformStep
    {
        public TerraformStep(int axis, bool increased, int newValue)
        {
            Axis = axis;
            Increased = increased;
            NewValue = newValue;
        }

        public int Axis { get; }

        public bool Increased { get; }

        public int NewValue { get; }

        public string AxisName
        {
            get
            {
                switch (Axis)
                {
                    case TerraformReach.GravityAxis: return "Gravity";
                    case TerraformReach.TemperatureAxis: return "Temperature";
                    default: return "Radiation";
                }
            }
        }
    }

    /// <summary>
    /// "Constructs" one one-point terraform step (behavior-specs-10/production-queue.md sections
    /// 6 and 10k item 3). One class covers the three queue items:
    /// - a MANUAL order is "Terraform Environment" (type 12): its quantity is cut to the planet's
    ///   remaining headroom before purchase (message 303), see <see cref="IProductionUnit.RoomForManualOrder"/>;
    /// - an AUTO order is "Max Terraform" (type 5) by default, or "Min Terraform" (type 4) when
    ///   <see cref="MinimumOnly"/> is set: both buy min(N, headroom) steps per turn, and Min buys
    ///   nothing while the planet's value for its owner is above zero and its population is not
    ///   falling this year.
    /// The headroom is the total of one-point steps still available over the three axes, each
    /// axis's target being the owner's ideal but no further from the planet's ORIGINAL value than
    /// the owner's researched terraform reach (<see cref="TerraformReach"/>), held to 1..99;
    /// immune axes count zero. Each step moves the axis with the largest habitability gain per
    /// point over its whole remaining move (ties: gravity, temperature, radiation).
    /// </summary>
    public class TerraformProductionUnit : IProductionUnit
    {
        private Resources cost;
        private Resources remainingCost;

        /// <summary>Steps made since the last <see cref="TakeSteps"/> (not persisted).</summary>
        [NonSerialized]
        private List<TerraformStep> steps = new List<TerraformStep>();

        public Resources Cost
        {
            get { return cost; }
        }

        public Resources RemainingCost
        {
            get { return remainingCost; }
        }

        /// <summary>
        /// True for a "Min Terraform" auto entry (type 4); false for "Max Terraform" (type 5) and
        /// for the manual "Terraform Environment" item (type 12), which has no gate.
        /// </summary>
        public bool MinimumOnly { get; set; }

        /// <summary>
        /// The owner's terraform reach, stamped by the server's production step from the owner's
        /// buildable components each turn (Manufacture) and saved with the unit so a client-side
        /// estimate sees the last known figure. Null means unknown: the legacy flat allowance is
        /// used instead (<see cref="TerraformReach.Legacy"/>).
        /// </summary>
        public TerraformReach Reach { get; set; }

        public string Name
        {
            get { return MinimumOnly ? "Min Terraform" : "Terraform"; }
        }

        /// <summary>
        /// initializing constructor.
        /// </summary>
        /// <param name="race">Race performing the terraforming: 100 resources per 1% step, 70 with
        /// Total Terraforming, and halved for Claim Adjuster (behavior-specs-8/production-queue.md
        /// section 6 - the previous revision's PRT-keyed 70/110/120 figures were Mineral Packet
        /// kilotonnages).</param>
        public TerraformProductionUnit(Race race)
        {
            int resourceCost = race.HasTrait("TT")
                ? Global.TerraformResourceCostTotalTerraforming
                : Global.TerraformResourceCost;

            if (race.HasTrait("CA"))
            {
                // A one-bit right shift in the original.
                resourceCost >>= 1;
            }

            cost = new Resources(0, 0, 0, resourceCost);

            // A copy: Construct's partial payment mutates remainingCost in place, which must
            // never reach the unit's Cost.
            remainingCost = new Resources(cost);
        }

        /// <summary>A Min (true) or Max (false) Terraform unit - see <see cref="MinimumOnly"/>.</summary>
        public TerraformProductionUnit(Race race, bool minimumOnly) : this(race)
        {
            MinimumOnly = minimumOnly;
        }

        /// <summary>
        /// Load: Read in a ProductionUnit from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionUnit</param>
        public TerraformProductionUnit(XmlNode node)
        {
            XmlNode mainNode = node.FirstChild;
            while (mainNode != null)
            {
                switch (mainNode.Name.ToLowerInvariant())
                {
                    case "cost":
                        cost = new Resources(mainNode);
                        break;

                    case "remainingcost":
                        remainingCost = new Resources(mainNode);
                        break;

                    case "minimumonly":
                        MinimumOnly = mainNode.FirstChild != null && bool.Parse(mainNode.FirstChild.Value);
                        break;

                    case "reach":
                        Reach = new TerraformReach(mainNode);
                        break;
                }

                mainNode = mainNode.NextSibling;
            }
        }

        // ---------------------------------------------------------------------------------
        // Headroom and step choice (production-queue.md 10k item 3).
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// The target of one axis: the race's ideal, but no further from the planet's original
        /// value than <paramref name="reach"/>, held to 1..99; -1 when the axis has none (immune,
        /// or no reach).
        /// </summary>
        public static int AxisTarget(Race race, int axis, int original, int reach)
        {
            EnvironmentTolerance tolerance = Tolerance(race, axis);
            if (tolerance == null || tolerance.Immune || reach <= 0)
            {
                return -1;
            }

            int target = Math.Max(original - reach, Math.Min(original + reach, tolerance.OptimumLevel));
            return Math.Max(1, Math.Min(99, target));
        }

        /// <summary>
        /// The one-point steps still available on one axis: from the current value to the
        /// target, counted only when that move is towards the race's ideal (a planet already
        /// pushed past its reach by something else is never terraformed away from its ideal).
        /// </summary>
        public static int AxisRoom(Star star, Race race, int axis, TerraformReach reach)
        {
            int target = AxisTarget(race, axis, OriginalValue(star, axis), reach[axis]);
            if (target < 0)
            {
                return 0;
            }

            int current = CurrentValue(star, axis);
            int ideal = Tolerance(race, axis).OptimumLevel;
            if (current == target || Math.Sign(target - current) != Math.Sign(ideal - current))
            {
                return 0;
            }

            return Math.Abs(target - current);
        }

        /// <summary>The planet's remaining terraform headroom H, the total over the three axes
        /// (FUN_1048_537e).</summary>
        public static int Headroom(Star star, Race race, TerraformReach reach)
        {
            if (star == null || race == null || reach == null)
            {
                return 0;
            }

            int total = 0;
            for (int axis = 0; axis < TerraformReach.AxisCount; axis++)
            {
                total += AxisRoom(star, race, axis, reach);
            }

            return total;
        }

        /// <summary>
        /// Production-queue.md row 37: the "Terraform Environment" catalog item is offered only
        /// on a planet with headroom for its owner.
        /// </summary>
        public static bool CatalogOffersTerraformEnvironment(Star star, EmpireData empire)
        {
            if (star == null || empire == null)
            {
                return false;
            }

            return Headroom(star, empire.Race, TerraformReach.For(empire)) > 0;
        }

        /// <summary>
        /// The step chooser (FUN_1048_3eee): for each axis with room, the planet's value with
        /// that axis moved all the way to its target; the score is the value gained per point of
        /// that move, times 100, plus 1 (an axis with no room scores 0). The highest score wins,
        /// ties to the earlier axis. Returns the axis index, or -1 when nothing can move.
        /// </summary>
        public static int ChooseAxis(Star star, Race race, TerraformReach reach)
        {
            int baseline = race.HabPercent(star);
            int best = -1;
            long bestScore = 0;

            for (int axis = 0; axis < TerraformReach.AxisCount; axis++)
            {
                int room = AxisRoom(star, race, axis, reach);
                if (room <= 0)
                {
                    continue;
                }

                int target = AxisTarget(race, axis, OriginalValue(star, axis), reach[axis]);
                Star projected = EnvironmentCopy(star);
                SetCurrentValue(projected, axis, target);
                long gain = race.HabPercent(projected) - baseline;
                long score = (gain * 100 / room) + 1;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = axis;
                }
            }

            return best;
        }

        /// <summary>
        /// Moves the chosen axis one point towards its target (held to 1..99). Returns the step
        /// made, or null when no axis can move.
        /// </summary>
        public static TerraformStep Step(Star star, Race race, TerraformReach reach)
        {
            int axis = ChooseAxis(star, race, reach);
            if (axis < 0)
            {
                return null;
            }

            int current = CurrentValue(star, axis);
            int target = AxisTarget(race, axis, OriginalValue(star, axis), reach[axis]);
            int next = Math.Max(1, Math.Min(99, current + Math.Sign(target - current)));
            SetCurrentValue(star, axis, next);
            return new TerraformStep(axis, next > current, next);
        }

        /// <summary>The reach this unit uses: the stamped one, or the legacy flat allowance.</summary>
        public TerraformReach EffectiveReach(Race race)
        {
            return Reach ?? TerraformReach.Legacy(race);
        }

        /// <summary>The steps made since the last call, oldest first (message 123, one per step).</summary>
        public List<TerraformStep> TakeSteps()
        {
            List<TerraformStep> taken = steps ?? new List<TerraformStep>();
            steps = new List<TerraformStep>();
            return taken;
        }

        private static EnvironmentTolerance Tolerance(Race race, int axis)
        {
            if (race == null)
            {
                return null;
            }

            switch (axis)
            {
                case TerraformReach.GravityAxis: return race.GravityTolerance;
                case TerraformReach.TemperatureAxis: return race.TemperatureTolerance;
                default: return race.RadiationTolerance;
            }
        }

        private static int CurrentValue(Star star, int axis)
        {
            switch (axis)
            {
                case TerraformReach.GravityAxis: return star.Gravity;
                case TerraformReach.TemperatureAxis: return star.Temperature;
                default: return star.Radiation;
            }
        }

        private static int OriginalValue(Star star, int axis)
        {
            switch (axis)
            {
                case TerraformReach.GravityAxis: return star.OriginalGravity;
                case TerraformReach.TemperatureAxis: return star.OriginalTemperature;
                default: return star.OriginalRadiation;
            }
        }

        private static void SetCurrentValue(Star star, int axis, int value)
        {
            switch (axis)
            {
                case TerraformReach.GravityAxis: star.Gravity = value; break;
                case TerraformReach.TemperatureAxis: star.Temperature = value; break;
                default: star.Radiation = value; break;
            }
        }

        /// <summary>A throwaway star carrying only the environment, for HabPercent.</summary>
        private static Star EnvironmentCopy(Star star)
        {
            return new Star
            {
                Gravity = star.Gravity,
                Temperature = star.Temperature,
                Radiation = star.Radiation,
                OriginalGravity = star.OriginalGravity,
                OriginalTemperature = star.OriginalTemperature,
                OriginalRadiation = star.OriginalRadiation,
            };
        }

        // ---------------------------------------------------------------------------------
        // Legacy helpers, kept for Claim Adjuster's free terraforming (StarUpdateStep) and the
        // AI's own headroom estimate (Nova.Ai), which have no access to the owner's components.
        // ---------------------------------------------------------------------------------

        /// <summary>The legacy flat terraform allowance: 15, or 30 with Total Terraforming.</summary>
        public static int MaxTerraformPercent(Race race)
        {
            return race.HasTrait("TT") ? 30 : 15;
        }

        /// <summary>
        /// Legacy worst-axis-first selection with the flat allowance (still used by Claim
        /// Adjuster's free terraforming and the AI); the paid queue item uses
        /// <see cref="ChooseAxis"/> instead.
        /// </summary>
        public static string SelectAxisToImprove(Star star, Race race)
        {
            int maxPercent = MaxTerraformPercent(race);

            string best = null;
            int bestDistance = 0;

            CheckAxis("Gravity", star.Gravity, star.OriginalGravity, race.GravityTolerance.OptimumLevel, maxPercent, ref best, ref bestDistance);
            CheckAxis("Temperature", star.Temperature, star.OriginalTemperature, race.TemperatureTolerance.OptimumLevel, maxPercent, ref best, ref bestDistance);
            CheckAxis("Radiation", star.Radiation, star.OriginalRadiation, race.RadiationTolerance.OptimumLevel, maxPercent, ref best, ref bestDistance);

            return best;
        }

        private static void CheckAxis(string axisName, int current, int original, int ideal, int maxPercent, ref string best, ref int bestDistance)
        {
            int distanceToIdeal = Math.Abs(current - ideal);
            int alreadyUsed = Math.Abs(current - original);

            if (distanceToIdeal > 0 && alreadyUsed < maxPercent && distanceToIdeal > bestDistance)
            {
                best = axisName;
                bestDistance = distanceToIdeal;
            }
        }

        /// <summary>One-step-toward-ideal nudge used by Claim Adjuster's free terraforming and
        /// its random-axis planet drift (StarUpdateStep).</summary>
        public static void ImproveAxis(Star star, Race race, string axis)
        {
            switch (axis)
            {
                case "Gravity":
                    star.Gravity += Math.Sign(race.GravityTolerance.OptimumLevel - star.Gravity);
                    break;
                case "Temperature":
                    star.Temperature += Math.Sign(race.TemperatureTolerance.OptimumLevel - star.Temperature);
                    break;
                case "Radiation":
                    star.Radiation += Math.Sign(race.RadiationTolerance.OptimumLevel - star.Radiation);
                    break;
            }
        }

        // ---------------------------------------------------------------------------------
        // IProductionUnit
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Returns true if this production item is to be skipped this year: no resources, or no
        /// headroom left (status 2: nothing spent, the entry is kept).
        /// </summary>
        public bool IsSkipped(Star star)
        {
            if (star.ResourcesOnHand.Energy <= 0)
            {
                return true;
            }

            return Headroom(star, star.ThisRace, EffectiveReach(star.ThisRace)) <= 0;
        }

        /// <summary>
        /// Zero: with <see cref="SupportableCount"/> as the room this makes an auto Min/Max
        /// Terraform entry buy min(N, room) steps per turn through the same clamp the other
        /// standing auto-build orders use, and lets the estimator show it Skipped at room 0.
        /// </summary>
        public int? CurrentCount(Star star)
        {
            return 0;
        }

        /// <summary>
        /// The auto entry's room this turn: the headroom, or 0 for Min Terraform while the
        /// planet's value for its owner is above zero and the growth routine predicts no fall in
        /// population this year (production-queue.md 10k item 3; tested at purchase time only).
        /// </summary>
        public int? SupportableCount(Star star)
        {
            Race race = star.ThisRace;
            if (race == null)
            {
                return 0;
            }

            if (MinimumOnly && race.HabPercent(star) > 0 && star.CalculateGrowth(race) >= 0)
            {
                return 0;
            }

            return Headroom(star, race, EffectiveReach(race));
        }

        /// <summary>A manual Terraform Environment order is cut to the remaining headroom
        /// (message 303), and deleted when there is none.</summary>
        public int? RoomForManualOrder(Star star)
        {
            return Headroom(star, star.ThisRace, EffectiveReach(star.ThisRace));
        }

        /// <summary>
        /// Construct one terraform step on the axis the step chooser picks.
        /// </summary>
        public bool Construct(Star star)
        {
            if (star.ResourcesOnHand.Energy < remainingCost.Energy)
            {
                remainingCost.Energy -= star.ResourcesOnHand.Energy;
                star.ResourcesOnHand.Energy = 0;
                return false;
            }
            else
            {
                star.ResourcesOnHand.Energy -= remainingCost.Energy;
                if (star.ThisRace != null)
                {
                    TerraformStep step = Step(star, star.ThisRace, EffectiveReach(star.ThisRace));
                    if (step != null)
                    {
                        if (steps == null)
                        {
                            steps = new List<TerraformStep>();
                        }

                        steps.Add(step);
                    }
                }

                remainingCost = new Resources(cost);
                return true;
            }
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelUnit = xmldoc.CreateElement("TerraformUnit");

            xmlelUnit.AppendChild(cost.ToXml(xmldoc, "Cost"));

            xmlelUnit.AppendChild(remainingCost.ToXml(xmldoc, "RemainingCost"));

            if (MinimumOnly)
            {
                Global.SaveData(xmldoc, xmlelUnit, "MinimumOnly", MinimumOnly.ToString(CultureInfo.InvariantCulture));
            }

            if (Reach != null)
            {
                xmlelUnit.AppendChild(Reach.ToXml(xmldoc, "Reach"));
            }

            return xmlelUnit;
        }
    }
}
