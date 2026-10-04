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
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The Mystery Trader (behavior-specs-10/turn-generation-engine.md §5a).
    ///
    /// As an <see cref="ITurnStep"/> this is the fleet ENCOUNTER routine (the original's
    /// FUN_1118_0784(1)), which runs in the post-movement stage right after the battle pass and
    /// the orbital bombardment (§1 step 23c). Creation (<see cref="TryCreate"/>) is called from the
    /// random-event wrapper (<see cref="RandomEventsStep"/>, §1 step 20) and movement lives in
    /// <see cref="MysteryTraderMovementStep"/> (the special-object pass, mode 0, §1 step 15).
    ///
    /// Random draws of an encounter, in order, so a test can script them (every draw is
    /// <c>Random.Next(int)</c>), for each fleet that trades:
    /// <list type="bullet">
    /// <item>Technology route, every field capped: Next(5) (0 = message 270, nothing else).</item>
    /// <item>Technology route: per advance Next(4) (non-zero = try a random field), then, only
    ///   when trying one, Next(6) (the field, in the original's Energy, Weapons, Propulsion,
    ///   Construction, Electronics, Biotechnology order).</item>
    /// <item>Part route: Next(13) when the Trader carried technology, then up to 25 Next(13)
    ///   redraws while the drawn item is already owned.</item>
    /// <item>Ships gift (human players only): Next(4) (Next(3) after counter 100; 0 = Lifeboat),
    ///   then, if not a Lifeboat, Next(2) (0 = Scout, 1 = Probe); Next(3) (0 = two ships); after
    ///   counter 100 in a game that is not single-human, Next(counter / 100 + 1) more; and for a
    ///   Scout or Probe Next(count + 1) more.</item>
    /// </list>
    ///
    /// The computer players' planet trade (§5a "Encounter rules for planets") follows the fleet
    /// encounters of each Trader (<see cref="TradeWithPlanets"/>); its draws are up to 50 Next(13)
    /// redraws per trading planet when the Trader's part is already owned. It reads the skill
    /// tier from <see cref="PlayerSettings.AiSkill"/>, which the New Game flow must record.
    ///
    /// Message 78 (a fleet that completed its orders) is posted by the server's waypoint-task
    /// passes and withdrawn here just before a Trader absorbs the fleet
    /// (<see cref="FleetOrdersNotice"/>). Not implemented (reported): message 272 (a fleet heading
    /// for a removed Trader - fleets cannot target a Trader here). The Trader's appearance in the
    /// players' turn files is handled by <see cref="IntelWriter.VisibleMysteryTradersFor"/>.
    /// </summary>
    public class MysteryTraderStep : ITurnStep
    {
        /// <summary>Creation needs counter 40 or more (year 2440 onward in the original).</summary>
        public const int CreationYearFloor = 40;

        /// <summary>A fleet needs 5,000 kT of Ironium + Boranium + Germanium to trade.</summary>
        public const int MinimumMinerals = 5000;

        /// <summary>One more advance per further 1,200 kT, at most 10 before the tech-total rule.</summary>
        public const int MineralsPerExtraAdvance = 1200;

        /// <summary>
        /// The Trader's tech-level cap. The original picks 26, or 10 when per-race status bit 0x2
        /// is set; that bit is only ever copied from an orders-file flag the build never writes, so
        /// the cap is always 26 (§5a "What selects 26 or 10"; reimplementation note: drop the 10).
        /// </summary>
        public const int TechCap = TechLevel.MaxLevel;

        /// <summary>Ship design slots per race.</summary>
        public const int MaxShipDesigns = 16;

        /// <summary>A race must own fewer than this many fleets to receive the ships gift.</summary>
        public const int MaxFleets = 512;

        /// <summary>The special-object table's per-type serial-number limit (§5a "How many can exist").</summary>
        public const int MaxTraders = 511;

        /// <summary>Edge coordinates lie 20 ly inside the galaxy boundary (§5a "Position and destination").</summary>
        public const int EdgeInset = 20;

        public const string LifeboatName = "M.T. Lifeboat";
        public const string ScoutName = "M.T. Scout";
        public const string ProbeName = "M.T. Probe";

        public const string MessageType = "Mystery Trader";

        /// <summary>
        /// The six fields in the original's storage order (the Ener/Weap/Prop/Const/Elect/Bio list
        /// of turn-generation-engine.md §5): "a random field" is random(6) over this order and "the
        /// lowest field (the first of any tie)" is the first minimum in it.
        /// </summary>
        public static readonly TechLevel.ResearchField[] FieldOrder =
        {
            TechLevel.ResearchField.Energy,
            TechLevel.ResearchField.Weapons,
            TechLevel.ResearchField.Propulsion,
            TechLevel.ResearchField.Construction,
            TechLevel.ResearchField.Electronics,
            TechLevel.ResearchField.Biotechnology,
        };

        // Gift-mask bits with special handling (ship-design-and-components.md §14a bit table).
        private const int AntiMatterTorpedoBit = 6;
        private const int MultiContainedMunitionBit = 7;
        private const int MiniMorphBit = 8;
        private const int GenesisDeviceBit = 10;
        private const int JumpGateBit = 11;

        // The injected test random, or null: each Process then takes the game's seeded
        // "MysteryTrader" stream (ServerData.CreateRandom), so encounters are repeatable.
        private readonly Random injectedRandom;
        private Random random;

        public MysteryTraderStep() : this(null)
        {
        }

        /// <summary>Overload for deterministic testing (see the class summary for the draw order).</summary>
        public MysteryTraderStep(Random random)
        {
            this.injectedRandom = random;
            this.random = random;
        }

        /// <summary>The post-movement fleet encounters (§1 step 23c).</summary>
        public void Process(ServerData serverState)
        {
            random = injectedRandom ?? serverState.CreateRandom("MysteryTrader");

            // Forget the Trader-given flag of fleets that no longer exist.
            HashSet<long> liveFleets = new HashSet<long>(serverState.IterateAllFleets().Select(fleet => fleet.Key));
            serverState.MysteryTraderGiftFleets.RemoveWhere(key => !liveFleets.Contains(key));

            foreach (MysteryTrader trader in serverState.AllMysteryTraders.Values.ToList())
            {
                foreach (Fleet fleet in serverState.IterateAllFleets().ToList())
                {
                    // Every fleet not already destroyed that stands EXACTLY on the Trader's position.
                    if (fleet.Composition.Count == 0 || fleet.IsStarbase || fleet.Position != trader.Position)
                    {
                        continue;
                    }

                    EmpireData empire;
                    if (serverState.AllEmpires.TryGetValue(fleet.Owner, out empire) && empire.OwnedFleets.ContainsKey(fleet.Key))
                    {
                        Meet(serverState, trader, fleet, empire);
                    }
                }

                TradeWithPlanets(serverState, trader);
            }
        }

        // ------------------------------------------------------------------ planet trade

        /// <summary>The planet trade reaches planets within this many light-years of the Trader.</summary>
        public const int PlanetTradeRange = 100;

        /// <summary>The lowest computer skill tier that trades (2 = Tough; 0 Easy, 1 Standard, 3 Expert).</summary>
        public const int PlanetTradeMinimumSkill = 2;

        /// <summary>The price for a Tough computer player, kT of surface minerals.</summary>
        public const int ToughPlanetTradePrice = 3500;

        /// <summary>The price for any higher tier (Expert), kT of surface minerals.</summary>
        public const int ExpertPlanetTradePrice = 5000;

        /// <summary>Redraws for a part the race lacks when the Trader's own part is already owned.</summary>
        public const int PlanetTradePartRedraws = 50;

        /// <summary>Levels the technology branch gives: the lowest field, one level at a time.</summary>
        public const int PlanetTradeTechLevels = 6;

        /// <summary>
        /// The planet price for a skill tier: 3,500 kT for Tough (2), 5,000 kT for higher tiers;
        /// 0 for a tier that does not trade.
        /// </summary>
        public static int PlanetTradePrice(int skill)
        {
            if (skill < PlanetTradeMinimumSkill)
            {
                return 0;
            }

            return skill == PlanetTradeMinimumSkill ? ToughPlanetTradePrice : ExpertPlanetTradePrice;
        }

        /// <summary>
        /// The computer players' planet trade (§5a "Encounter rules for planets"), after the fleet
        /// encounters of the same Trader. A planet trades when it is owned and has a starbase, its
        /// owner is a computer player of skill tier 2 (Tough) or higher, it lies within 100 ly of
        /// the Trader, the Trader has not served that race, and its surface Ironium + Boranium +
        /// Germanium is at least the price (3,500 kT Tough, 5,000 kT Expert). Then:
        /// <list type="number">
        /// <item>Part first: a part the Trader carries that the race lacks is granted; if the race
        ///   already has it, up to 50 Next(13) redraws look for one it lacks. Granted silently.</item>
        /// <item>Otherwise, when the race's tech total is below 6 x (cap - 1) (150), its lowest
        ///   field is raised by one level six times, free.</item>
        /// <item>If neither applies nothing happens and nothing is paid.</item>
        /// <item>After a trade the race is served and the price is taken Germanium first, then
        ///   Boranium, then Ironium - except on the part path, which takes the planet's ENTIRE
        ///   surface stock (the code-level finding of §5a, reproduced literally).</item>
        /// </list>
        /// </summary>
        /// <remarks>
        /// Ambiguities (reported): the Trader's ships item (0x1000) is treated like a part (its
        /// bit is set silently; a computer player gets no ships), since the original tells its
        /// cargo apart only as "technology" (value 0) or an item bit; "within 100 ly" includes
        /// 100; "its lowest field is raised by one level six times" re-picks the lowest field
        /// (first of a tie, original field order) before each raise and stops at the cap. Planets
        /// are visited in this port's star-table order. The tech cap is 26 (see TechCap).
        /// </remarks>
        public void TradeWithPlanets(ServerData serverState, MysteryTrader trader)
        {
            if (random == null)
            {
                random = injectedRandom ?? serverState.CreateRandom("MysteryTrader");
            }

            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Owner == Global.Nobody || star.Starbase == null)
                {
                    continue;
                }

                if (!serverState.AllEmpires.TryGetValue(star.Owner, out EmpireData empire) || trader.ServedRaces.Contains(empire.Id))
                {
                    continue;
                }

                int price = PlanetTradePrice(ComputerSkillTier(serverState, empire));
                if (price == 0)
                {
                    continue;
                }

                double dx = star.Position.X - trader.Position.X;
                double dy = star.Position.Y - trader.Position.Y;
                if ((dx * dx) + (dy * dy) > PlanetTradeRange * PlanetTradeRange)
                {
                    continue;
                }

                Resources stock = star.ResourcesOnHand;
                if (stock.Ironium + stock.Boranium + stock.Germanium < price)
                {
                    continue;
                }

                if (TradePart(empire, trader))
                {
                    // The part path jumps to the payment with the whole stock total as the amount.
                    trader.ServedRaces.Add(empire.Id);
                    stock.Ironium = 0;
                    stock.Boranium = 0;
                    stock.Germanium = 0;
                    continue;
                }

                if (TechTotal(empire) < FieldOrder.Length * (TechCap - 1))
                {
                    StarUpdateStep levels = new StarUpdateStep();
                    for (int i = 0; i < PlanetTradeTechLevels; i++)
                    {
                        TechLevel.ResearchField lowest = LowestField(empire);
                        if (empire.ResearchLevels[lowest] >= TechCap)
                        {
                            break;
                        }

                        levels.RaiseTechLevel(serverState, empire, lowest);
                    }

                    trader.ServedRaces.Add(empire.Id);
                    PayGermaniumFirst(stock, price);
                }
            }
        }

        /// <summary>
        /// The planet trade's part step: the Trader's part if the race lacks it, otherwise up to 50
        /// Next(13) redraws for one it lacks. The item bit is set silently (no message). Returns
        /// false when the Trader carries technology or no part the race lacks was found.
        /// </summary>
        private bool TradePart(EmpireData empire, MysteryTrader trader)
        {
            if (trader.CarriesTechnology)
            {
                return false;
            }

            int item = trader.Item;
            for (int redraw = 0; redraw < PlanetTradePartRedraws && Owns(empire, item); redraw++)
            {
                item = random.Next(SpecialComponentGrants.GiftBitCount);
            }

            if (Owns(empire, item))
            {
                return false;
            }

            string name = SpecialComponentGrants.GiftBitName(item);
            empire.GrantedSpecialComponents.Add(name);

            Component component = new AllComponents().Fetch(name);
            if (component != null && empire.ResearchLevels >= component.RequiredTech && !RaceComponents.IsRestrictedFor(component, empire.Race))
            {
                empire.AvailableComponents.Add(component);
            }

            return true;
        }

        /// <summary>Takes <paramref name="price"/> kT from the stock, Germanium first, then Boranium, then Ironium.</summary>
        public static void PayGermaniumFirst(Resources stock, int price)
        {
            int fromGermanium = Math.Min(price, stock.Germanium);
            stock.Germanium -= fromGermanium;
            price -= fromGermanium;

            int fromBoranium = Math.Min(price, stock.Boranium);
            stock.Boranium -= fromBoranium;
            price -= fromBoranium;

            stock.Ironium -= Math.Min(price, stock.Ironium);
        }

        /// <summary>
        /// The computer skill tier of an empire's slot (<see cref="PlayerSettings.AiSkill"/>), or
        /// -1 for a human or a slot whose tier was not recorded.
        /// </summary>
        public static int ComputerSkillTier(ServerData serverState, EmpireData empire)
        {
            if (!IsComputerPlayer(serverState, empire))
            {
                return -1;
            }

            PlayerSettings settings = serverState.AllPlayers.FirstOrDefault(player => player.PlayerNumber == empire.Id);
            return settings == null ? -1 : settings.AiSkill;
        }

        // ------------------------------------------------------------------ creation

        /// <summary>
        /// The creation chance for a counter (§5a "Year gate and chance"), checked in order:
        /// 1 in 2 if counter mod 100 is 71; else 1 in 3 if it is 33; else 1 in 4 if counter mod 128
        /// is 49; else none in an odd year; else 1 in 7. Returns the "1 in n" n, or 0 for no chance
        /// at all (below counter 40, or an odd year).
        /// </summary>
        public static int CreationOdds(int counter)
        {
            if (counter < CreationYearFloor)
            {
                return 0;
            }

            if (counter % 100 == 71)
            {
                return 2;
            }

            if (counter % 100 == 33)
            {
                return 3;
            }

            if (counter % 128 == 49)
            {
                return 4;
            }

            return counter % 2 == 1 ? 0 : 7;
        }

        /// <summary>
        /// Mystery Trader creation (FUN_10b8_3976, §5a), the last roll of the random-event wrapper.
        /// No check for an existing Trader (several can be alive at once); the only limit is the
        /// table's 511 serial numbers, and a spawn that fails loses its roll. Draws, in order: the
        /// chance (none below counter 40 or in an odd year, see <see cref="CreationOdds"/>); the
        /// speed 8 + Next(5); the crossing (Next(2) axis, Next(2) start edge, then Next(n) for the
        /// start's and the destination's other coordinate); then the cargo (see
        /// <see cref="ChooseCargo"/>). Every player gets message 299.
        /// </summary>
        /// <returns>The new Trader, or null when none was created.</returns>
        public static MysteryTrader TryCreate(ServerData serverState, Random random, int counter)
        {
            int odds = CreationOdds(counter);
            if (odds == 0 || random.Next(odds) != 0)
            {
                return null;
            }

            if (serverState.AllMysteryTraders.Count >= MaxTraders)
            {
                return null;
            }

            MysteryTrader trader = new MysteryTrader();
            trader.Key = serverState.AllMysteryTraders.Count == 0 ? 1 : serverState.AllMysteryTraders.Keys.Max() + 1;
            trader.Speed = 8 + random.Next(5);

            // A 50/50 draw picks the axis it crosses along, a second which edge it starts on; it
            // heads for the opposite edge. The other coordinate is drawn separately for each end.
            bool alongX = random.Next(2) == 0;
            bool startOnFarEdge = random.Next(2) == 1;
            trader.Position = EdgePoint(random, alongX, startOnFarEdge);
            trader.Destination = EdgePoint(random, alongX, !startOnFarEdge);

            trader.Item = ChooseCargo(random, counter, trader.Speed);

            serverState.AllMysteryTraders.Add(trader.Key, trader);

            // Message 299: an unknown trading ship has been sighted entering the explored galaxy.
            Broadcast(serverState, "An unknown trading ship has been sighted entering the explored galaxy at "
                + trader.Position + ". It is heading for " + trader.Destination + ".");

            return trader;
        }

        /// <summary>
        /// The Trader's cargo (§5a "Cargo"). k = 5 below counter 100, 3 below 250, else 2; +1 at
        /// speed 8-9, -1 at speed 11-12. Next(10) &lt; k: no part - Next(6) == 0 the ships item,
        /// otherwise technology. Else a part Next(13); a first draw of Anti Matter Torpedo, Multi
        /// Contained Munition, Genesis Device or Jump Gate is redrawn once (the second result is
        /// kept); a final Multi Contained Munition before counter 120, Genesis Device before 150 or
        /// Jump Gate before 180 becomes technology on Next(2) == 0.
        /// </summary>
        public static int ChooseCargo(Random random, int counter, int speed)
        {
            int k = counter < 100 ? 5 : (counter < 250 ? 3 : 2);
            if (speed <= 9)
            {
                k++;
            }
            else if (speed >= 11)
            {
                k--;
            }

            if (random.Next(10) < k)
            {
                return random.Next(6) == 0 ? MysteryTrader.ShipsItem : MysteryTrader.TechnologyItem;
            }

            int bit = random.Next(SpecialComponentGrants.GiftBitCount);
            if (bit == AntiMatterTorpedoBit || bit == MultiContainedMunitionBit || bit == GenesisDeviceBit || bit == JumpGateBit)
            {
                bit = random.Next(SpecialComponentGrants.GiftBitCount);
            }

            bool yearGated = (bit == MultiContainedMunitionBit && counter < 120)
                || (bit == GenesisDeviceBit && counter < 150)
                || (bit == JumpGateBit && counter < 180);
            if (yearGated && random.Next(2) == 0)
            {
                return MysteryTrader.TechnologyItem;
            }

            return bit;
        }

        /// <summary>
        /// A destination on a random one of the four edges, by the spawn's edge rule (§5a
        /// movement step 1 and "another pass"): Next(2) axis, Next(2) edge, Next(n) other coordinate.
        /// </summary>
        public static NovaPoint RandomEdgePoint(Random random)
        {
            bool alongX = random.Next(2) == 0;
            bool farEdge = random.Next(2) == 1;
            return EdgePoint(random, alongX, farEdge);
        }

        /// <summary>
        /// A point on an edge. The original's galaxy runs from 1,000 to 1,000 + 400(s + 1); its edge
        /// coordinate is 1,020 or 1,000 + 400(s + 1) - 20 and the other coordinate is uniform over
        /// 1,020 to 1,380 + 400s (both ends included, the literal reading). This port's map runs
        /// from 0 to MapWidth / MapHeight (not necessarily square), so the edge coordinate is 20 or
        /// extent - 20 and the other coordinate is 20 + Next(extent - 40 + 1).
        /// </summary>
        /// <param name="alongX">True: the edges are x = constant (the Trader crosses along x).</param>
        public static NovaPoint EdgePoint(Random random, bool alongX, bool farEdge)
        {
            int edgeExtent = alongX ? GameSettings.Data.MapWidth : GameSettings.Data.MapHeight;
            int otherExtent = alongX ? GameSettings.Data.MapHeight : GameSettings.Data.MapWidth;

            int edge = farEdge ? edgeExtent - EdgeInset : EdgeInset;
            int other = EdgeInset + random.Next(Math.Max(1, otherExtent - (2 * EdgeInset) + 1));

            return alongX ? new NovaPoint(edge, other) : new NovaPoint(other, edge);
        }

        /// <summary>Sends one message to every player.</summary>
        public static void Broadcast(ServerData serverState, string text)
        {
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                serverState.AllMessages.Add(new Message(empire.Id, text, MessageType, null));
            }
        }

        // ------------------------------------------------------------------ encounters

        /// <summary>One fleet meeting one Trader (§5a "The encounter rules for fleets").</summary>
        private void Meet(ServerData serverState, MysteryTrader trader, Fleet fleet, EmpireData empire)
        {
            int minerals = fleet.Cargo.Ironium + fleet.Cargo.Boranium + fleet.Cargo.Germanium;

            // 1. Too few minerals: message 264, unless the fleet was itself given out by a Trader.
            // Nothing else happens; the fleet can try again another year.
            if (minerals < MinimumMinerals)
            {
                if (!serverState.MysteryTraderGiftFleets.Contains(fleet.Key))
                {
                    Tell(serverState, empire, "The Mystery Trader refused to meet with the captain of " + fleet.Name
                        + ", probably because the fleet carried too few minerals to make a deal worthwhile.");
                }
                return;
            }

            // 2. Race already served: message 280 and nothing happens.
            if (trader.ServedRaces.Contains(empire.Id))
            {
                Tell(serverState, empire, "The Mystery Trader is still recovering from its previous deal with your race and refused to meet "
                    + fleet.Name + ".");
                return;
            }

            // 3. The trade goes ahead: the race is served and the WHOLE fleet is absorbed, ships and
            // cargo. Just before that, this generation's message-78 "completed its assigned orders"
            // notices for the fleet are withdrawn, so the player is not told a fleet that no longer
            // exists finished its orders (behavior-specs-11/turn-generation-engine.md §5a).
            trader.ServedRaces.Add(empire.Id);
            NovaPoint giftPosition = new NovaPoint(fleet.Position);
            Mappable giftOrbit = fleet.InOrbit;
            FleetOrdersNotice.Withdraw(serverState.AllMessages, fleet.Key);
            empire.RemoveFleet(fleet.Key);
            serverState.MysteryTraderGiftFleets.Remove(fleet.Key);

            int counter = serverState.TurnYear - Global.StartingYear;

            // Technology route: the Trader carries technology, or a part the race already owns.
            if (trader.CarriesTechnology || Owns(empire, trader.Item))
            {
                if (!AllFieldsCapped(empire))
                {
                    TechnologyReward(serverState, empire, fleet, minerals);
                    return;
                }

                // Every field at the cap: 1 in 5 message 270 and no reward, else the part route.
                if (random.Next(5) == 0)
                {
                    Tell(serverState, empire, "The Mystery Trader took in " + fleet.Name
                        + " but found it had nothing new to teach your race.");
                    return;
                }
            }

            PartRoute(serverState, trader, empire, fleet.Name, giftPosition, giftOrbit, counter);
        }

        /// <summary>
        /// The technology route's reward (§5a): N = min(10, 6 + floor((m - 5,000) / 1,200)), then
        /// by the race's tech total T: T &gt;= 108 gives 1; 96-107 gives 2; 84-95 N - 3; 72-83 N - 2;
        /// 60-71 N - 1. Message 265 (266 if it already owns all 13 gift bits) quotes N. N times a
        /// field is chosen - 3 in 4 a random field, kept if below the cap, otherwise the lowest field
        /// (the loop stops if that is capped too) - its banked pool is doubled plus the full cost of
        /// its next level, and the research buy loop runs, so each step buys at least one level.
        /// </summary>
        private void TechnologyReward(ServerData serverState, EmpireData empire, Fleet fleet, int minerals)
        {
            int advances = AdvanceCount(minerals, TechTotal(empire));

            bool ownsEverything = Enumerable.Range(0, SpecialComponentGrants.GiftBitCount).All(bit => Owns(empire, bit));
            string text = fleet.Name + " was taken in by the Mystery Trader, who in exchange granted your race "
                + advances.ToString(CultureInfo.InvariantCulture) + " technology advance" + (advances == 1 ? string.Empty : "s") + ".";
            if (ownsEverything)
            {
                // Message 266: the variant for a race that already owns every item the Trader deals in.
                text += " The Trader had nothing else left to offer your race.";
            }
            Tell(serverState, empire, text);

            StarUpdateStep buyLoop = new StarUpdateStep();
            for (int i = 0; i < advances; i++)
            {
                TechLevel.ResearchField? chosen = null;

                // "3 times in 4": Next(4) != 0 tries a random field.
                if (random.Next(4) != 0)
                {
                    TechLevel.ResearchField candidate = FieldOrder[random.Next(FieldOrder.Length)];
                    if (empire.ResearchLevels[candidate] < TechCap)
                    {
                        chosen = candidate;
                    }
                }

                if (chosen == null)
                {
                    TechLevel.ResearchField lowest = LowestField(empire);
                    if (empire.ResearchLevels[lowest] >= TechCap)
                    {
                        break;
                    }
                    chosen = lowest;
                }

                TechLevel.ResearchField field = chosen.Value;
                int nextLevelCost = Research.Cost(field, empire.Race, empire.ResearchLevels, empire.ResearchLevels[field] + 1);
                empire.ResearchResources[field] = (empire.ResearchResources[field] * 2) + nextLevelCost;

                buyLoop.SpendBankedResearch(serverState, empire);
            }
        }

        /// <summary>
        /// N for the technology route: min(10, 6 + floor((m - 5,000) / 1,200)), adjusted by the
        /// tech total T (T &gt;= 108: 1; 96-107: 2; 84-95: N - 3; 72-83: N - 2 - the raw bytes
        /// subtract 2, not 1; 60-71: N - 1; below 60 unchanged).
        /// </summary>
        public static int AdvanceCount(int minerals, int techTotal)
        {
            int n = Math.Min(10, 6 + ((minerals - MinimumMinerals) / MineralsPerExtraAdvance));

            if (techTotal >= 108)
            {
                return 1;
            }

            if (techTotal >= 96)
            {
                return 2;
            }

            if (techTotal >= 84)
            {
                return n - 3;
            }

            if (techTotal >= 72)
            {
                return n - 2;
            }

            if (techTotal >= 60)
            {
                return n - 1;
            }

            return n;
        }

        /// <summary>
        /// The part route (§5a): the item is the Trader's part, or Next(13) if it carried
        /// technology; an owned item is redrawn up to 25 times, and if every draw is owned it
        /// becomes the ships item. A part is granted (messages 267 / 268 Mini Morph / 271 Genesis
        /// Device); the ships item goes to <see cref="ShipsGift"/>.
        /// </summary>
        private void PartRoute(ServerData serverState, MysteryTrader trader, EmpireData empire, string fleetName, NovaPoint position, Mappable orbit, int counter)
        {
            int item = trader.CarriesTechnology ? random.Next(SpecialComponentGrants.GiftBitCount) : trader.Item;
            for (int redraw = 0; redraw < 25 && Owns(empire, item); redraw++)
            {
                item = random.Next(SpecialComponentGrants.GiftBitCount);
            }

            if (Owns(empire, item))
            {
                item = MysteryTrader.ShipsItem;
            }

            if (item == MysteryTrader.ShipsItem)
            {
                ShipsGift(serverState, empire, fleetName, position, orbit, counter);
                return;
            }

            GrantPart(serverState, empire, fleetName, item);
        }

        /// <summary>
        /// Grants gift-mask bit 0-11 (the original's FUN_1118_1196): the bit is set and the race is
        /// told - message 268 for the Mini Morph hull, 271 for the Genesis Device, 267 otherwise. As
        /// with a battle grant, the part becomes buildable now if the race's tech already allows it,
        /// otherwise when its tech catches up (StarUpdateStep.TechLevelUp).
        /// </summary>
        private static void GrantPart(ServerData serverState, EmpireData empire, string fleetName, int bit)
        {
            string name = SpecialComponentGrants.GiftBitName(bit);
            empire.GrantedSpecialComponents.Add(name);

            Component component = new AllComponents().Fetch(name);
            if (component != null && empire.ResearchLevels >= component.RequiredTech && !RaceComponents.IsRestrictedFor(component, empire.Race))
            {
                empire.AvailableComponents.Add(component);
            }

            string text;
            if (bit == MiniMorphBit)
            {
                text = "In exchange for " + fleetName + ", the Mystery Trader has given your race the plans for the Mini Morph hull.";
            }
            else if (bit == GenesisDeviceBit)
            {
                text = "In exchange for " + fleetName + ", the Mystery Trader has given your race the secret of the Genesis Device.";
            }
            else
            {
                text = "In exchange for " + fleetName + ", the Mystery Trader has given your race the plans for the " + name + ".";
            }
            Tell(serverState, empire, text);
        }

        /// <summary>
        /// The ships item (§5a): computer players get nothing (the fleet is still absorbed, no
        /// message). A human gets one of the three built-in designs - Lifeboat 1 in 4 (1 in 3 after
        /// counter 100), otherwise Scout or Probe - and 1 ship, or 2 with probability 1/3; after
        /// counter 100, unless the game has only one human player, Next(counter / 100 + 1) more; the
        /// count is capped at 5; a Scout or Probe adds Next(count + 1). It needs a free slot among
        /// the 16 ship designs (or an identical design already present) and fewer than 512 fleets:
        /// then a Trader-given fleet appears at the absorbed fleet's position (message 335),
        /// otherwise message 336. "After counter 100" is read as counter &gt; 100.
        /// </summary>
        private void ShipsGift(ServerData serverState, EmpireData empire, string fleetName, NovaPoint position, Mappable orbit, int counter)
        {
            if (IsComputerPlayer(serverState, empire))
            {
                return;
            }

            bool late = counter > 100;

            string designName;
            if (random.Next(late ? 3 : 4) == 0)
            {
                designName = LifeboatName;
            }
            else
            {
                designName = random.Next(2) == 0 ? ScoutName : ProbeName;
            }

            int count = random.Next(3) == 0 ? 2 : 1;
            if (late && !IsSingleHumanGame(serverState))
            {
                count += random.Next((counter / 100) + 1);
            }
            count = Math.Min(count, 5);
            if (designName != LifeboatName)
            {
                count += random.Next(count + 1);
            }

            ShipDesign template = BuildTemplate(designName, empire);
            ShipDesign design = template == null ? null : FindIdenticalDesign(empire, template);
            bool freeSlot = design != null || CountShipDesigns(empire) < MaxShipDesigns;
            int fleetCount = empire.OwnedFleets.Values.Count(owned => !owned.IsStarbase);

            if (template == null || !freeSlot || fleetCount >= MaxFleets)
            {
                // Message 336.
                Tell(serverState, empire, "The Mystery Trader offered your race ships in exchange for " + fleetName
                    + ", but your race had no room for another ship design or fleet, so the offer was lost.");
                return;
            }

            if (design == null)
            {
                design = template;
                design.Key = empire.GetNextDesignKey();
                design.Update(empire.Race, empire.ResearchLevels);
                empire.Designs.Add(design.Key, design);
            }

            ShipToken token = new ShipToken(design, count);
            Fleet gift = new Fleet(empire.GetNextFleetKey());
            gift.Type = ItemType.Fleet;
            gift.Composition.Add(token.Key, token);
            gift.Position = new NovaPoint(position);
            gift.InOrbit = orbit;
            gift.Waypoints.Add(new Waypoint
            {
                Position = new NovaPoint(position),
                Destination = orbit != null ? orbit.Name : "Space at " + position,
                WarpFactor = 0,
            });
            gift.Name = design.Name + " #" + gift.Id;
            gift.FuelAvailable = gift.TotalFuelCapacity;

            empire.AddOrUpdateFleet(gift);
            serverState.MysteryTraderGiftFleets.Add(gift.Key);

            // Message 335.
            Tell(serverState, empire, "In exchange for " + fleetName + ", the Mystery Trader has given your race "
                + count.ToString(CultureInfo.InvariantCulture) + " " + design.Name + (count == 1 ? " ship" : " ships") + ", now in fleet " + gift.Name + ".");
        }

        /// <summary>
        /// The three Trader templates (entries 19-21 of the starting-design table, §5a, copied
        /// unchanged): "M.T. Lifeboat" - Nubian hull, 3 Enigma Pulsar, 2 x 3 Mega Poly Shell, 2 x 3
        /// Anti Matter Torpedo, 2 x 3 Langston Shell, 2 x 3 Multi Function Pod, 3 Multi Cargo Pod,
        /// 3 x 3 Multi Contained Munition; "M.T. Scout" - Mini Morph hull, 2 Enigma Pulsar, 3
        /// Langston Shell, 1 Multi Function Pod, 1 Multi Cargo Pod, 1 Jump Gate, 2 x 2 Anti Matter
        /// Torpedo; "M.T. Probe" - the Scout with 3 Mega Poly Shell instead of the Langston Shells.
        /// The engine goes in the hull's engine slot and the rest fill the general-purpose slots in
        /// the order listed (the spec gives counts, not the template's slot order). The returned
        /// design has no key yet. Null if components.xml lacks one of the parts.
        /// </summary>
        public static ShipDesign BuildTemplate(string designName, EmpireData empire)
        {
            string hullName;
            string[] fill;
            switch (designName)
            {
                case LifeboatName:
                    hullName = "Nubian";
                    fill = new[]
                    {
                        "Mega Poly Shell", "Mega Poly Shell", "Anti Matter Torpedo", "Anti Matter Torpedo",
                        "Langston Shell", "Langston Shell", "Multi Function Pod", "Multi Function Pod",
                        "Multi Cargo Pod", "Multi Contained Munition", "Multi Contained Munition", "Multi Contained Munition",
                    };
                    break;
                case ScoutName:
                case ProbeName:
                    hullName = "Mini Morph";
                    fill = new[]
                    {
                        designName == ScoutName ? "Langston Shell" : "Mega Poly Shell",
                        "Multi Function Pod", "Multi Cargo Pod", "Jump Gate", "Anti Matter Torpedo", "Anti Matter Torpedo",
                    };
                    break;
                default:
                    return null;
            }

            AllComponents components = new AllComponents();
            Component hull = components.Fetch(hullName);
            Component engine = components.Fetch("Enigma Pulsar");
            if (hull == null || engine == null || !hull.Properties.ContainsKey("Hull"))
            {
                return null;
            }

            ShipDesign design = new ShipDesign(0);
            design.Blueprint = hull;
            design.Type = ItemType.Ship;
            design.Name = designName;
            design.Icon = new ShipIcon(hull.ImageFile, hull.ComponentImage);

            int next = 0;
            foreach (HullModule module in design.Hull.Modules)
            {
                Component part;
                if (module.ComponentType == "Engine")
                {
                    part = engine;
                }
                else if (next < fill.Length)
                {
                    part = components.Fetch(fill[next++]);
                }
                else
                {
                    continue;
                }

                if (part == null)
                {
                    return null;
                }

                // Every slot is filled to capacity.
                module.AllocatedComponent = part;
                module.ComponentCount = module.ComponentMaximum;
            }

            design.Update(empire.Race, empire.ResearchLevels);
            return design;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Whether the race already owns gift-mask bit 0-12.</summary>
        private static bool Owns(EmpireData empire, int bit)
        {
            return empire.GrantedSpecialComponents.Contains(SpecialComponentGrants.GiftBitName(bit));
        }

        private static int TechTotal(EmpireData empire)
        {
            return FieldOrder.Sum(field => empire.ResearchLevels[field]);
        }

        private static bool AllFieldsCapped(EmpireData empire)
        {
            return FieldOrder.All(field => empire.ResearchLevels[field] >= TechCap);
        }

        private static TechLevel.ResearchField LowestField(EmpireData empire)
        {
            TechLevel.ResearchField lowest = FieldOrder[0];
            foreach (TechLevel.ResearchField field in FieldOrder)
            {
                if (empire.ResearchLevels[field] < empire.ResearchLevels[lowest])
                {
                    lowest = field;
                }
            }
            return lowest;
        }

        /// <summary>A computer player: an AllPlayers slot for this empire whose AI program is not "Human".</summary>
        private static bool IsComputerPlayer(ServerData serverState, EmpireData empire)
        {
            PlayerSettings settings = serverState.AllPlayers.FirstOrDefault(player => player.PlayerNumber == empire.Id);
            return settings != null && settings.AiProgram != null && settings.AiProgram != "Human";
        }

        /// <summary>The original's "single human player" game flag (options bit 0x04): exactly one human slot.</summary>
        private static bool IsSingleHumanGame(ServerData serverState)
        {
            return serverState.AllPlayers.Count(player => player.AiProgram == "Human") == 1;
        }

        private static int CountShipDesigns(EmpireData empire)
        {
            return empire.Designs.Values.Count(design => design.Blueprint != null
                && design.Blueprint.Properties.ContainsKey("Hull") && !design.IsStarbase);
        }

        /// <summary>An existing design with the template's name, hull and slot contents.</summary>
        private static ShipDesign FindIdenticalDesign(EmpireData empire, ShipDesign template)
        {
            string signature = Signature(template);
            return empire.Designs.Values.FirstOrDefault(design => design.Name == template.Name
                && design.Blueprint != null && design.Blueprint.Properties.ContainsKey("Hull")
                && Signature(design) == signature);
        }

        private static string Signature(ShipDesign design)
        {
            return design.Blueprint.Name + ":" + string.Join(",", design.Hull.Modules.Select(module =>
                (module.AllocatedComponent == null ? string.Empty : module.AllocatedComponent.Name) + "x" + module.ComponentCount));
        }

        private static void Tell(ServerData serverState, EmpireData empire, string text)
        {
            serverState.AllMessages.Add(new Message(empire.Id, text, MessageType, null));
        }
    }
}
