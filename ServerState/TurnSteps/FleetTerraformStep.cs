#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Fleet terraforming and hostile un-terraforming of the planet a fleet orbits (Orbital
    /// Adjuster): turn-generation step 27 (FUN_10b8_2e04), after repair (25) and Claim Adjuster
    /// drift (26) - behavior-specs-10/turn-generation-engine.md section 1 and section 11 "Fleet
    /// terraforming"; diplomacy-relations.md section 3 (the Friend read is the fleet owner's
    /// opinion of the planet owner).
    /// </summary>
    /// <remarks>
    /// Spec rules implemented exactly: every (non-starbase) fleet orbiting an OWNED planet whose
    /// terraforming capacity is positive acts on it once a year. If the planet's owner is the
    /// fleet's owner, or the fleet's owner rates the planet's owner Friend, the fleet improves the
    /// planet toward the FLEET OWNER's habitability; otherwise, if the planet has no starbase, it
    /// degrades the planet. One environment point moves per unit of capacity, every value
    /// clamped to 1..99. Messages 300/301 (improved / cannot improve further) and 346/347
    /// (degraded / cannot degrade further) go to the fleet's owner and, when a value changed, to
    /// the planet's owner.
    /// Capacity (FUN_1080_1a34, not read out by the spec): the fleet's summed "Orbital Adjuster"
    /// property - 1 per Orbital Adjuster per ship (components.xml: "terraforming ... by 1% per
    /// year").
    /// Interpretations (the spec does not give FUN_10b8_52d2's step choice in full):
    /// - Improving: each point goes to the axis furthest from the fleet owner's ideal that still
    ///   has terraforming allowance left (the same worst-axis-first rule and 15% / 30% Total
    ///   Terraforming allowance from the planet's original value that paid terraforming uses,
    ///   TerraformProductionUnit), skipping axes the fleet owner is immune on.
    /// - Degrading: each point moves the axis nearest the PLANET OWNER's ideal one point away
    ///   from it (skipping axes the planet owner is immune on), within 1..99 and within the fleet
    ///   owner's allowance from the original value (moving back toward the original value is
    ///   always allowed - un-terraforming).
    /// </remarks>
    public class FleetTerraformStep : ITurnStep
    {
        public const string MessageType = "Terraform";

        private static readonly string[] Axes = { "Gravity", "Temperature", "Radiation" };

        public void Process(ServerData serverState)
        {
            foreach (Fleet fleet in serverState.IterateAllFleets().ToList())
            {
                if (fleet.IsStarbase || fleet.InOrbit == null)
                {
                    continue;
                }

                int capacity = TerraformCapacity(fleet);
                if (capacity <= 0)
                {
                    continue;
                }

                if (!serverState.AllStars.TryGetValue(fleet.InOrbit.Name, out Star star) || star.Owner == Global.Nobody)
                {
                    continue;
                }

                if (!serverState.AllEmpires.TryGetValue(fleet.Owner, out EmpireData fleetOwner) || fleetOwner.Race == null)
                {
                    continue;
                }

                if (Improves(fleetOwner, star.Owner))
                {
                    int moved = Improve(star, fleetOwner.Race, capacity);
                    SendMessages(serverState, fleet, star, moved, true);
                }
                else if (star.Starbase == null)
                {
                    Race planetRace = serverState.AllEmpires.TryGetValue(star.Owner, out EmpireData planetOwner) ? planetOwner.Race : null;
                    int moved = Degrade(star, planetRace, fleetOwner.Race, capacity);
                    SendMessages(serverState, fleet, star, moved, false);
                }
            }
        }

        /// <summary>True when the fleet owner improves (rather than degrades) a planet of
        /// <paramref name="planetOwner"/>: its own, or one whose owner it rates Friend.</summary>
        public static bool Improves(EmpireData fleetOwner, int planetOwner)
        {
            if (fleetOwner.Id == planetOwner)
            {
                return true;
            }

            return fleetOwner.EmpireReports.TryGetValue((ushort)planetOwner, out EmpireIntel intel) && intel.Relation == PlayerRelation.Friend;
        }

        /// <summary>The fleet's terraforming capacity: summed Orbital Adjuster units over its ships.</summary>
        public static int TerraformCapacity(Fleet fleet)
        {
            int capacity = 0;
            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (token.Quantity <= 0 || token.Design?.Summary?.Properties == null)
                {
                    continue;
                }

                if (token.Design.Summary.Properties.TryGetValue("Orbital Adjuster", out ComponentProperty property) && property is IntegerProperty adjuster)
                {
                    capacity += adjuster.Value * token.Quantity;
                }
            }

            return capacity;
        }

        /// <summary>Moves up to <paramref name="points"/> points toward <paramref name="race"/>'s
        /// ideal (see the class remarks); returns the points actually moved.</summary>
        public static int Improve(Star star, Race race, int points)
        {
            int maxPercent = TerraformProductionUnit.MaxTerraformPercent(race);
            int moved = 0;

            for (int p = 0; p < points; p++)
            {
                int best = -1;
                int bestDistance = 0;
                for (int axis = 0; axis < Axes.Length; axis++)
                {
                    if (race.IsImmune(axis))
                    {
                        continue;
                    }

                    int current = Current(star, axis);
                    int ideal = race.CenterHab(axis);
                    int next = current + Math.Sign(ideal - current);
                    int distance = Math.Abs(ideal - current);
                    if (distance > 0 && next >= 1 && next <= 99 && WithinAllowance(next, Original(star, axis), current, maxPercent) && distance > bestDistance)
                    {
                        best = axis;
                        bestDistance = distance;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                int value = Current(star, best);
                SetCurrent(star, best, value + Math.Sign(race.CenterHab(best) - value));
                moved++;
            }

            return moved;
        }

        /// <summary>Moves up to <paramref name="points"/> points away from
        /// <paramref name="planetRace"/>'s ideal (see the class remarks); returns the points
        /// actually moved. <paramref name="fleetRace"/> sets the allowance.</summary>
        public static int Degrade(Star star, Race planetRace, Race fleetRace, int points)
        {
            int maxPercent = TerraformProductionUnit.MaxTerraformPercent(fleetRace);
            int moved = 0;

            for (int p = 0; p < points; p++)
            {
                int best = -1;
                int bestNext = 0;
                int bestDistance = int.MaxValue;
                for (int axis = 0; axis < Axes.Length; axis++)
                {
                    if (planetRace != null && planetRace.IsImmune(axis))
                    {
                        continue;
                    }

                    int current = Current(star, axis);
                    int original = Original(star, axis);
                    int ideal = planetRace != null ? planetRace.CenterHab(axis) : 50;

                    // Away from the ideal; at the ideal itself, toward the original value (or
                    // upward when there is none to return to).
                    int direction = current != ideal ? Math.Sign(current - ideal) : (original != current ? Math.Sign(original - current) : 1);
                    int next = current + direction;
                    if (next < 1 || next > 99)
                    {
                        next = current - direction;
                        if (current != ideal || next < 1 || next > 99)
                        {
                            continue;
                        }
                    }

                    if (!WithinAllowance(next, original, current, maxPercent))
                    {
                        continue;
                    }

                    int distance = Math.Abs(ideal - current);
                    if (distance < bestDistance)
                    {
                        best = axis;
                        bestNext = next;
                        bestDistance = distance;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                SetCurrent(star, best, bestNext);
                moved++;
            }

            return moved;
        }

        /// <summary>A step to <paramref name="next"/> is allowed when it stays within the
        /// allowance from the original value, or brings the value back toward the original.</summary>
        private static bool WithinAllowance(int next, int original, int current, int maxPercent)
        {
            int after = Math.Abs(next - original);
            return after <= maxPercent || after < Math.Abs(current - original);
        }

        private static int Current(Star star, int axis)
        {
            return axis == 0 ? star.Gravity : (axis == 1 ? star.Temperature : star.Radiation);
        }

        private static int Original(Star star, int axis)
        {
            return axis == 0 ? star.OriginalGravity : (axis == 1 ? star.OriginalTemperature : star.OriginalRadiation);
        }

        private static void SetCurrent(Star star, int axis, int value)
        {
            value = Math.Max(1, Math.Min(99, value));
            switch (axis)
            {
                case 0:
                    star.Gravity = value;
                    break;
                case 1:
                    star.Temperature = value;
                    break;
                default:
                    star.Radiation = value;
                    break;
            }
        }

        private static void SendMessages(ServerData serverState, Fleet fleet, Star star, int moved, bool improving)
        {
            Message toFleetOwner = new Message();
            toFleetOwner.Audience = fleet.Owner;
            toFleetOwner.Type = MessageType;
            if (improving)
            {
                toFleetOwner.Text = moved > 0
                    ? fleet.Name + " has terraformed " + star.Name + " by " + moved + "%." // 300
                    : fleet.Name + " cannot terraform " + star.Name + " any further."; // 301
            }
            else
            {
                toFleetOwner.Text = moved > 0
                    ? fleet.Name + " has degraded the environment of " + star.Name + " by " + moved + "%." // 346
                    : fleet.Name + " cannot degrade the environment of " + star.Name + " any further."; // 347
            }

            serverState.AllMessages.Add(toFleetOwner);

            if (moved > 0 && star.Owner != fleet.Owner)
            {
                Message toPlanetOwner = new Message();
                toPlanetOwner.Audience = star.Owner;
                toPlanetOwner.Type = MessageType;
                toPlanetOwner.Text = improving
                    ? "A fleet in orbit of " + star.Name + " has terraformed it by " + moved + "%."
                    : "A fleet in orbit of " + star.Name + " has degraded its environment by " + moved + "%.";
                serverState.AllMessages.Add(toPlanetOwner);
            }
        }
    }
}
