#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010 stars-nova
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

namespace Nova.Server
{
    using System;
    using System.Collections.Generic;
    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// Class to provide score data: the per-race score record of
    /// docs/behavior-specs-9/victory-conditions.md section 2 ("Score record layout and formula")
    /// and client-ui-dialog-catalog.md's "Score display" entry (the nine-row table and the Score
    /// sum). The same record feeds both the Score display and victory conditions 1, 4-8.
    /// </summary>
    public class Scores
    {
        /// <summary>Design weapon rating at or above which a ship is a Capital ship (spec: 2,000,
        /// "threshold confirmed in raw bytes"). Ratings 1 to 1,999 are Escorts, 0 is Unarmed.</summary>
        public const int CapitalShipRating = 2000;

        /// <summary>Population points are capped at this many per planet.</summary>
        public const int MaxPopulationPointsPerPlanet = 6;

        private ServerData serverState;

        public Scores(ServerData serverState)
        {
            this.serverState = serverState;
        }

        /// <summary>
        /// Return a list of all scores, ranked.
        /// </summary>
        public List<ScoreRecord> GetScores()
        {
            List<ScoreRecord> scores = new List<ScoreRecord>();

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                scores.Add(GetScoreRecord(empire));
            }

            SetRanks(scores);

            return scores;
        }

        /// <summary>
        /// Build a <see cref="ScoreRecord"/> for a given empire (victory-conditions.md section 2;
        /// client-ui-dialog-catalog.md "Score display").
        /// </summary>
        private ScoreRecord GetScoreRecord(EmpireData empire)
        {
            int empireId = empire.Id;
            ScoreRecord score = new ScoreRecord();
            score.EmpireId = empireId;

            // ----------------------------------------------------------------------------
            // Planet-derived values: Planets, Starbases, Resources and the population points.
            // ----------------------------------------------------------------------------

            long populationPoints = 0;
            long resources = 0;

            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Owner != empireId)
                {
                    continue;
                }

                score.Planets++;

                // Resources is the summed resource OUTPUT of every owned planet, not the
                // leftover ResourcesOnHand after production spent it.
                resources += star.GetResourceRate();

                if (HasQualifyingStarbase(star))
                {
                    score.Starbases++;
                }

