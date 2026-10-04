namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    /// <summary>One broken rule: which rule, when, whose, which object, and what was seen.</summary>
    public sealed class InvariantViolation
    {
        public string Rule { get; set; }

        public int Turn { get; set; }

        public int Year { get; set; }

        /// <summary>0 when the violation is not about one empire.</summary>
        public int EmpireId { get; set; }

        /// <summary>The object concerned (fleet / star / design name or key).</summary>
        public string Subject { get; set; }

        public string Message { get; set; }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0}] turn {1} (year {2}){3}{4}: {5}",
                Rule,
                Turn,
                Year,
                EmpireId != 0 ? " empire " + EmpireId : string.Empty,
                string.IsNullOrEmpty(Subject) ? string.Empty : " " + Subject,
                Message);
        }
    }

    /// <summary>A rule's raw finding, before the checker stamps rule name and turn on it.</summary>
    public readonly struct Finding
    {
        public Finding(int empireId, string subject, string message)
        {
            EmpireId = empireId;
            Subject = subject;
            Message = message;
        }

        public int EmpireId { get; }

        public string Subject { get; }

        public string Message { get; }
    }

    /// <summary>Facts about one turn that are not in the saved state: what the harness saw while
    /// running it.</summary>
    public sealed class TurnObservation
    {
        public int Turn { get; set; }

        public int Year { get; set; }

        public GenerationObservations Generation { get; set; } = new GenerationObservations();

        public Dictionary<int, AiSubmission> Submissions { get; set; } = new Dictionary<int, AiSubmission>();

        /// <summary>Empires whose orders the harness wrote this turn.</summary>
        public HashSet<int> AiEmpires { get; set; } = new HashSet<int>();

        /// <summary>AI empires whose orders file OrderReader did not accept.</summary>
        public HashSet<int> OrdersNotAccepted { get; set; } = new HashSet<int>();

        public List<string> Errors { get; set; } = new List<string>();

        public double TurnSeconds { get; set; }

        public bool RoundTripChecked { get; set; }

        /// <summary>Null when the reload/re-save text matched (or was not checked).</summary>
        public string RoundTripDifference { get; set; }
    }

    public sealed class InvariantContext
    {
        public InvariantContext(ServerData state, TurnObservation turn, SimulationLimits limits)
        {
            State = state;
            Turn = turn ?? new TurnObservation { Year = state.TurnYear };
            Limits = limits ?? new SimulationLimits();
        }

        public ServerData State { get; }

        public TurnObservation Turn { get; }

        public SimulationLimits Limits { get; }

        public int MapWidth
        {
            get { return GameSettings.Data.MapWidth; }
        }

        public int MapHeight
        {
            get { return GameSettings.Data.MapHeight; }
        }

        public bool InsideMap(NovaPoint point)
        {
            int margin = Limits.MapMargin;
            return point != null
                && point.X >= -margin && point.X <= MapWidth + margin
                && point.Y >= -margin && point.Y <= MapHeight + margin;
        }
    }

    /// <summary>A named, individually switchable invariant.</summary>
    public sealed class InvariantRule
    {
        public InvariantRule(string name, string description, Func<InvariantContext, IEnumerable<Finding>> check)
        {
            Name = name;
            Description = description;
            Check = check;
        }

        public string Name { get; }

        public string Description { get; }

        public Func<InvariantContext, IEnumerable<Finding>> Check { get; }
    }

    /// <summary>
    /// Runs the invariant rules over a game state after a turn. Every rule is data (a name, a
    /// description and a check), so a failure names the rule, the turn, the empire and the
    /// object; rules can be disabled by name (SimulationConfig.DisabledInvariants) and callers can
    /// add their own.
    /// </summary>
    public sealed class InvariantChecker
    {
        /// <summary>The rule the runner reports when a turn throws.</summary>
        public const string NoExceptionRule = "NoException";

        private const int MaxFindingsPerRule = 25;

        public InvariantChecker()
            : this(DefaultRules())
        {
        }

        public InvariantChecker(IEnumerable<InvariantRule> rules)
        {
            Rules = rules.ToList();
        }

        public List<InvariantRule> Rules { get; }

        public List<InvariantViolation> Check(InvariantContext context, ICollection<string> disabled = null)
        {
            List<InvariantViolation> violations = new List<InvariantViolation>();
            foreach (InvariantRule rule in Rules)
            {
                if (disabled != null && disabled.Contains(rule.Name))
                {
                    continue;
                }

                List<Finding> findings;
                try
                {
                    findings = rule.Check(context).ToList();
                }
                catch (Exception e)
                {
                    findings = new List<Finding> { new Finding(0, null, "the rule itself threw " + e.GetType().Name + ": " + e.Message) };
                }

                int total = findings.Count;
                foreach (Finding finding in findings.Take(MaxFindingsPerRule))
                {
                    violations.Add(Stamp(rule.Name, context, finding));
                }

                if (total > MaxFindingsPerRule)
                {
                    violations.Add(Stamp(rule.Name, context, new Finding(0, null, (total - MaxFindingsPerRule) + " more findings of this rule this turn")));
                }
            }

            return violations;
        }

        private static InvariantViolation Stamp(string rule, InvariantContext context, Finding finding)
        {
            return new InvariantViolation
            {
                Rule = rule,
                Turn = context.Turn.Turn,
                Year = context.Turn.Year != 0 ? context.Turn.Year : context.State.TurnYear,
                EmpireId = finding.EmpireId,
                Subject = finding.Subject,
                Message = finding.Message,
            };
        }

        /// <summary>Every built-in rule, in report order.</summary>
        public static List<InvariantRule> DefaultRules()
        {
            return new List<InvariantRule>
            {
                new InvariantRule("NoReportedErrors", "The game reported no Report.Error during the turn (outside the allowed substrings).", NoReportedErrors),
                new InvariantRule("AiOrdersAccepted", "Every AI's orders file was accepted by OrderReader, parsed without a bad order, and no command failed ICommand.IsValid.", AiOrdersAccepted),
                new InvariantRule("StockpilesNonNegative", "Planet minerals, population and installations, fleet cargo and fuel, and research resources are never negative.", StockpilesNonNegative),
                new InvariantRule("NumbersFinite", "Fleet fuel, bearing, cloak and target distance are finite numbers.", NumbersFinite),
                new InvariantRule("TechLevelsInRange", "Every research level is between 0 and TechLevel.MaxLevel (26).", TechLevelsInRange),
                new InvariantRule("FleetCap", "An empire owns at most MaxFleetsPerEmpire (512) non-starbase fleets.", FleetCap),
                new InvariantRule("DesignCap", "An empire holds at most 16 ship designs and 10 starbase designs.", DesignCap),
                new InvariantRule("FleetsInsideMap", "Every fleet, minefield and packet position is inside the map (plus MapMargin).", FleetsInsideMap),
                new InvariantRule("WaypointTargetsResolvable", "Every waypoint position is on the map; planet targets name an existing star; fleet targets name an existing fleet (or are frozen).", WaypointTargetsResolvable),
                new InvariantRule("NoEmptyFleets", "No fleet has zero ship tokens, a token with quantity <= 0, or a token without a design.", NoEmptyFleets),
                new InvariantRule("ShipDesignsExist", "Every ship token's design is one of its owner's designs.", ShipDesignsExist),
                new InvariantRule("KeysUnique", "Fleet, design and minefield keys match their dictionary keys and owners and are unique across empires.", KeysUnique),
                new InvariantRule("PlanetOwnersExist", "Every owned star's owner exists and lists the star; every star an empire lists is owned by it.", PlanetOwnersExist),
                new InvariantRule("PlanetRaceMatchesOwner", "An owned planet's ThisRace is its owner's race (growth, habitability and production all read it).", PlanetRaceMatchesOwner),
                new InvariantRule("StarbaseConsistent", "A starbase belongs to its planet's owner, is one of that owner's fleets, and only owned planets have one.", StarbaseConsistent),
                new InvariantRule("OrbitConsistent", "A fleet in orbit sits at the position of the star it orbits.", OrbitConsistent),
                new InvariantRule("ProductionQueuesValid", "Queues hold only non-negative quantities, ship orders for existing designs, at most MaxQueueLength entries, and nothing on unowned planets.", ProductionQueuesValid),
                new InvariantRule("PopulationWithinCapacity", "Population stays below PopulationCapacityFactor x max(capacity, PopulationFloor).", PopulationWithinCapacity),
                new InvariantRule("ScoreEliminationConsistent", "Scores are non-negative, ranks are 1..N, the elimination flag matches the elimination test, and this year's score history is recorded.", ScoreEliminationConsistent),
                new InvariantRule("SaveRoundTrip", "Reloading the saved state and saving it again gives identical text.", SaveRoundTrip),
                new InvariantRule("MessageCountBounded", "No empire receives more than MaxMessagesPerEmpirePerTurn messages in a turn (message storm).", MessageCountBounded),
                new InvariantRule("TurnTimeBounded", "A whole turn (AI moves plus generation) takes at most MaxTurnSeconds.", TurnTimeBounded),
            };
        }

        // ------------------------------------------------------------------------------ rules

        private static IEnumerable<Finding> NoReportedErrors(InvariantContext c)
        {
            foreach (string error in c.Turn.Errors)
            {
                if (c.Limits.AllowedErrorSubstrings.Any(allowed => error.IndexOf(allowed, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                yield return new Finding(0, null, OneLine(error));
            }
        }

        private static IEnumerable<Finding> AiOrdersAccepted(InvariantContext c)
        {
            foreach (int empireId in c.Turn.OrdersNotAccepted.OrderBy(id => id))
            {
                yield return new Finding(empireId, null, "OrderReader did not accept this AI's orders file (wrong turn/id, unreadable XML, or missing)");
            }

            foreach (KeyValuePair<int, int> rejected in c.Turn.Generation.RejectedByEmpire.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key))
            {
                if (c.Turn.AiEmpires.Count > 0 && !c.Turn.AiEmpires.Contains(rejected.Key))
                {
                    continue;
                }

                string samples = string.Join(" | ", c.Turn.Generation.RejectedSamples.Where(s => s.StartsWith("empire " + rejected.Key + " ", StringComparison.Ordinal)).Take(3));
                yield return new Finding(rejected.Key, null, rejected.Value + " command(s) failed IsValid: " + samples);
            }

            foreach (string error in c.Turn.Errors.Where(e => e.IndexOf("problem reading", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                yield return new Finding(0, null, "order parse failure: " + OneLine(error));
            }
        }

        private static IEnumerable<Finding> StockpilesNonNegative(InvariantContext c)
        {
            foreach (Star star in c.State.AllStars.Values)
            {
                Resources r = star.ResourcesOnHand;
                if (r != null && (r.Ironium < 0 || r.Boranium < 0 || r.Germanium < 0 || r.Energy < 0))
                {
                    yield return new Finding(star.Owner, "star " + star.Name, "negative stockpile " + r.Ironium + "/" + r.Boranium + "/" + r.Germanium + " energy " + r.Energy);
                }

                if (star.Colonists < 0 || star.Factories < 0 || star.Mines < 0 || star.Defenses < 0)
                {
                    yield return new Finding(star.Owner, "star " + star.Name, "negative colonists/factories/mines/defenses " + star.Colonists + "/" + star.Factories + "/" + star.Mines + "/" + star.Defenses);
                }
            }

            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    Cargo cargo = fleet.Cargo;
                    if (cargo != null && (cargo.Ironium < 0 || cargo.Boranium < 0 || cargo.Germanium < 0 || cargo.ColonistsInKilotons < 0))
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "negative cargo " + cargo.Ironium + "/" + cargo.Boranium + "/" + cargo.Germanium + " colonists " + cargo.ColonistsInKilotons + " kT");
                    }

                    if (fleet.FuelAvailable < -0.5)
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "negative fuel " + fleet.FuelAvailable.ToString(CultureInfo.InvariantCulture));
                    }
                }

                foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
                {
                    if (empire.ResearchResources[field] < 0)
                    {
                        yield return new Finding(empire.Id, field.ToString(), "negative research resources " + empire.ResearchResources[field]);
                    }
                }
            }
        }

        private static IEnumerable<Finding> NumbersFinite(InvariantContext c)
        {
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    if (!Finite(fleet.FuelAvailable) || !Finite(fleet.Bearing) || !Finite(fleet.Cloaked) || !Finite(fleet.TargetDistance))
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "non-finite fuel/bearing/cloak/target distance " + fleet.FuelAvailable + "/" + fleet.Bearing + "/" + fleet.Cloaked + "/" + fleet.TargetDistance);
                    }
                }
            }
        }

        private static IEnumerable<Finding> TechLevelsInRange(InvariantContext c)
        {
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
                {
                    int level = empire.ResearchLevels[field];
                    if (level < 0 || level > TechLevel.MaxLevel)
                    {
                        yield return new Finding(empire.Id, field.ToString(), "tech level " + level + " outside 0.." + TechLevel.MaxLevel);
                    }
                }
            }
        }

        private static IEnumerable<Finding> FleetCap(InvariantContext c)
        {
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                int fleets = empire.OwnedFleets.Values.Count(fleet => !fleet.IsStarbase);
                if (fleets > c.Limits.MaxFleetsPerEmpire)
                {
                    yield return new Finding(empire.Id, null, fleets + " fleets, cap " + c.Limits.MaxFleetsPerEmpire);
                }
            }
        }

        private static IEnumerable<Finding> DesignCap(InvariantContext c)
        {
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                int ships = empire.Designs.Values.Count(design => !design.IsStarbase);
                int bases = empire.Designs.Values.Count(design => design.IsStarbase);
                if (ships > c.Limits.MaxShipDesigns)
                {
                    yield return new Finding(empire.Id, null, ships + " ship designs, cap " + c.Limits.MaxShipDesigns + " (" + string.Join(", ", empire.Designs.Values.Where(d => !d.IsStarbase).Select(d => d.Name)) + ")");
                }

                if (bases > c.Limits.MaxStarbaseDesigns)
                {
                    yield return new Finding(empire.Id, null, bases + " starbase designs, cap " + c.Limits.MaxStarbaseDesigns);
                }
            }
        }

        private static IEnumerable<Finding> FleetsInsideMap(InvariantContext c)
        {
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    if (!c.InsideMap(fleet.Position))
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "position " + Point(fleet.Position) + " outside the " + c.MapWidth + "x" + c.MapHeight + " map");
                    }
                }
            }

            foreach (Minefield field in c.State.AllMinefields.Values)
            {
                if (!c.InsideMap(field.Position))
                {
                    yield return new Finding(field.Owner, "minefield " + field.Key.ToString("X", CultureInfo.InvariantCulture), "position " + Point(field.Position) + " outside the map");
                }
            }

            foreach (MineralPacket packet in c.State.AllMineralPackets.Values)
            {
                if (!c.InsideMap(packet.Position))
                {
                    yield return new Finding(packet.Owner, "packet " + packet.Key.ToString("X", CultureInfo.InvariantCulture), "position " + Point(packet.Position) + " outside the map");
                }
            }
        }

        private static IEnumerable<Finding> WaypointTargetsResolvable(InvariantContext c)
        {
            HashSet<long> allFleets = new HashSet<long>(c.State.IterateAllFleets().Select(fleet => fleet.Key));
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    for (int i = 0; i < fleet.Waypoints.Count; i++)
                    {
                        Waypoint waypoint = fleet.Waypoints[i];
                        string where = FleetName(fleet) + " waypoint " + i;
                        if (waypoint == null)
                        {
                            yield return new Finding(empire.Id, where, "null waypoint");
                            continue;
                        }

                        if (waypoint.Position == null || !c.InsideMap(waypoint.Position))
                        {
                            yield return new Finding(empire.Id, where, "position " + Point(waypoint.Position) + " off the map (destination '" + waypoint.Destination + "')");
                        }

                        if (waypoint.TargetKind == WaypointTargetKind.Planet && !c.State.AllStars.ContainsKey(waypoint.Destination ?? string.Empty))
                        {
                            yield return new Finding(empire.Id, where, "planet target '" + waypoint.Destination + "' is not a star");
                        }

                        if (waypoint.IsFleetTarget && !waypoint.PursuitFrozen && !allFleets.Contains(waypoint.TargetFleetKey))
                        {
                            yield return new Finding(empire.Id, where, "fleet target " + waypoint.TargetFleetKey.ToString("X", CultureInfo.InvariantCulture) + " no longer exists");
                        }

                        if (waypoint.Task == null)
                        {
                            yield return new Finding(empire.Id, where, "null task");
                        }
                    }
                }
            }
        }

        private static IEnumerable<Finding> NoEmptyFleets(InvariantContext c)
        {
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    if (fleet.Composition.Count == 0)
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "fleet with no ships");
                        continue;
                    }

                    foreach (ShipToken token in fleet.Composition.Values)
                    {
                        if (token.Design == null)
                        {
                            yield return new Finding(empire.Id, FleetName(fleet), "token " + token.Key + " has no design");
                        }
                        else if (token.Quantity <= 0)
                        {
                            yield return new Finding(empire.Id, FleetName(fleet), "token " + token.Design.Name + " has quantity " + token.Quantity);
                        }
                    }
                }
            }
        }

        private static IEnumerable<Finding> ShipDesignsExist(InvariantContext c)
        {
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    foreach (ShipToken token in fleet.Composition.Values)
                    {
                        if (token.Design != null && !empire.Designs.ContainsKey(token.Design.Key))
                        {
                            yield return new Finding(empire.Id, FleetName(fleet), "ships of design '" + token.Design.Name + "' (" + token.Design.Key.ToString("X", CultureInfo.InvariantCulture) + ") which the empire does not have");
                        }
                    }
                }
            }
        }

        private static IEnumerable<Finding> KeysUnique(InvariantContext c)
        {
            Dictionary<long, int> fleetOwners = new Dictionary<long, int>();
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (KeyValuePair<long, Fleet> entry in empire.OwnedFleets)
                {
                    Fleet fleet = entry.Value;
                    if (entry.Key != fleet.Key)
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "stored under key " + entry.Key.ToString("X", CultureInfo.InvariantCulture) + " but its key is " + fleet.Key.ToString("X", CultureInfo.InvariantCulture));
                    }

                    if (fleet.Owner != empire.Id)
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "listed by empire " + empire.Id + " but owned by " + fleet.Owner);
                    }

                    if (fleetOwners.TryGetValue(fleet.Key, out int other))
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "fleet key also used by empire " + other);
                    }
                    else
                    {
                        fleetOwners[fleet.Key] = empire.Id;
                    }
                }

                foreach (KeyValuePair<long, ShipDesign> entry in empire.Designs)
                {
                    if (entry.Key != entry.Value.Key)
                    {
                        yield return new Finding(empire.Id, "design " + entry.Value.Name, "stored under key " + entry.Key.ToString("X", CultureInfo.InvariantCulture) + " but its key is " + entry.Value.Key.ToString("X", CultureInfo.InvariantCulture));
                    }

                    if (entry.Value.Owner != empire.Id)
                    {
                        yield return new Finding(empire.Id, "design " + entry.Value.Name, "design owned by " + entry.Value.Owner);
                    }
                }

                foreach (IGrouping<string, ShipDesign> duplicate in empire.Designs.Values.GroupBy(d => d.Name ?? string.Empty).Where(g => g.Count() > 1))
                {
                    yield return new Finding(empire.Id, "design " + duplicate.Key, duplicate.Count() + " designs share this name");
                }
            }

            foreach (KeyValuePair<long, Minefield> entry in c.State.AllMinefields)
            {
                if (entry.Key != entry.Value.Key)
                {
                    yield return new Finding(entry.Value.Owner, "minefield", "stored under key " + entry.Key.ToString("X", CultureInfo.InvariantCulture) + " but its key is " + entry.Value.Key.ToString("X", CultureInfo.InvariantCulture));
                }
            }
        }

        private static IEnumerable<Finding> PlanetOwnersExist(InvariantContext c)
        {
            foreach (Star star in c.State.AllStars.Values)
            {
                if (star.Owner == Global.Nobody)
                {
                    continue;
                }

                if (!c.State.AllEmpires.TryGetValue(star.Owner, out EmpireData owner))
                {
                    yield return new Finding(star.Owner, "star " + star.Name, "owned by empire " + star.Owner + ", which does not exist");
                    continue;
                }

                if (!owner.OwnedStars.ContainsKey(star.Key))
                {
                    yield return new Finding(star.Owner, "star " + star.Name, "owned by this empire but missing from its OwnedStars");
                }
            }

            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (KeyValuePair<string, Star> entry in empire.OwnedStars)
                {
                    if (!c.State.AllStars.TryGetValue(entry.Key, out Star star))
                    {
                        yield return new Finding(empire.Id, "star " + entry.Key, "listed in OwnedStars but not a star of the galaxy");
                    }
                    else if (star.Owner != empire.Id)
                    {
                        yield return new Finding(empire.Id, "star " + entry.Key, "listed in OwnedStars but owned by " + star.Owner);
                    }
                }
            }
        }

        private static IEnumerable<Finding> PlanetRaceMatchesOwner(InvariantContext c)
        {
            foreach (Star star in c.State.AllStars.Values)
            {
                if (star.Owner == Global.Nobody || !c.State.AllEmpires.TryGetValue(star.Owner, out EmpireData owner) || owner.Race == null)
                {
                    continue;
                }

                if (star.ThisRace == null || star.ThisRace.Name != owner.Race.Name)
                {
                    yield return new Finding(star.Owner, "star " + star.Name, "owned by " + owner.Race.Name + " but its race is " + (star.ThisRace == null ? "(none)" : star.ThisRace.Name) + " (" + star.Colonists + " colonists)");
                }
            }
        }

        private static IEnumerable<Finding> StarbaseConsistent(InvariantContext c)
        {
            foreach (Star star in c.State.AllStars.Values)
            {
                if (star.Starbase == null)
                {
                    continue;
                }

                if (star.Owner == Global.Nobody)
                {
                    yield return new Finding(star.Starbase.Owner, "star " + star.Name, "unowned planet still has starbase " + star.Starbase.Name);
                    continue;
                }

                if (star.Starbase.Owner != star.Owner)
                {
                    yield return new Finding(star.Owner, "star " + star.Name, "starbase " + star.Starbase.Name + " belongs to empire " + star.Starbase.Owner);
                }

                if (c.State.AllEmpires.TryGetValue(star.Owner, out EmpireData owner) && !owner.OwnedFleets.ContainsKey(star.Starbase.Key))
                {
                    yield return new Finding(star.Owner, "star " + star.Name, "starbase " + star.Starbase.Name + " is not one of the owner's fleets");
                }
            }
        }

        private static IEnumerable<Finding> OrbitConsistent(InvariantContext c)
        {
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    if (fleet.InOrbit != null && fleet.InOrbit.Position != null && fleet.Position != null
                        && (fleet.InOrbit.Position.X != fleet.Position.X || fleet.InOrbit.Position.Y != fleet.Position.Y))
                    {
                        yield return new Finding(empire.Id, FleetName(fleet), "in orbit of " + fleet.InOrbit.Name + " at " + Point(fleet.InOrbit.Position) + " but positioned at " + Point(fleet.Position));
                    }
                }
            }
        }

        private static IEnumerable<Finding> ProductionQueuesValid(InvariantContext c)
        {
            foreach (Star star in c.State.AllStars.Values)
            {
                List<ProductionOrder> queue = star.ManufacturingQueue == null ? null : star.ManufacturingQueue.Queue;
                if (queue == null || queue.Count == 0)
                {
                    continue;
                }

                if (star.Owner == Global.Nobody)
                {
                    yield return new Finding(0, "star " + star.Name, "unowned planet has " + queue.Count + " queued orders");
                    continue;
                }

                if (queue.Count > c.Limits.MaxQueueLength)
                {
                    yield return new Finding(star.Owner, "star " + star.Name, queue.Count + " queued orders, limit " + c.Limits.MaxQueueLength);
                }

                c.State.AllEmpires.TryGetValue(star.Owner, out EmpireData owner);
                foreach (ProductionOrder order in queue)
                {
                    if (order == null || order.Unit == null)
                    {
                        yield return new Finding(star.Owner, "star " + star.Name, "null production order or unit");
                        continue;
                    }

                    if (order.Quantity < 0)
                    {
                        yield return new Finding(star.Owner, "star " + star.Name, "order '" + order.Name + "' has quantity " + order.Quantity);
                    }

                    if (order.Unit is ShipProductionUnit ship && owner != null && !owner.Designs.ContainsKey(ship.DesignKey))
                    {
                        yield return new Finding(star.Owner, "star " + star.Name, "ship order '" + order.Name + "' for design " + ship.DesignKey.ToString("X", CultureInfo.InvariantCulture) + " the empire does not have");
                    }
                }
            }
        }

        private static IEnumerable<Finding> PopulationWithinCapacity(InvariantContext c)
        {
            foreach (Star star in c.State.AllStars.Values)
            {
                if (star.Owner == Global.Nobody || star.Colonists <= 0 || !c.State.AllEmpires.TryGetValue(star.Owner, out EmpireData owner) || owner.Race == null)
                {
                    continue;
                }

                double capacity = star.CapacityColonists(owner.Race);
                double bound = c.Limits.PopulationCapacityFactor * Math.Max(capacity, c.Limits.PopulationFloor);
                if (star.Colonists > bound)
                {
                    yield return new Finding(star.Owner, "star " + star.Name, star.Colonists + " colonists, capacity " + capacity.ToString("0", CultureInfo.InvariantCulture) + " (bound " + bound.ToString("0", CultureInfo.InvariantCulture) + ")");
                }
            }
        }

        private static IEnumerable<Finding> ScoreEliminationConsistent(InvariantContext c)
        {
            List<ScoreRecord> scores = new Scores(c.State).GetScores();
            int count = scores.Count;
            foreach (ScoreRecord record in scores)
            {
                if (record.Score < 0)
                {
                    yield return new Finding(record.EmpireId, null, "negative score " + record.Score);
                }

                if (record.Rank < 1 || record.Rank > count)
                {
                    yield return new Finding(record.EmpireId, null, "rank " + record.Rank + " outside 1.." + count);
                }

                if (!c.State.AllEmpires.TryGetValue(record.EmpireId, out EmpireData empire))
                {
                    continue;
                }

                bool test = VictoryCheck.IsEliminated(record);
                if (test && !empire.Eliminated)
                {
                    yield return new Finding(record.EmpireId, null, "has no planets and no ships but is not flagged eliminated");
                }
                else if (!test && empire.Eliminated)
                {
                    yield return new Finding(record.EmpireId, null, "flagged eliminated but still has " + record.Planets + " planets and " + (record.UnarmedShips + record.EscortShips + record.CapitalShips) + " ships");
                }
            }

            if (c.State.ScoreHistory.Count > 0)
            {
                IReadOnlyList<ScoreRecord> history = c.State.ScoreHistory.For(c.State.TurnYear);
                if (history == null || history.Count != c.State.AllEmpires.Count)
                {
                    yield return new Finding(0, "score history", "year " + c.State.TurnYear + " has " + (history == null ? 0 : history.Count) + " records for " + c.State.AllEmpires.Count + " empires");
                }
            }
        }

        private static IEnumerable<Finding> SaveRoundTrip(InvariantContext c)
        {
            if (c.Turn.RoundTripChecked && c.Turn.RoundTripDifference != null)
            {
                yield return new Finding(0, "saved state", "reload + re-save differs: " + c.Turn.RoundTripDifference);
            }
        }

        private static IEnumerable<Finding> MessageCountBounded(InvariantContext c)
        {
            List<Message> messages = c.Turn.Generation.Messages;
            int everyone = messages.Count(m => m.Audience == Global.Everyone);
            foreach (EmpireData empire in c.State.AllEmpires.Values)
            {
                int count = everyone + messages.Count(m => m.Audience == empire.Id);
                if (count > c.Limits.MaxMessagesPerEmpirePerTurn)
                {
                    string common = string.Join(" | ", messages.Where(m => m.Audience == empire.Id || m.Audience == Global.Everyone)
                        .GroupBy(m => Prefix(m.Text)).OrderByDescending(g => g.Count()).Take(3).Select(g => g.Count() + "x '" + g.Key + "'"));
                    yield return new Finding(empire.Id, null, count + " messages this turn (limit " + c.Limits.MaxMessagesPerEmpirePerTurn + "): " + common);
                }
            }
        }

        private static IEnumerable<Finding> TurnTimeBounded(InvariantContext c)
        {
            if (c.Turn.TurnSeconds > c.Limits.MaxTurnSeconds)
            {
                yield return new Finding(0, null, "turn took " + c.Turn.TurnSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s (limit " + c.Limits.MaxTurnSeconds + " s)");
            }
        }

        // ---------------------------------------------------------------------------- helpers

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string FleetName(Fleet fleet)
        {
            return "fleet '" + fleet.Name + "' (" + fleet.Key.ToString("X", CultureInfo.InvariantCulture) + ")";
        }

        private static string Point(NovaPoint point)
        {
            return point == null ? "(null)" : "(" + point.X + "," + point.Y + ")";
        }

        private static string OneLine(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            string flat = text.Replace("\r", " ").Replace("\n", " ");
            return flat.Length > 400 ? flat.Substring(0, 400) + "..." : flat;
        }

        private static string Prefix(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length > 60 ? text.Substring(0, 60) : text;
        }
    }
}
