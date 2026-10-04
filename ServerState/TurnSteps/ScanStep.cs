#region Copyright Notice
// ============================================================================
// Copyright (C) 2011-2012 The Stars-Nova Project
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

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// This step updates intel with scanning information.
    /// </summary>
    public class ScanStep : ITurnStep
    {
        private ServerData serverState;

        /// <summary>The source of the probabilistic detection rolls (Space Demolition fields, wormholes).
        /// (The injected test random, or null: each Process then takes the game's seeded "Scan"
        /// stream, ServerData.CreateRandom, so detection is repeatable.)</summary>
        private readonly Random injectedRandom;
        private Random random;

        public ScanStep()
            : this(null)
        {
        }

        /// <summary>Test seam: inject the random source used for the detection rolls.</summary>
        public ScanStep(Random random)
        {
            this.injectedRandom = random;
            this.random = random;
        }


        public void Process(ServerData serverState)
        {
            this.serverState = serverState;
            random = injectedRandom ?? serverState.CreateRandom("Scan");

            // Refresh every fleet's effective cloak percentage once per turn, before it's used
            // as a scan target below - nothing else in this codebase recomputes Fleet.Cloaked as
            // composition changes (see Fleet.RecalculateCloak's own comment), and this also keeps
            // every design's Summary.Properties (including "Tachyon Detector" counts, used by
            // the counter-cloak check below) fresh as a side effect of the same Update() call.
            foreach (Fleet fleet in serverState.IterateAllFleets())
            {
                serverState.AllEmpires.TryGetValue(fleet.Owner, out EmpireData owner);
                fleet.RecalculateCloak(owner?.Race);
            }

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                AddStars(empire);
                Scan(empire);
            }
        }
        
        
        private void AddStars(EmpireData empire)
        {             
            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Owner == empire.Id)
                {
                    if (!empire.OwnedStars.Contains(star))
                    {
                        empire.OwnedStars.Add(star);
                    }
                    else
                    {
                        empire.OwnedStars[star.Name] = serverState.AllStars[star.Name];
                    }
                    
                    if (!empire.StarReports.ContainsKey(star.Name))
                    {
                       empire.StarReports.Add(star.Name, star.GenerateReport(ScanLevel.Owned, serverState.TurnYear));   
                    }
                    else
                    {
                        empire.StarReports[star.Name].Update(star, ScanLevel.Owned, serverState.TurnYear);
                    }
                    
                }
                else
                {
                    if (empire.OwnedStars.Contains(star))
                    {
                        empire.OwnedStars.Remove(star);                        
                    }
                    
                    if (!empire.StarReports.ContainsKey(star.Name))
                    {
                        empire.StarReports.Add(star.Name, star.GenerateReport(ScanLevel.None, Global.Unset));
                    }
                }
            }    
        }
        
        private void Scan(EmpireData empire)
        {
            // Fleets this empire has detected this year (by any route), and every scan source's
            // position and normal range, for the minefield and wormhole sweeps after the loop.
            HashSet<long> detectedFleets = new HashSet<long>();
            List<KeyValuePair<NovaPoint, int>> scanSources = new List<KeyValuePair<NovaPoint, int>>();

            foreach (Mappable scanner in empire.IterateAllMappables().Concat(PacketPhysicsScanners(empire)))
            {
                int scanRange = 0;
                int penScanRange = 0;
                bool fleetCanScan = false;

                //Do some self scanning (Update reports) and set ranges..
                if (scanner is MineralPacket packetScanner)
                {
                    // A Packet Physics packet's built-in penetrating scanner, range = warp²
                    // (fleet-movement-scanning-cargo.md §3; race-traits.md §2). Read as covering
                    // ordinary detection too (a penetrating scanner also sees what a normal one
                    // sees); not doubled by No Advanced Scanners (an inherent racial ability).
                    scanRange = packetScanner.ScannerRange;
                    penScanRange = packetScanner.ScannerRange;
                }
                else if (scanner is Star)
                {
                    scanRange = (scanner as Star).ScanRange;
                    penScanRange = (scanner as Star).ScanRange; // TODO:(priority 6) Planetary Pen-Scan not implemented yet.
                    empire.StarReports[scanner.Name].Update(scanner as Star, ScanLevel.Owned, serverState.TurnYear);
                }
                else
                {
                    Fleet ownFleet = scanner as Fleet;

                    // The fleet's best design, including Jack of All Trades' built-in scanner on
                    // Scout/Frigate/Destroyer hulls (ScannerRules.DesignScanRanges).
                    ScannerRules.FleetScanRanges(ownFleet, empire.Race, empire.ResearchLevels, out scanRange, out penScanRange);
                    fleetCanScan = ownFleet.CanScan || scanRange > 0 || penScanRange > 0;

                    // Unlike AddStars (above) for stars, nothing guarantees every owned fleet
                    // already has its own FleetReports entry before this self-scan runs - a fleet
                    // created this same turn (Split/Merge, a freshly-built ship) has none yet.
                    // Left unguarded, that threw KeyNotFoundException here, silently leaving this
                    // fleet's own report (and everything drawn from it - see
                    // StarMapDocumentViewModel's own comment on the marker-position bug this
                    // caused) stuck at whatever position/bearing/ship count it last had - forever,
                    // since the same fleet would keep hitting this same throw every subsequent
                    // turn once its report ever fell behind. Matches AddStars' own
                    // ContainsKey-or-Add pattern.
                    if (empire.FleetReports.ContainsKey(scanner.Key))
                    {
                        empire.FleetReports[scanner.Key].Update(ownFleet, ScanLevel.Owned, empire.TurnYear);
                    }
                    else
                    {
                        empire.FleetReports.Add(scanner.Key, ownFleet.GenerateReport(ScanLevel.Owned, empire.TurnYear));
                    }
                }

                scanSources.Add(new KeyValuePair<NovaPoint, int>(scanner.Position, scanRange));

                // Scan everything
                foreach (Mappable scanned in serverState.IterateAllMappables())
                {
                    // ...That isn't ours!
                    if (scanned.Owner == empire.Id)
                    {
                        continue;
                    }
                    
                    ScanLevel scanLevel = ScanLevel.None;
                    double range = 0;
                    range = PointUtilities.Distance(scanner.Position, scanned.Position);
                    
                    if (scanned is Star)
                    {
                        Star star = scanned as Star;
                        
                        // There are two ways to get information from a Star:
                        // 1. In orbit with a fleet,
                        // 2. Not in orbit with a Pen Scan (fleet or star).
                        // Non penetrating distance scans won't tell anything about it.
                        if((scanner is Fleet) && range == 0)
                        {
                            scanLevel = fleetCanScan ? ScanLevel.InDeepScan : ScanLevel.InPlace;
                        }
                        else // scanner is Star or non orbiting Fleet
                        {
                            scanLevel = (range <= penScanRange) ? ScanLevel.InDeepScan : ScanLevel.None;
                        }
                        
                        // Dont update if we didn't scan to allow report to age.                        
                        if (scanLevel == ScanLevel.None)
                        {
                            continue;
                        }
                        
                        if (empire.StarReports.ContainsKey(scanned.Name))
                        {
                            empire.StarReports[scanned.Name].Update((scanned as Star), scanLevel, serverState.TurnYear);
                        }
                        else
                        {
                            empire.StarReports.Add(scanned.Name, (scanned as Star).GenerateReport(scanLevel, serverState.TurnYear));
                        }
                    }
                    else // scanned is Fleet
                    {
                        // Fleets are simple as scan levels (PenScan for example) won't affect them. We only
                        // care for non penetrating distance scans.
                        // A cloak reduces the effective detection range by its percentage — e.g. an
                        // 80%-cloaked fleet is only detectable within 20% of the scanner's normal
                        // rated range. See docs/behavior-specs/fleet-movement-scanning-cargo.md §3.
                        // The observer's own installed Tachyon Detectors counter this: the target's
                        // cloak% is reduced by the detector-count multiplier BEFORE this range check
                        // (behavior-specs-7/combat-resolution.md §11 - the multiplier applies to the
                        // target's cloak, never the detector-carrying ship's own).
                        int observerDetectors = GetTachyonDetectorCount(scanner);
                        double effectiveCloak = CloakCalculator.ApplyTachyonDetectors((scanned as Fleet).Cloaked, observerDetectors);
                        double effectiveScanRange = scanRange * (100 - effectiveCloak) / 100.0;

                        if (range > effectiveScanRange)
                        {
                            continue;
                        }

                        RecordFleetDetection(empire, scanned as Fleet, detectedFleets);
                    }
                }
            }

            DetectInSpaceDemolitionMinefields(empire, detectedFleets);
            UpdateVisibleMinefields(empire, scanSources);
            DetectWormholes(empire, scanSources);
            UpdateVisiblePackets(empire, scanSources);
        }

        /// <summary>
        /// The empire's own packets in flight when it is a Packet Physics race: each carries a
        /// penetrating scanner (fleet-movement-scanning-cargo.md §3). None for any other race.
        /// </summary>
        private IEnumerable<Mappable> PacketPhysicsScanners(EmpireData empire)
        {
            if (empire.Race == null || !empire.Race.HasTrait("PP"))
            {
                return Enumerable.Empty<Mappable>();
            }

            // Only packets flying faster than warp 4 scan (fleet-movement-scanning-cargo.md §3:
            // "only packets flying faster than warp 4 scan"); a warp-4 packet's range would be 16 ly.
            return serverState.AllMineralPackets.Values
                .Where(packet => packet.Owner == empire.Id && !packet.IsEmpty && packet.Warp > 4)
                .Cast<Mappable>()
                .ToList();
        }

        /// <summary>
        /// Recomputes the mineral packets the empire sees this year (EmpireData.MineralPacketReports,
        /// copies carried in its turn file): every packet in flight for a Packet Physics race
        /// ("can sense every player's packets in flight", race-traits.md §2), and any packet within
        /// the normal range of one of its scan sources (squared distance at most range squared; a
        /// source of range 0 sees only a packet at its own position). An own packet is NOT seen
        /// automatically (fleet-movement-scanning-cargo.md §3): it is written only while it lies in
        /// range. The specs give packets no cloak, so none is applied (spec gap).
        /// </summary>
        private void UpdateVisiblePackets(EmpireData empire, List<KeyValuePair<NovaPoint, int>> scanSources)
        {
            empire.MineralPacketReports.Clear();
            bool sensesAllPackets = empire.Race != null && empire.Race.HasTrait("PP");

            foreach (MineralPacket packet in serverState.AllMineralPackets.Values)
            {
                bool visible = sensesAllPackets
                    || scanSources.Any(source =>
                        PointUtilities.DistanceSquare(source.Key, packet.Position) <= (double)source.Value * source.Value);

                if (visible)
                {
                    empire.MineralPacketReports[packet.Key] = new MineralPacket(packet);
                }
            }
        }

        /// <summary>
        /// Records a detected foreign fleet: its designs (hull only, or in full for War Monger)
        /// go into the empire's intel on the owner, and the fleet report is added or refreshed.
        /// </summary>
        private void RecordFleetDetection(EmpireData empire, Fleet scanned, HashSet<long> detectedFleets)
        {
            if (!detectedFleets.Add(scanned.Key))
            {
                return;
            }

            // Check if we have a record of this design(s).
            if (empire.EmpireReports.TryGetValue(scanned.Owner, out EmpireIntel ownerIntel))
            {
                foreach (ShipToken token in scanned.Composition.Values)
                {
                    if (ownerIntel.Designs.ContainsKey(token.Design.Key))
                    {
                        continue;
                    }

                    // Normally just the empty Hull is recorded - a scan alone doesn't
                    // reveal what's actually fitted inside it. War Monger is the
                    // documented exception: it "instantly learns the exact design of any
                    // enemy ship once scanned" (behavior-specs-7/race-traits.md §2), so a
                    // WM empire keeps every fitted component instead of the usual
                    // ClearAllocated() strip-down.
                    ShipDesign newDesign = new ShipDesign(token.Design);
                    newDesign.Key = token.Design.Key;
                    if (!empire.Race.HasTrait("WM"))
                    {
                        newDesign.ClearAllocated();
                    }
                    ownerIntel.Designs.Add(newDesign.Key, newDesign);
                }
            }

            if (!empire.FleetReports.ContainsKey(scanned.Key))
            {
                empire.FleetReports.Add(scanned.Key, scanned.GenerateReport(ScanLevel.InScan, serverState.TurnYear));
            }
            else
            {
                empire.FleetReports[scanned.Key].Update(scanned, ScanLevel.InScan, serverState.TurnYear);
            }
        }

        /// <summary>
        /// Space Demolition's minefields as a second detection layer
        /// (behavior-specs-10/fleet-movement-scanning-cargo.md §3, "Cloaking vs. scanning", and
        /// Example 3): every foreign fleet inside one of an SD empire's fields that its scanners
        /// did not already see is spotted with chance 100% - cloak% this year, one 0-99 roll per
        /// fleet however many of the fields it sits in, independent of any scanner's range. An
        /// uncloaked fleet is always spotted. Starbases are seen through their planets instead.
        /// </summary>
        private void DetectInSpaceDemolitionMinefields(EmpireData empire, HashSet<long> detectedFleets)
        {
            if (empire.Race == null || !empire.Race.HasTrait("SD"))
            {
                return;
            }

            List<Minefield> ownFields = serverState.AllMinefields.Values.Where(field => field.Owner == empire.Id && field.NumberOfMines > 0).ToList();
            if (ownFields.Count == 0)
            {
                return;
            }

            foreach (Fleet fleet in serverState.IterateAllFleets().ToList())
            {
                if (fleet.Owner == empire.Id || fleet.IsStarbase || fleet.Composition.Count == 0 || detectedFleets.Contains(fleet.Key))
                {
                    continue;
                }

                if (!ownFields.Any(field => ScannerRules.IsInsideMinefield(fleet.Position, field)))
                {
                    continue;
                }

                if (ScannerRules.SpaceDemolitionFieldDetects(fleet.Cloaked, random))
                {
                    RecordFleetDetection(empire, fleet, detectedFleets);
                }
            }
        }

        /// <summary>
        /// Recomputes the minefields the empire can see this year (EmpireData.VisibleMinefields,
        /// which IntelWriter uses to choose the fields in the player's turn file): its own, those
        /// that showed themselves by striking its fleets (Minefield.VisibleTo), and those within
        /// reach of any of its scanners. A scanner reaches a field when the squared distance is at
        /// most (normal range + the field's flat detection radius) squared, the detection radius
        /// being the field's size plus 4 (fleet-movement-scanning-cargo.md §3, "Minefield
        /// detection"; ScannerRules.DetectsMinefield).
        /// </summary>
        private void UpdateVisibleMinefields(EmpireData empire, List<KeyValuePair<NovaPoint, int>> scanSources)
        {
            empire.VisibleMinefields.Clear();

            foreach (Minefield field in serverState.AllMinefields.Values)
            {
                bool visible = field.IsVisibleTo(empire.Id)
                    || scanSources.Any(source => ScannerRules.DetectsMinefield(source.Key, source.Value, field));

                if (visible)
                {
                    empire.VisibleMinefields.Add(field.Key);
                }
            }
        }

        /// <summary>
        /// Wormhole detection (fleet-movement-scanning-cargo.md §3: "a flat radius test is
        /// combined with a random roll (0-99) against the observed object's cloak percentage"; §5:
        /// wormholes are cloaked 75% until a player has discovered them once). Each scan source
        /// with the wormhole inside its normal range gets one roll until one succeeds; a wormhole
        /// the empire already has a report for is no longer cloaked to it and is seen whenever it
        /// is in range. A detection records the wormhole's current position and the year.
        /// </summary>
        private void DetectWormholes(EmpireData empire, List<KeyValuePair<NovaPoint, int>> scanSources)
        {
            foreach (Wormhole wormhole in serverState.AllWormholes.Values)
            {
                bool discovered = empire.WormholeReports.ContainsKey(wormhole.Key);
                bool detected = false;

                foreach (KeyValuePair<NovaPoint, int> source in scanSources)
                {
                    if (ScannerRules.DetectsWormhole(source.Key, source.Value, wormhole.Position, discovered, random))
                    {
                        detected = true;
                        break;
                    }
                }

                if (!detected)
                {
                    // Out of sight the position, year and tier stay as last seen, but the race's
                    // own use of the wormhole is its own knowledge, not an observation.
                    if (empire.WormholeReports.TryGetValue(wormhole.Key, out WormholeIntel unseen))
                    {
                        unseen.UsedByUs = wormhole.IsUsedBy(empire.Id);
                    }

                    continue;
                }

                if (empire.WormholeReports.TryGetValue(wormhole.Key, out WormholeIntel report))
                {
                    report.Update(wormhole, serverState.TurnYear);
                }
                else
                {
                    report = new WormholeIntel(wormhole, serverState.TurnYear);
                    empire.WormholeReports.Add(wormhole.Key, report);
                }

                // The stability tier is copied with the sighting (WormholeIntel.Update); "used by
                // this race" is the wormhole's per-race bit (ai-opponent-behavior.md §12).
                report.UsedByUs = wormhole.IsUsedBy(empire.Id);
            }
        }

        
        /// <summary>
        /// The observing side's installed Tachyon Detector count, for CloakCalculator's
        /// counter-cloak multiplier - a scanning Fleet's own designs, or (for a scanning Star)
        /// its Starbase's, if any. Tachyon Detector is a ship-mounted (Electrical) component, so
        /// a bare planet with no starbase in orbit contributes none.
        /// </summary>
        private static int GetTachyonDetectorCount(Mappable scanner)
        {
            Fleet fleet = scanner as Fleet ?? (scanner as Star)?.Starbase;
            if (fleet == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ShipToken token in fleet.Composition.Values)
            {
                token.Design.Update();
                if (token.Design.Summary.Properties.TryGetValue("Tachyon Detector", out ComponentProperty detector))
                {
                    count += (int)(((ProbabilityProperty)detector).Value * token.Quantity);
                }
            }

            return count;
        }

        // TODO: (priority 2) Move this to the client so players can decide how long to keep
        // old reports.
        private void DiscardOldReports(EmpireData empire)
        {
            List<FleetIntel> toRemove = new List<FleetIntel>();
            
            foreach (FleetIntel report in empire.FleetReports.Values)
            {
                if (serverState.TurnYear - report.Year > Global.DiscardFleetReportAge)
                {
                    toRemove.Add(report);
                }    
            }
            
            foreach (FleetIntel report in toRemove)
            {
                empire.FleetReports.Remove(report.Key);
            }    
        }
    }
}

