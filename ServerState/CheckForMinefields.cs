#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010 The Stars-Nova Project
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

namespace Nova.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Combat;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>The outcome of one fleet's yearly pass through minefields.</summary>
    public enum MinefieldHit
    {
        /// <summary>No mine was hit.</summary>
        None,

        /// <summary>A mine was hit: the fleet has been stopped (and possibly damaged) and survives.</summary>
        Hit,

        /// <summary>A mine was hit and every ship in the fleet was destroyed.</summary>
        Destroyed
    }

    /// <summary>
    /// The minefield routine for a moving fleet (the original's FUN_10b0_312a, called from the
    /// movement pass). behavior-specs-10/fleet-movement-scanning-cargo.md section 5, "Minefield
    /// rules, code-confirmed":
    /// - only fields owned by another race that does not rate the fleet's race "friend" count
    ///   (the text reads "owned by another race", so a race's own fields never hit its fleets);
    /// - a field is a circle of radius sqrt(mines): squared distances are compared with the count;
    /// - the fleet's straight path for the year (start to where it actually ended) is sampled at
    ///   whole light-years; consecutive inside points of fields of the same type form one stretch
    ///   (so overlapping same-type fields add no extra chance), at most 8 stretches per type;
    /// - the speed used is the smallest w in 3..10 with w x w >= d - 1, d the distance covered;
    /// - the racial allowance (Space Demolition +2, Super Stealth +1) is added to every safe warp,
    ///   and nothing happens if w is at most 3 + allowance or the fleet did not move;
    /// - per stretch (in order of entry distance, across types) the chance is
    ///   c = (w - allowance - safe warp) x rate per mille, one 0-999 roll per whole light-year,
    ///   and the first hit ends the routine (at most one hit per fleet per year).
    /// A hit also makes the field visible to the fleet's race (Minefield.VisibleTo) and gives a
    /// Space Demolition field owner full copies of the fleet's designs. Detonating fields
    /// (step 18) use the same damage through <see cref="Detonate"/>.
    /// </summary>
    public class CheckForMinefields
    {
        /// <summary>Up to 8 merged stretches are kept per field type.</summary>
        public const int MaxStretchesPerType = 8;

        // The injected random, or null: the rolls then come from the ambient game stream
        // (GameRandom.Current - during movement, the moving fleet's own seeded stream), so they
        // are repeatable and one fleet's hits never shift another fleet's rolls.
        private readonly Random random;
        private readonly ServerData serverState;

        public CheckForMinefields(ServerData serverState)
            : this(serverState, null)
        {
        }

        /// <summary>Test seam: inject the random source used for the per-light-year rolls.</summary>
        public CheckForMinefields(ServerData serverState, Random random)
        {
            this.serverState = serverState;
            this.random = random;
        }

        /// <summary>
        /// The speed the routine uses: the smallest whole number w from 3 to 10 with
        /// w x w >= d - 1, where d is the distance actually covered this year (not the ordered warp).
        /// </summary>
        public static int EffectiveWarp(double distanceCovered)
        {
            for (int w = 3; w < 10; w++)
            {
                if (w * w >= distanceCovered - 1)
                {
                    return w;
                }
            }

            return 10;
        }

        /// <summary>Space Demolition adds 2 to every safe warp, Super Stealth 1.</summary>
        public static int RacialAllowance(Race race)
        {
            if (race == null)
            {
                return 0;
            }

            if (race.HasTrait("SD"))
            {
                return 2;
            }

            return race.HasTrait("SS") ? 1 : 0;
        }

        /// <summary>
        /// Mines a field loses when it is hit: max(10, mines / 20) when mines / 20 is at most 50,
        /// otherwise max(50, mines / 100).
        /// </summary>
        public static int FieldLossOnHit(int mines)
        {
            int twentieth = mines / 20;
            if (twentieth <= 50)
            {
                return Math.Max(10, twentieth);
            }

            return Math.Max(50, mines / 100);
        }

        /// <summary>
        /// True when any occupied stack's engine burns no fuel at warp 4 (the six ram scoops, and
        /// also Settler's Delight, Fuel Mizer and Enigma Pulsar): the "scoop" column of the tables.
        /// </summary>
        public static bool IsScoopFleet(Fleet fleet)
        {
            foreach (ShipToken token in fleet.Composition.Values)
            {
                Engine engine = token.Quantity > 0 ? token.Design.Engine : null;
                if (engine != null && engine.FuelConsumption.Length > 3 && engine.FuelConsumption[3] == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Runs the minefield routine for a fleet that has just moved from <paramref name="start"/>
        /// to its current position. On a hit the fleet is moved back to the hit point.
        /// </summary>
        /// <param name="fleet">The moving fleet, already at the end of this year's travel.</param>
        /// <param name="start">Where the fleet was before this year's travel.</param>
        public MinefieldHit Check(Fleet fleet, NovaPoint start)
        {
            if (fleet == null || start == null || fleet.Composition.Count == 0)
            {
                return MinefieldHit.None;
            }

            NovaPoint end = fleet.Position;
            double distance = PointUtilities.Distance(start, end);
            if (distance <= 0)
            {
                return MinefieldHit.None;
            }

            Race race = null;
            if (serverState.AllEmpires.TryGetValue(fleet.Owner, out EmpireData fleetEmpire))
            {
                race = fleetEmpire.Race;
            }

            int allowance = RacialAllowance(race);
            int warp = EffectiveWarp(distance);
            if (warp <= 3 + allowance)
            {
                return MinefieldHit.None;
            }

            List<Minefield> hostileFields = new List<Minefield>();
            foreach (Minefield minefield in serverState.AllMinefields.Values)
            {
                if (minefield.NumberOfMines <= 0 || minefield.Owner == fleet.Owner || RatesFriend(minefield.Owner, fleet.Owner))
                {
                    continue;
                }

                // Quick reject: a field whose circle cannot reach the path at all.
                double reach = distance + Math.Sqrt(minefield.NumberOfMines);
                if (PointUtilities.DistanceSquare(start, minefield.Position) > reach * reach)
                {
                    continue;
                }

                hostileFields.Add(minefield);
            }

            if (hostileFields.Count == 0)
            {
                return MinefieldHit.None;
            }

            double unitX = (end.X - start.X) / distance;
            double unitY = (end.Y - start.Y) / distance;
            int wholeLightYears = (int)Math.Floor(distance);

            List<Stretch> stretches = new List<Stretch>();
            foreach (MinefieldType fieldType in Enum.GetValues(typeof(MinefieldType)))
            {
                List<Minefield> fieldsOfType = hostileFields.Where(f => f.FieldType == fieldType).ToList();
                if (fieldsOfType.Count == 0)
                {
                    continue;
                }

                int stretchesOfType = 0;
                int runStart = -1;
                for (int k = 0; k <= wholeLightYears; k++)
                {
                    bool inside = FieldContaining(fieldsOfType, start.X + (unitX * k), start.Y + (unitY * k)) != null;
                    if (inside && runStart < 0)
                    {
                        runStart = k;
                    }

                    if (runStart >= 0 && (!inside || k == wholeLightYears))
                    {
                        int runEnd = inside ? k : k - 1;
                        if (stretchesOfType < MaxStretchesPerType)
                        {
                            stretches.Add(new Stretch(fieldType, runStart, runEnd - runStart));
                            stretchesOfType++;
                        }

                        runStart = -1;
                    }
                }
            }

            foreach (Stretch stretch in stretches.OrderBy(s => s.Entry).ThenBy(s => (int)s.FieldType))
            {
                int type = (int)stretch.FieldType;
                int chancePerMille = (warp - allowance - Minefield.SafeWarpByType[type]) * Minefield.HitRatePerMilleByType[type];
                if (chancePerMille <= 0)
                {
                    continue;
                }

                for (int lightYear = 0; lightYear < stretch.Length; lightYear++)
                {
                    if ((random ?? GameRandom.Current).Next(1000) < chancePerMille)
                    {
                        int hitDistance = stretch.Entry + lightYear;
                        List<Minefield> fieldsOfType = hostileFields.Where(f => f.FieldType == stretch.FieldType).ToList();
                        double hitX = start.X + (unitX * hitDistance);
                        double hitY = start.Y + (unitY * hitDistance);
                        Minefield field = FieldContaining(fieldsOfType, hitX, hitY) ?? fieldsOfType.First();
                        NovaPoint hitPoint = hitDistance <= 0 ? new NovaPoint(start) : PointUtilities.MoveTo(start, end, hitDistance);
                        return ApplyHit(fleet, field, hitPoint);
                    }
                }
            }

            return MinefieldHit.None;
        }

        /// <summary>The nearest of the given fields whose circle contains the point, or null.</summary>
        private static Minefield FieldContaining(List<Minefield> fields, double x, double y)
        {
            Minefield nearest = null;
            double nearestSquare = double.MaxValue;
            foreach (Minefield field in fields)
            {
                double dx = x - field.Position.X;
                double dy = y - field.Position.Y;
                double square = (dx * dx) + (dy * dy);
                if (square <= field.NumberOfMines && square < nearestSquare)
                {
                    nearest = field;
                    nearestSquare = square;
                }
            }

            return nearest;
        }

        /// <summary>True when the field owner rates the fleet owner "friend".</summary>
        private bool RatesFriend(ushort fieldOwner, ushort fleetOwner)
        {
            return serverState.AllEmpires.TryGetValue(fieldOwner, out EmpireData owner)
                && owner.EmpireReports.TryGetValue(fleetOwner, out EmpireIntel intel)
                && intel.Relation == PlayerRelation.Friend;
        }

        /// <summary>
        /// Stops the fleet at the hit point, applies the type's damage, takes the field's loss and
        /// sends the messages (section 5 "Damage", "The field", "Messages").
        /// </summary>
        private MinefieldHit ApplyHit(Fleet fleet, Minefield field, NovaPoint hitPoint)
        {
            // The fleet stops at the hit point.
            fleet.Position = hitPoint;
            fleet.InOrbit = null;

            // The designs are learned from the stacks as they were hit (before any is destroyed),
            // and a transit hit reveals the field to the fleet's race.
            RevealAndLearn(fleet, field, reveal: true);
            DamageOutcome outcome = DamageFleet(fleet, field, false);
            TakeFieldLoss(field);
            SendMessages(fleet, field, outcome.AnyDamage, outcome.ShipsLost, outcome.ShipsBefore, outcome.Destroyed);

            return outcome.Destroyed ? MinefieldHit.Destroyed : MinefieldHit.Hit;
        }

        /// <summary>
        /// Detonation (turn step 18, the minefield pass FUN_10b8_433a calling the minefield
        /// routine with the detonating field as its third input; fleet-movement-scanning-cargo.md
        /// section 5 "Detonation"): every fleet inside the field's circle that has not already
        /// been caught by a detonating field this year takes the field's per-type damage once,
        /// with no roll and no stop - the field owner's own fleets included, except the owner's own
        /// stacks on the Mini Mine Layer and Super Mine Layer hulls. A Space Demolition owner learns
        /// the designs (ship-design-and-components.md: "or is caught by a detonating one"), but the
        /// field is NOT revealed to the fleet's race. The field takes NO
        /// per-fleet strike loss here: the spec gives a detonating field's cost as the +25 points
        /// of decay only (an interpretation - the shared routine's loss step is not stated either
        /// way for detonation). Starbases are not caught. Messages 351-353 (field owner) and
        /// 354-356 (fleet owner). Fleets destroyed are left with an empty composition for the
        /// caller's cleanup.
        /// </summary>
        /// <param name="field">The detonating field.</param>
        /// <param name="alreadyHit">Fleets already caught this year; those caught here are added.</param>
        /// <returns>The number of fleets caught.</returns>
        public int Detonate(Minefield field, ISet<long> alreadyHit)
        {
            if (field == null || field.NumberOfMines <= 0)
            {
                return 0;
            }

            int caught = 0;
            foreach (Fleet fleet in serverState.IterateAllFleets().ToList())
            {
                if (field.NumberOfMines <= 0)
                {
                    break;
                }

                if (fleet.Composition.Count == 0 || fleet.Position == null || fleet.IsStarbase
                    || (alreadyHit != null && alreadyHit.Contains(fleet.Key))
                    || PointUtilities.DistanceSquare(fleet.Position, field.Position) > field.NumberOfMines)
                {
                    continue;
                }

                alreadyHit?.Add(fleet.Key);
                caught++;

                // A detonation does NOT reveal the field to the fleet's race (only a transit hit
                // does); a Space Demolition field owner still learns the designs.
                RevealAndLearn(fleet, field, reveal: false);
                DamageOutcome outcome = DamageFleet(fleet, field, true);
                if (outcome.AnyDamage)
                {
                    SendDetonationMessages(fleet, field, outcome.ShipsLost, outcome.ShipsBefore, outcome.Destroyed);
                }
            }

            return caught;
        }

        /// <summary>True for a design on the Mini Mine Layer or Super Mine Layer hull (hulls 27 and 28).</summary>
        public static bool IsMineLayerHull(ShipDesign design)
        {
            string hull = design?.Blueprint?.Name;
            return hull == "Mini Mine Layer" || hull == "Super Mine Layer";
        }

        /// <summary>What <see cref="DamageFleet"/> did.</summary>
        private struct DamageOutcome
        {
            public bool AnyDamage;
            public int ShipsLost;
            public int ShipsBefore;
            public bool Destroyed;
        }

        /// <summary>
        /// Section 5 "Damage": per stack, (ships x damage per ship + top-up) x engines in the
        /// design's engine slot; the top-up (fleet minimum - damage per ship x total ships) goes to
        /// the first damaged stack only, and only for fleets of 4 ships or fewer; the stack's
        /// shields absorb up to half; the rest is added to the stack's existing damage and spread
        /// over its ships (the damage word at 100%, Nova.Common.Combat.DamageWord). If that exceeds
        /// the design's armor the stack is destroyed, leaving wreckage at the fleet's position.
        /// </summary>
        private DamageOutcome DamageFleet(Fleet fleet, Minefield field, bool detonation)
        {
            int type = (int)field.FieldType;
            bool scoop = IsScoopFleet(fleet);
            int damagePerShip = scoop ? Minefield.ScoopDamagePerShipByType[type] : Minefield.DamagePerShipByType[type];
            int fleetMinimum = scoop ? Minefield.ScoopFleetMinimumByType[type] : Minefield.FleetMinimumByType[type];
            int totalShips = fleet.Composition.Values.Sum(t => t.Quantity);

            DamageOutcome outcome = new DamageOutcome { ShipsBefore = totalShips };

            // The top-up only applies to fleets of 4 ships or fewer, and only to the first
            // damaged stack.
            bool topUpPending = totalShips <= 4;
            Resources wreckage = new Resources();
            List<long> destroyedStacks = new List<long>();

            if (damagePerShip > 0)
            {
                foreach (KeyValuePair<long, ShipToken> entry in fleet.Composition)
                {
                    ShipToken token = entry.Value;
                    int engines = EngineCount(token.Design);
                    // A detonating field spares only the FIELD OWNER's own mine-layer hulls; another
                    // race's layer hulls (friend or not) take the damage like any other ship.
                    if (token.Quantity <= 0 || engines <= 0
                        || (detonation && fleet.Owner == field.Owner && IsMineLayerHull(token.Design)))
                    {
                        continue;
                    }

                    int topUp = 0;
                    if (topUpPending)
                    {
                        topUp = Math.Max(0, fleetMinimum - (damagePerShip * totalShips));
                        topUpPending = false;
                    }

                    // (ships x damage per ship + top-up) x engines in the design's engine slot.
                    long rawDamage = (((long)token.Quantity * damagePerShip) + topUp) * engines;

                    // The stack's shields absorb up to half of it.
                    long absorbed = Math.Min((long)Math.Max(0, Math.Floor(token.Shields)), rawDamage / 2);
                    long armorDamage = rawDamage - absorbed;
                    if (armorDamage <= 0)
                    {
                        continue;
                    }

                    outcome.AnyDamage = true;
                    int armorPerShip = token.Design.Armor;
                    long total = DamageWord.For(token).TotalDamage(token.Quantity, armorPerShip) + armorDamage;
                    long perShip = (total + token.Quantity - 1) / token.Quantity;
                    // Only damage strictly above the armor destroys the stack; a figure exactly equal
                    // to the armor leaves every ship alive at that damage (section 5 "Damage").
                    if (perShip > armorPerShip)
                    {
                        outcome.ShipsLost += token.Quantity;
                        wreckage += token.Design.Cost * token.Quantity;
                        token.Armor = 0;
                        destroyedStacks.Add(entry.Key);
                    }
                    else
                    {
                        DamageWord.Store(token, DamageWord.FromPooledDamage(total, token.Quantity, armorPerShip));
                    }
                }
            }

            foreach (long key in destroyedStacks)
            {
                fleet.Composition.Remove(key);
            }

            // Destroyed ships leave wreckage at the stop point (a third of their cost, as for
            // battle wreckage - combat-resolution.md section 7). A detonating field leaves none:
            // its only cost is the +25 points of yearly decay (section 5 "Detonation").
            if (outcome.ShipsLost > 0 && !detonation)
            {
                BattleEngine.AddWreckage(serverState, fleet.Position, wreckage * (1.0 / 3.0));
            }

            outcome.Destroyed = fleet.Composition.Count == 0;
            return outcome;
        }

        /// <summary>
        /// Section 5 "The field" and "Damage": the field becomes visible to the fleet's race, and
        /// a Space Demolition field owner learns the designs of the fleet's stacks (the owner's bit
        /// in each design's +0x8b word; ship-design-and-components.md, FUN_10b0_312a
        /// :74575-74607): a full copy of each design goes into the owner's intel on the fleet's
        /// race, replacing a hull-only scan record.
        /// </summary>
        private void RevealAndLearn(Fleet fleet, Minefield field, bool reveal)
        {
            if (field.Owner == fleet.Owner)
            {
                return;
            }

            if (reveal)
            {
                // A mine hit marks the field known to the hit fleet's race (both the known and the
                // seen-this-generation masks; fleet-movement-scanning-cargo.md section 3). A
                // detonation does not reveal it.
                field.MarkKnown(fleet.Owner);
            }

            if (!serverState.AllEmpires.TryGetValue(field.Owner, out EmpireData fieldOwner)
                || fieldOwner.Race == null
                || !fieldOwner.Race.HasTrait("SD"))
            {
                return;
            }

            if (!fieldOwner.EmpireReports.TryGetValue(fleet.Owner, out EmpireIntel intel))
            {
                return;
            }

            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (token.Design == null)
                {
                    continue;
                }

                // The copy constructor needs an icon; a design without one is recorded as is.
                ShipDesign learned = token.Design.Icon != null ? new ShipDesign(token.Design) : token.Design;
                learned.Key = token.Design.Key;
                intel.Designs[learned.Key] = learned;
            }
        }

        /// <summary>
        /// The field loses max(10, mines/20) (or max(50, mines/100) for big fields); a field that
        /// loses its whole count is removed.
        /// </summary>
        private void TakeFieldLoss(Minefield field)
        {
            int loss = FieldLossOnHit(field.NumberOfMines);
            if (loss >= field.NumberOfMines)
            {
                serverState.AllMinefields.Remove(field.Key);
                field.NumberOfMines = 0;
            }
            else
            {
                field.NumberOfMines -= loss;
            }
        }

        /// <summary>The number of engines fitted in the design's engine slot(s).</summary>
        private static int EngineCount(ShipDesign design)
        {
            int engines = 0;
            if (design.Blueprint != null && design.Blueprint.Properties.ContainsKey("Hull") && design.Hull.Modules != null)
            {
                foreach (HullModule module in design.Hull.Modules)
                {
                    if (module.AllocatedComponent != null && module.AllocatedComponent.Properties.ContainsKey("Engine"))
                    {
                        engines += module.ComponentCount;
                    }
                }
            }

            return engines;
        }

        /// <summary>
        /// Messages 197-200 to the fleet's owner and the matching 201-204 to the field's owner
        /// (always a different race here): stopped with no damage, damaged with none lost, some
        /// ships lost, fleet destroyed (behavior-specs-11/fleet-movement-scanning-cargo.md §5,
        /// "Messages").
        /// </summary>
        private void SendMessages(Fleet fleet, Minefield field, bool anyDamage, int shipsLost, int shipsBefore, bool destroyed)
        {
            string fieldOwnerName = RaceName(field.Owner);
            string fleetOwnerName = RaceName(fleet.Owner);
            string kind = FieldTypeName(field.FieldType);

            string toFleetOwner;
            string toFieldOwner;
            if (destroyed)
            {
                toFleetOwner = "Fleet " + fleet.Name + " has been annihilated in a " + kind + " minefield laid by the " + fieldOwnerName + ". All " + shipsBefore + " ships were lost.";
                toFieldOwner = "The " + fleetOwnerName + " fleet " + fleet.Name + " has been annihilated by your " + kind + " minefield.";
            }
            else if (shipsLost > 0)
            {
                toFleetOwner = "Fleet " + fleet.Name + " has struck a mine in a " + kind + " minefield laid by the " + fieldOwnerName + " and lost " + shipsLost + " of " + shipsBefore + " ships. The fleet has come to a stop.";
                toFieldOwner = "The " + fleetOwnerName + " fleet " + fleet.Name + " has struck your " + kind + " minefield and lost " + shipsLost + " of " + shipsBefore + " ships.";
            }
            else if (anyDamage)
            {
                toFleetOwner = "Fleet " + fleet.Name + " has been damaged by a mine in a " + kind + " minefield laid by the " + fieldOwnerName + ", but no ships were lost. The fleet has come to a stop.";
                toFieldOwner = "The " + fleetOwnerName + " fleet " + fleet.Name + " has been damaged by your " + kind + " minefield, but no ships were lost.";
            }
            else
            {
                toFleetOwner = "Fleet " + fleet.Name + " has been stopped by a " + kind + " minefield laid by the " + fieldOwnerName + ".";
                toFieldOwner = "The " + fleetOwnerName + " fleet " + fleet.Name + " has been stopped by your " + kind + " minefield.";
            }

            serverState.AllMessages.Add(new Message(fleet.Owner, toFleetOwner, "Minefield", field));
            if (field.Owner != fleet.Owner)
            {
                serverState.AllMessages.Add(new Message(field.Owner, toFieldOwner, "Minefield", field));
            }
        }

        /// <summary>
        /// The detonation messages, the 351-356 group (behavior-specs-11/fleet-movement-scanning-
        /// cargo.md §5, "Detonation", "Messages"): to the fleet's owner 351 (fleet destroyed), 352
        /// (damaged, none lost), 353 (some ships lost); the matching 354 (destroyed), 355 (damaged,
        /// none lost) and 356 (some ships lost) to the field owner when it is a different race. The
        /// original wording is not in the repository, so this text is Nova's own.
        /// </summary>
        private void SendDetonationMessages(Fleet fleet, Minefield field, int shipsLost, int shipsBefore, bool destroyed)
        {
            string fieldOwnerName = field.Owner == fleet.Owner ? "your own empire" : "the " + RaceName(field.Owner);
            string fleetOwnerName = RaceName(fleet.Owner);
            string kind = FieldTypeName(field.FieldType);

            string toFleetOwner;
            string toFieldOwner;
            if (destroyed)
            {
                toFleetOwner = "Fleet " + fleet.Name + " has been annihilated by the detonation of a " + kind + " minefield laid by " + fieldOwnerName + ". All " + shipsBefore + " ships were lost.";
                toFieldOwner = "The detonation of your " + kind + " minefield has annihilated the " + fleetOwnerName + " fleet " + fleet.Name + ".";
            }
            else if (shipsLost > 0)
            {
                toFleetOwner = "Fleet " + fleet.Name + " has lost " + shipsLost + " of " + shipsBefore + " ships to the detonation of a " + kind + " minefield laid by " + fieldOwnerName + ".";
                toFieldOwner = "The detonation of your " + kind + " minefield has destroyed " + shipsLost + " of " + shipsBefore + " ships in the " + fleetOwnerName + " fleet " + fleet.Name + ".";
            }
            else
            {
                toFleetOwner = "Fleet " + fleet.Name + " has been damaged by the detonation of a " + kind + " minefield laid by " + fieldOwnerName + ", but no ships were lost.";
                toFieldOwner = "The detonation of your " + kind + " minefield has damaged the " + fleetOwnerName + " fleet " + fleet.Name + ", but no ships were lost.";
            }

            serverState.AllMessages.Add(new Message(fleet.Owner, toFleetOwner, "Minefield", field));
            if (field.Owner != fleet.Owner)
            {
                serverState.AllMessages.Add(new Message(field.Owner, toFieldOwner, "Minefield", field));
            }
        }

        private string RaceName(ushort empireId)
        {
            if (serverState.AllEmpires.TryGetValue(empireId, out EmpireData empire) && empire.Race != null && !string.IsNullOrEmpty(empire.Race.PluralName))
            {
                return empire.Race.PluralName;
            }

            return "empire " + empireId;
        }

        private static string FieldTypeName(MinefieldType fieldType)
        {
            switch (fieldType)
            {
                case MinefieldType.Heavy:
                    return "heavy";
                case MinefieldType.SpeedBump:
                    return "speed bump";
                default:
                    return "standard";
            }
        }

        /// <summary>A run of whole light-years of the path inside fields of one type.</summary>
        private sealed class Stretch
        {
            public Stretch(MinefieldType fieldType, int entry, int length)
            {
                FieldType = fieldType;
                Entry = entry;
                Length = length;
            }

            public MinefieldType FieldType { get; }

            public int Entry { get; }

            public int Length { get; }
        }
    }
}
