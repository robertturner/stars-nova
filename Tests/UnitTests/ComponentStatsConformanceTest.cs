namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using static Nova.Common.TechLevel;

    /// <summary>
    /// Data conformance: every record of behavior-specs-10/component-stats.tsv (the component data
    /// block read out of the executable, ship-design-and-components.md section 15) against the
    /// component of the same name in components.xml, field by field for every field the tsv
    /// carries and Nova stores. Coverage rows (ship-design-and-components.md): 2, 5, 6, 16, 17, 21,
    /// 25-36, 38.
    /// </summary>
    /// <remarks>
    /// Field mapping (section 15b/15c/15e), written here independently of the loaders:
    /// - tech E W P C El B, mass, res/iron/bor/germ: Component.RequiredTech, Mass, Cost.
    /// - engines: fuelW1..fuelW10 = Engine.FuelConsumption[0..9] (Nova stores warp 1-10; warp 0
    ///   is always 0 in the tsv).
    /// - ship scanners: scanRange = NormalScan; penClass 0 = no penetrating capability.
    /// - shields / armor: the Shield / Armor property value.
    /// - beams / torpedoes: range, damage (Weapon.Power), initiative; torpedo accuracyPct;
    ///   beam fireMode 0 standard, 1 shield sapper, 2 hits all targets (gatling).
    /// - bombs: popKill in tenths of a percent (Bomb.PopKill is a percent), installations.
    /// - mining robots: miningRate; mine layers: minesPerYear_x10 x 10 = LayerRate.
    /// - gates: maxMass / maxRange, 65535 = unlimited (Nova -1); drivers: warp = Mass Driver value.
    /// - electrical (the family's single stat): cloak points (idx 0-4), computer accuracy %
    ///   (5-7), jammer deflection % (8-11), capacitor % (12-13).
    /// - mechanical: thrusters' battle movement in quarter squares, the deflector's %.
    /// - terraforming: maxTerraform = the +-N in the item's name.
    /// - planetary scanners: signed range, negative = penetrating with the magnitude as range.
    /// - hulls/chassis: fuel, base armor, cargo (ship hulls: BaseCargo; chassis: the dock
    ///   capacity, 65535 = unlimited), and the slot list (mask, capacity) as a multiset.
    /// Not compared (unit or meaning not pinned by the spec): planetary defence coverage
    /// (section 15c says hundredths of a percent, turn-generation-engine.md section 11 says
    /// tenths), the Energy Dampener / Tachyon Detector / Anti-matter Generator stat, and the
    /// mechanical sub-family codes of the pods, tanks and modules.
    /// </remarks>
    [TestFixture]
    public class ComponentStatsConformanceTest
    {
        /// <summary>One tsv record: its category header, column names and values.</summary>
        public sealed class TsvRecord
        {
            public string Category;
            public string Name;
            public Dictionary<string, string> Fields = new Dictionary<string, string>();

            public int Int(string column)
            {
                return int.Parse(Fields[column], CultureInfo.InvariantCulture);
            }

            public override string ToString()
            {
                return Category + " " + Name;
            }
        }

        /// <summary>tsv spellings that components.xml spells differently (cosmetic; reported).</summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "Robber Baron Scanner", "Robber Barron Scanner" },
            { "Gorilla Delagator", "Gorilla Delegator" },
            { "Gatling Neutrino Cannon", "Gatling Neutrino Cannnon" },
            { "Armageddon Missile", "Armegeddon Missile" },
            { "Super-Fuel Xport", "Super-Fuel Transport" },
        };

        private static List<TsvRecord> records;
        private static AllComponents components;

        /// <summary>The spec table, found above the test directory or, failing that, above this
        /// source file.</summary>
        private static string TsvPath([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
        {
            foreach (string start in new[] { TestContext.CurrentContext.TestDirectory, Path.GetDirectoryName(sourceFile) })
            {
                DirectoryInfo dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "docs", "behavior-specs-10", "component-stats.tsv");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    dir = dir.Parent;
                }
            }

            throw new FileNotFoundException("docs/behavior-specs-10/component-stats.tsv not found above the test directory");
        }

        public static List<TsvRecord> Records()
        {
            if (records != null)
            {
                return records;
            }

            List<TsvRecord> result = new List<TsvRecord>();
            string category = null;
            string[] header = null;
            foreach (string raw in File.ReadAllLines(TsvPath()))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("## category", StringComparison.Ordinal))
                {
                    category = line.Split(' ')[2];
                    header = null;
                    continue;
                }

                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] cells = line.Split('\t');
                if (cells[0] == "idx")
                {
                    header = cells;
                    continue;
                }

                TsvRecord record = new TsvRecord { Category = category, Name = cells[1] };
                for (int i = 0; i < header.Length && i < cells.Length; i++)
                {
                    record.Fields[header[i]] = cells[i];
                }

                result.Add(record);
            }

            records = result;
            return records;
        }

        private static IEnumerable<TestCaseData> AllRecords()
        {
            return Records().Select(r => new TestCaseData(r).SetName("{m}(" + r.Category + " " + r.Name + ")"));
        }

        private static IEnumerable<TestCaseData> HullRecords()
        {
            return Records().Where(r => r.Category == "0x4000" || r.Category == "0x0400")
                .Select(r => new TestCaseData(r).SetName("{m}(" + r.Name + ")"));
        }

        private static AllComponents Components()
        {
            return components ?? (components = new AllComponents());
        }

        private static string Normalise(string name)
        {
            return Regex.Replace(name.ToLowerInvariant().Replace(" terraform ", " "), "[^a-z0-9±/']", string.Empty);
        }

        /// <summary>The components.xml component for a tsv name: exact, alias, then the same name
        /// ignoring case, spacing, hyphens and the word "Terraform".</summary>
        private static Component Find(string tsvName)
        {
            IDictionary<string, Component> all = Components().GetAll;
            if (all.TryGetValue(tsvName, out Component exact))
            {
                return exact;
            }

            if (Aliases.TryGetValue(tsvName, out string alias) && all.TryGetValue(alias, out Component aliased))
            {
                return aliased;
            }

            string wanted = Normalise(tsvName);
            return all.Values.FirstOrDefault(c => Normalise(c.Name) == wanted);
        }

        private static Component Require(TsvRecord record)
        {
            Component component = Find(record.Name);
            Assert.NotNull(component, $"components.xml has no component for tsv record '{record}'");
            return component;
        }

        private static T Property<T>(Component component, string key) where T : ComponentProperty
        {
            return component.Properties.TryGetValue(key, out ComponentProperty property) ? property as T : null;
        }

        // ---- Presence and identity (rows 5, 16, 25-38) ----

        [Test]
        public void TheTsvHas239Records_AndEveryOneMapsToADistinctComponent()
        {
            List<TsvRecord> all = Records();
            Assert.AreEqual(239, all.Count, "section 15: 16 categories, 239 records");

            HashSet<string> matched = new HashSet<string>();
            foreach (TsvRecord record in all)
            {
                Component component = Find(record.Name);
                Assert.NotNull(component, $"missing: {record}");
                Assert.IsTrue(matched.Add(component.Name), $"two tsv records map to '{component.Name}'");
            }

            Assert.AreEqual(all.Count, Components().GetAll.Count, "components.xml should hold exactly the 239 tsv components");
        }

        [TestCase("0x0001", ItemType.Engine, 16)]
        [TestCase("0x0002", ItemType.Scanner, 16)]
        [TestCase("0x0004", ItemType.Shield, 10)]
        [TestCase("0x0008", ItemType.Armor, 12)]
        [TestCase("0x0010", ItemType.BeamWeapons, 24)]
        [TestCase("0x0020", ItemType.Torpedoes, 12)]
        [TestCase("0x0040", ItemType.Bomb, 15)]
        [TestCase("0x0080", ItemType.MiningRobot, 8)]
        [TestCase("0x0100", ItemType.MineLayer, 10)]
        [TestCase("0x0200", ItemType.Orbital, 16)]
        [TestCase("0x0400", ItemType.Hull, 5)]
        [TestCase("0x0800", ItemType.Electrical, 17)]
        [TestCase("0x1000", ItemType.Mechanical, 11)]
        [TestCase("0x2000", ItemType.Terraforming, 20)]
        [TestCase("0x4000", ItemType.Hull, 32)]
        [TestCase("0x8000", ItemType.PlanetaryInstallations, 15)]
        public void EachCategoryMapsToOneNovaComponentType(string category, ItemType type, int count)
        {
            List<TsvRecord> inCategory = Records().Where(r => r.Category == category).ToList();
            Assert.AreEqual(count, inCategory.Count, $"tsv count for {category}");
            foreach (TsvRecord record in inCategory)
            {
                Assert.AreEqual(type, Require(record).Type, record.ToString());
            }
        }

        /// <summary>Row 16 (section 13): ship hulls and starbase chassis share one hull table; the
        /// five chassis are the starbases.</summary>
        [Test]
        public void HullsAndChassisShareOneTable_TheFiveChassisAreTheStarbases()
        {
            foreach (TsvRecord record in Records().Where(r => r.Category == "0x4000" || r.Category == "0x0400"))
            {
                Hull hull = Property<Hull>(Require(record), "Hull");
                Assert.NotNull(hull, record.ToString());
                Assert.AreEqual(record.Category == "0x0400", hull.IsStarbase, record.ToString());
            }
        }

        // ---- Universal record layout (row 17): tech, mass, cost ----

        [TestCaseSource(nameof(AllRecords))]
        public void TechMassAndCost(TsvRecord record)
        {
            Component c = Require(record);
            List<string> wrong = new List<string>();
            void Check(string field, int expected, int actual)
            {
                if (expected != actual)
                {
                    wrong.Add($"{field}: tsv {expected}, xml {actual}");
                }
            }

            Check("Energy tech", record.Int("E"), c.RequiredTech[ResearchField.Energy]);
            Check("Weapons tech", record.Int("W"), c.RequiredTech[ResearchField.Weapons]);
            Check("Propulsion tech", record.Int("P"), c.RequiredTech[ResearchField.Propulsion]);
            Check("Construction tech", record.Int("C"), c.RequiredTech[ResearchField.Construction]);
            Check("Electronics tech", record.Int("El"), c.RequiredTech[ResearchField.Electronics]);
            Check("Biotechnology tech", record.Int("B"), c.RequiredTech[ResearchField.Biotechnology]);
            Check("mass", record.Int("mass"), c.Mass);
            Check("resources", record.Int("res"), c.Cost.Energy);
            Check("ironium", record.Int("iron"), c.Cost.Ironium);
            Check("boranium", record.Int("bor"), c.Cost.Boranium);
            Check("germanium", record.Int("germ"), c.Cost.Germanium);

            Assert.IsEmpty(wrong, record + ": " + string.Join("; ", wrong));
        }

        // ---- Category-specific stats (section 15c; rows 26-38) ----

        [TestCaseSource(nameof(AllRecords))]
        public void CategoryStats(TsvRecord record)
        {
            Component c = Require(record);
            int idx = record.Int("idx");
            switch (record.Category)
            {
                case "0x0001":
                    {
                        Engine engine = Property<Engine>(c, "Engine");
                        Assert.NotNull(engine);
                        Assert.AreEqual(0, record.Int("fuelW0"), "warp 0 burns nothing");
                        for (int warp = 1; warp <= 10; warp++)
                        {
                            Assert.AreEqual(record.Int("fuelW" + warp), engine.FuelConsumption[warp - 1], $"fuel at warp {warp}");
                        }

                        break;
                    }

                case "0x0002":
                    {
                        Scanner scanner = Property<Scanner>(c, "Scanner");
                        Assert.NotNull(scanner);
                        Assert.AreEqual(record.Int("scanRange"), scanner.NormalScan, "scan range");
                        int penClass = record.Int("penClass");
                        if (penClass == 0)
                        {
                            Assert.AreEqual(0, scanner.PenetratingScan, "penetrating class 0 = none");
                        }
                        else if (record.Name != "Pick Pocket Scanner" && record.Name != "Robber Baron Scanner")
                        {
                            // The two theft scanners' class bypasses the range math (section 15c);
                            // what they penetrate is not pinned, so only the others are checked.
                            Assert.Greater(scanner.PenetratingScan, 0, "a non-zero class penetrates");
                        }

                        break;
                    }

                case "0x0004":
                    Assert.AreEqual(record.Int("shield"), Property<IntegerProperty>(c, "Shield")?.Value, "shield points");
                    break;

                case "0x0008":
                    Assert.AreEqual(record.Int("armor"), Property<IntegerProperty>(c, "Armor")?.Value, "armor points");
                    break;

                case "0x0010":
                case "0x0020":
                    {
                        Weapon weapon = Property<Weapon>(c, "Weapon");
                        Assert.NotNull(weapon);
                        Assert.AreEqual(record.Int("range"), weapon.Range, "range");
                        Assert.AreEqual(record.Int("damage"), weapon.Power, "damage");
                        Assert.AreEqual(record.Int("initiative"), weapon.Initiative, "initiative");
                        if (record.Category == "0x0020")
                        {
                            Assert.AreEqual(record.Int("accuracyPct"), weapon.Accuracy, "base accuracy %");
                            Assert.That(weapon.Group, Is.EqualTo(WeaponType.torpedo).Or.EqualTo(WeaponType.missile));
                        }
                        else
                        {
                            WeaponType[] byFireMode = { WeaponType.standardBeam, WeaponType.shieldSapper, WeaponType.gatlingGun };
                            Assert.AreEqual(byFireMode[record.Int("fireMode")], weapon.Group, "fire mode");
                        }

                        break;
                    }

                case "0x0040":
                    {
                        Bomb bomb = Property<Bomb>(c, "Bomb");
                        double popKill = bomb == null ? 0 : bomb.PopKill;
                        int installations = bomb == null ? 0 : bomb.Installations;
                        Assert.AreEqual(record.Int("popKill_x0.1pct"), popKill * 10, 1e-9, "population kill, tenths of a percent");
                        Assert.AreEqual(record.Int("installations"), installations, "installations per bomb");
                        break;
                    }

                case "0x0080":
                    Assert.AreEqual(record.Int("miningRate"), Property<IntegerProperty>(c, "Mining Robot")?.Value ?? 0, "mining rate");
                    break;

                case "0x0100":
                    Assert.AreEqual(record.Int("minesPerYear_x10") * 10, Property<MineLayer>(c, "Mine Layer")?.LayerRate, "mines per year");
                    break;

                case "0x0200":
                    {
                        Gate gate = Property<Gate>(c, "Gate");
                        if (gate != null)
                        {
                            Assert.AreEqual(record.Int("maxMassOrWarp"), gate.SafeHullMass < 0 ? 65535 : gate.SafeHullMass, "gate mass limit");
                            Assert.AreEqual(record.Int("maxRangeLy"), gate.SafeRange < 0 ? 65535 : gate.SafeRange, "gate range");
                        }
                        else
                        {
                            Assert.AreEqual(0, record.Int("maxRangeLy"), "a driver has no range");
                            Assert.AreEqual(record.Int("maxMassOrWarp"), Property<MassDriver>(c, "Mass Driver")?.Value, "driver warp");
                        }

                        break;
                    }

                case "0x0800":
                    {
                        int stat = record.Int("stat");
                        if (idx <= 4)
                        {
                            Assert.AreEqual(stat, Property<ProbabilityProperty>(c, "Cloak")?.Value, "cloak points");
                        }
                        else if (idx <= 7)
                        {
                            Assert.AreEqual(stat, Property<Computer>(c, "Computer")?.Accuracy, "computer accuracy bonus %");
                        }
                        else if (idx <= 11)
                        {
                            Assert.AreEqual(stat, Property<ProbabilityProperty>(c, "Jammer")?.Value, "jammer deflection %");
                        }
                        else if (idx <= 13)
                        {
                            Assert.AreEqual(stat, Property<CapacitorProperty>(c, "Capacitor")?.Value, "capacitor %");
                        }

                        break;
                    }

                case "0x1000":
                    if (record.Name == "Maneuvering Jet" || record.Name == "Overthruster")
                    {
                        Assert.AreEqual(record.Int("stat") * 0.25, Property<DoubleProperty>(c, "Battle Movement")?.Value, "battle movement, quarter squares");
                    }
                    else if (record.Name == "Beam Deflector")
                    {
                        Assert.AreEqual(record.Int("stat"), Property<ProbabilityProperty>(c, "Beam Deflector")?.Value, "deflection %");
                    }

                    break;

                case "0x2000":
                    {
                        Match match = Regex.Match(c.Name, "±(\\d+)$");
                        Assert.IsTrue(match.Success, c.Name);
                        Assert.AreEqual(record.Int("maxTerraform"), int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), "maximum terraform");
                        break;
                    }

                case "0x8000":
                    if (idx <= 8)
                    {
                        int signed = record.Int("scanRange_negIfPenetrating");
                        if (signed > 32767)
                        {
                            signed -= 65536;
                        }

                        Scanner scanner = Property<Scanner>(c, "Scanner");
                        Assert.NotNull(scanner);
                        Assert.AreEqual(Math.Abs(signed), scanner.NormalScan, "planetary scan range");
                        Assert.AreEqual(signed < 0 ? Math.Abs(signed) : 0, scanner.PenetratingScan, "negative = penetrating at the same range");
                    }

                    break;

                case "0x0400":
                case "0x4000":
                    {
                        Hull hull = Property<Hull>(c, "Hull");
                        Assert.NotNull(hull);
                        Assert.AreEqual(record.Int("fuel"), hull.FuelCapacity, "fuel capacity");
                        Assert.AreEqual(record.Int("armor"), hull.ArmorStrength, "base armor");
                        int cargo = record.Int("cargo");
                        if (record.Category == "0x4000")
                        {
                            Assert.AreEqual(cargo, hull.BaseCargo, "cargo capacity");
                        }
                        else if (cargo == 65535)
                        {
                            // 0xFFFF = unlimited; Nova's sentinel for an unlimited dock is 10,000 kT
                            // (Manufacture.UnlimitedDockCapacity).
                            Assert.GreaterOrEqual(hull.DockCapacity, 10000, "unlimited dock");
                        }
                        else
                        {
                            Assert.AreEqual(cargo, hull.DockCapacity, "dock capacity");
                        }

                        break;
                    }
            }
        }

        // ---- Hull slot layouts (rows 2, 6, 21; section 15e) ----

        [Flags]
        private enum Slot
        {
            None = 0,
            Eng = 1 << 0,
            Scan = 1 << 1,
            Shld = 1 << 2,
            Arm = 1 << 3,
            Beam = 1 << 4,
            Torp = 1 << 5,
            Bomb = 1 << 6,
            Mine = 1 << 7,
            MLay = 1 << 8,
            Orb = 1 << 9,
            Elec = 1 << 10,
            Mech = 1 << 11,
        }

        /// <summary>The spec's general-purpose slot: the union of the eight ship-mountable families
        /// (section 15e).</summary>
        private const Slot GeneralPurpose = Slot.Scan | Slot.Shld | Slot.Arm | Slot.Beam | Slot.Torp | Slot.MLay | Slot.Elec | Slot.Mech;

        /// <summary>What each components.xml module type string accepts, on the spec's category
        /// bits. "Base Cargo" and "Space Dock" are display pseudo-modules that take no part.</summary>
        private static readonly Dictionary<string, Slot> ModuleTypes = new Dictionary<string, Slot>
        {
            { "Engine", Slot.Eng },
            { "Scanner", Slot.Scan },
            { "Shield", Slot.Shld },
            { "Armor", Slot.Arm },
            { "Weapon", Slot.Beam | Slot.Torp },
            { "Bomb", Slot.Bomb },
            { "Mining Robot", Slot.Mine },
            { "Mine Layer", Slot.MLay },
            { "Electrical", Slot.Elec },
            { "Mechanical", Slot.Mech },
            { "Shield or Armor", Slot.Shld | Slot.Arm },
            { "General Purpose", GeneralPurpose },
            { "Scanner Electrical Mechanical", Slot.Scan | Slot.Elec | Slot.Mech },
            { "Shield Electrical Mechanical", Slot.Shld | Slot.Elec | Slot.Mech },
            { "Mine Layer Electrical Mechanical", Slot.MLay | Slot.Elec | Slot.Mech },
            { "Armor Scanner Elect Mech", Slot.Arm | Slot.Scan | Slot.Elec | Slot.Mech },
            { "Electrical or Mechanical", Slot.Elec | Slot.Mech },
            { "Weapon or Shield", Slot.Shld | Slot.Beam | Slot.Torp },
            { "Orbital or Electrical", Slot.Orb | Slot.Elec },
        };

        private static readonly HashSet<string> PseudoModules = new HashSet<string> { "Base Cargo", "Space Dock" };

        private static Slot ParseMask(string tsvMask)
        {
            Slot mask = Slot.None;
            foreach (string part in tsvMask.Split('+'))
            {
                mask |= (Slot)Enum.Parse(typeof(Slot), part);
            }

            return mask;
        }

        [TestCaseSource(nameof(HullRecords))]
        public void HullSlotLayout(TsvRecord record)
        {
            Hull hull = Property<Hull>(Require(record), "Hull");
            Assert.NotNull(hull);

            List<string> expected = new List<string>();
            foreach (string token in record.Fields["slots"].Split(' '))
            {
                int x = token.LastIndexOf('x');
                expected.Add(ParseMask(token.Substring(0, x)) + " x" + token.Substring(x + 1));
            }

            List<string> actual = new List<string>();
            foreach (HullModule module in hull.Modules)
            {
                if (PseudoModules.Contains(module.ComponentType))
                {
                    continue;
                }

                Assert.IsTrue(ModuleTypes.TryGetValue(module.ComponentType, out Slot mask), $"unknown module type '{module.ComponentType}'");
                actual.Add(mask + " x" + module.ComponentMaximum.ToString(CultureInfo.InvariantCulture));
            }

            Assert.AreEqual(record.Int("nslots"), expected.Count, "tsv self-check: slot count matches the list");
            Assert.AreEqual(record.Int("nslots"), actual.Count, "slot count");
            expected.Sort(StringComparer.Ordinal);
            actual.Sort(StringComparer.Ordinal);
            CollectionAssert.AreEqual(expected, actual, "slot (allowed categories, capacity) multiset; components.xml orders modules by designer cell, so order is not compared");
        }

        /// <summary>Section 15e: slot counts run from 2 to 16 and are per-hull data; the spec's
        /// named examples.</summary>
        [TestCase("Scout", 3)]
        [TestCase("Destroyer", 7)]
        [TestCase("Battleship", 11)]
        [TestCase("Nubian", 13)]
        [TestCase("Meta Morph", 7)]
        [TestCase("Colony Ship", 2)]
        [TestCase("Ultra Station", 16)]
        [TestCase("Death Star", 16)]
        public void SlotCountsOfTheNamedExamples(string hullName, int slots)
        {
            Hull hull = Property<Hull>(Find(hullName), "Hull");
            Assert.AreEqual(slots, hull.Modules.Count(m => !PseudoModules.Contains(m.ComponentType)));
        }

        [Test]
        public void SlotCountsRangeFromTwoToSixteen()
        {
            List<int> counts = Records().Where(r => r.Category == "0x4000" || r.Category == "0x0400")
                .Select(r => Property<Hull>(Require(r), "Hull").Modules.Count(m => !PseudoModules.Contains(m.ComponentType)))
                .ToList();
            Assert.AreEqual(2, counts.Min());
            Assert.AreEqual(16, counts.Max());
        }
    }
}