                populationPoints += PopulationPoints(star.Colonists);
            }

            score.Resources = (int)Math.Min(int.MaxValue, resources);

            // ----------------------------------------------------------------------------
            // Ship classes, by each design's weapon rating (0 / 1-1,999 / 2,000+). Counts are
            // SHIPS (token quantities), not tokens.
            // ----------------------------------------------------------------------------

            long unarmedShips = 0;
            long escortShips = 0;
            long capitalShips = 0;

            foreach (Fleet fleet in empire.OwnedFleets.Values)
            {
                if (fleet.Owner != empireId)
                {
                    continue;
                }

                // Starbases are scored through their planet (the Starbases row), not as ships:
                // in the original they are planet records, not entries in the fleet table the
                // ship tally walks.
                if (fleet.Composition.Count == 0 || IsStarbaseFleet(fleet))
                {
                    continue;
                }

                foreach (ShipToken token in fleet.Composition.Values)
                {
                    if (token.Design == null || token.Quantity <= 0)
                    {
                        continue;
                    }

                    int rating = DesignWeaponRating(token.Design);
                    if (rating <= 0)
                    {
                        unarmedShips += token.Quantity;
                    }
                    else if (rating < CapitalShipRating)
                    {
                        escortShips += token.Quantity;
                    }
                    else
                    {
                        capitalShips += token.Quantity;
                    }
                }
            }

            score.UnarmedShips = (int)Math.Min(int.MaxValue, unarmedShips);
            score.EscortShips = (int)Math.Min(int.MaxValue, escortShips);
            score.CapitalShips = (int)Math.Min(int.MaxValue, capitalShips);

            // ----------------------------------------------------------------------------
            // Tech Levels row: the plain sum of the six levels (not the weighted curve).
            // ----------------------------------------------------------------------------

            int techSum = 0;
            long techPoints = 0;
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                int level = empire.ResearchLevels[field];
                techSum += level;
                techPoints += TechLevelPoints(level);
            }
            score.TechLevel = techSum;

            // ----------------------------------------------------------------------------
            // The Score: the sum of the seven terms of client-ui-dialog-catalog.md's list.
            // ----------------------------------------------------------------------------

            long planets = score.Planets;
            long total = populationPoints;
            total += resources / 30;
            total += 3L * score.Starbases;

            // "skipped once the race is eliminated" - the elimination bit raised by
            // VictoryCheck (victory-conditions.md section 2, "Elimination flag").
            if (!empire.Eliminated)
            {
                total += techPoints;
            }

            total += Math.Min(unarmedShips, planets) / 2;
            total += 2 * Math.Min(escortShips, planets);
            if (capitalShips > 0)
            {
                total += (8 * planets * capitalShips) / (planets + capitalShips);
            }

            score.Score = (int)Math.Min(int.MaxValue, total);

            return score;
        }

        /// <summary>
        /// Population points for one planet: "its population in thousands rounded up, at most 6
        /// per planet". ASSUMPTION: the original stores population in units of 100 colonists, so
        /// "in thousands" of those units is ceil(colonists / 100,000) - the reading the spec-9
        /// coverage report's open question 5 also takes, since a literal ceil(colonists / 1,000)
        /// would hit the cap of 6 on any planet above 5,000 colonists.
        /// </summary>
        public static int PopulationPoints(int colonists)
        {
            if (colonists <= 0)
            {
                return 0;
            }

            int points = (int)Math.Ceiling(colonists / 100000.0);
            return Math.Min(MaxPopulationPointsPerPlanet, points);
        }

        /// <summary>
        /// The tech-curve points for one field at level L: L for 0-3, 2L - 3 for 4-6,
        /// 3(L - 3) for 7-9, 4L - 18 for 10 and up (client-ui-dialog-catalog.md, Score list).
        /// </summary>
        public static int TechLevelPoints(int level)
        {
            if (level <= 3)
            {
                return Math.Max(0, level);
            }
            if (level <= 6)
            {
                return (2 * level) - 3;
            }
            if (level <= 9)
            {
                return 3 * (level - 3);
            }
            return (4 * level) - 18;
        }

        /// <summary>
        /// Fleet.IsStarbase without its Report.Error side effect for a design with no hull.
        /// </summary>
        private static bool IsStarbaseFleet(Fleet fleet)
        {
            foreach (ShipToken token in fleet.Composition.Values)
            {
                ShipDesign design = token.Design;
                if (design != null && design.Blueprint != null
                    && design.Blueprint.Properties.TryGetValue("Hull", out ComponentProperty hullProperty)
                    && hullProperty is Hull hull && hull.IsStarbase)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A planet scores a starbase only when its starbase hull has a non-zero dock capacity
        /// (client-ui-dialog-catalog.md row 2: "An Orbital Fort, with no dock, does not count").
        /// </summary>
        private static bool HasQualifyingStarbase(Star star)
        {
            Fleet starbase = star.Starbase;
            if (starbase == null || starbase.Composition.Count == 0 || starbase.Owner != star.Owner)
            {
                return false;
            }

            foreach (ShipToken token in starbase.Composition.Values)
            {
                ShipDesign design = token.Design;
                if (design != null && design.Blueprint != null
                    && design.Blueprint.Properties.ContainsKey("Hull")
                    && design.DockCapacity > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The design's weapon value ("power rating") that buckets ships into Unarmed / Escort /
        /// Capital - docs/behavior-specs-9/ship-design-and-components.md section 9, "The
        /// ship-side weapon value". Summed over the design's occupied slots:
        /// <list type="bullet">
        /// <item>beams (category 0x0010): (range + 3) x damage x quantity / 4;</item>
        /// <item>torpedoes / capital missiles (0x0020): (range - 2) x damage x quantity / 2;</item>
        /// <item>bombs (0x0040): (population-kill rate in tenths of a percent + installations
        /// destroyed) x quantity x 2 (the "two damage fields", section 15c);</item>
        /// <item>the capacitors' compounding percentage multiplied onto the beam sum.</item>
        /// </list>
        /// Derived here from the design's own components rather than ShipDesign.PowerRating, which
        /// is a stub. Known gaps, both unread in the spec: (1) beams "carrying a per-component
        /// flag" are cut to a third, but the flag is not identified (sapper or hits-all-targets
        /// fire mode are both candidates), so no cut is applied; (2) the final per-design
        /// adjustment from an unread helper is not added. Each slot term uses integer division,
        /// as a per-slot accumulation in the original would.
        /// </summary>
        public static int DesignWeaponRating(ShipDesign design)
        {
            if (design == null || design.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull"))
            {
                return 0;
            }

            Hull hull = design.Blueprint.Properties["Hull"] as Hull;
            if (hull == null || hull.Modules == null)
            {
                return 0;
            }

            long beamSum = 0;
            long torpedoSum = 0;
            long bombSum = 0;
            double capacitorPercent = 0;

            foreach (HullModule module in hull.Modules)
            {
                Component component = module.AllocatedComponent;
                int quantity = module.ComponentCount;
                if (component == null || quantity <= 0)
                {
                    continue;
                }

                if (component.Properties.TryGetValue("Weapon", out ComponentProperty weaponProperty)
                    && weaponProperty is Weapon weapon)
                {
                    if (weapon.IsBeam)
                    {
                        beamSum += (long)(weapon.Range + 3) * weapon.Power * quantity / 4;
                    }
                    else if (weapon.IsMissile)
                    {
                        torpedoSum += (long)(weapon.Range - 2) * weapon.Power * quantity / 2;
                    }
                }

                if (component.Properties.TryGetValue("Bomb", out ComponentProperty bombProperty)
                    && bombProperty is Bomb bomb)
                {
                    // Nova stores the kill rate as a percentage (0.6 = 0.6%); the original field
                    // is in tenths of a percent (Lady Finger = 6).
                    long popKillTenths = (long)Math.Round(bomb.PopKill * 10.0);
                    bombSum += (popKillTenths + bomb.Installations) * quantity * 2;
                }

                if (component.Properties.TryGetValue("Capacitor", out ComponentProperty capacitorProperty)
                    && capacitorProperty is CapacitorProperty capacitor)
                {
                    // Capacitors compound: each one multiplies the beam figure again.
                    CapacitorProperty slotTotal = capacitor * quantity;
                    capacitorPercent = (((100 + capacitorPercent) * (100 + slotTotal.Value)) / 100) - 100;
                }
            }

            if (capacitorPercent > 0)
            {
                capacitorPercent = Math.Min(capacitorPercent, CapacitorProperty.Maximum);
                beamSum = (long)(beamSum * (100 + capacitorPercent) / 100);
            }

            long rating = beamSum + torpedoSum + bombSum;
            return (int)Math.Max(0, Math.Min(int.MaxValue, rating));
        }

        /// <summary>
        /// Set the rank for all races: "1 plus the number of races with a strictly higher
        /// score, so tied races share a rank" (client-ui-dialog-catalog.md row 9). A lone race is
        /// therefore always rank 1.
        /// </summary>
        private static void SetRanks(List<ScoreRecord> scores)
        {
            scores.Sort();

            foreach (ScoreRecord score in scores)
            {
                int higher = 0;
                foreach (ScoreRecord other in scores)
                {
                    if (other.Score > score.Score)
                    {
                        higher++;
                    }
                }
                score.Rank = higher + 1;
            }
        }
    }
}
