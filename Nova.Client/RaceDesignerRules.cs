namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// The race designer's sections, in the order Nova's designer shows them. The spec's six
    /// wizard stages (race-designer-ui-and-availability.md "Identity and archetype stage") map
    /// onto these: Identity/Presets, Environment-and-growth, Economy (Production), PRT (part of
    /// Identity here), LRTs (Traits), Research; the leftover-points choice, which the spec puts on
    /// the Identity stage, has its own section in Nova.
    /// </summary>
    public enum RaceDraftSection
    {
        Identity,
        Traits,
        Environment,
        Production,
        Research,
        LeftoverPoints,
    }

    /// <summary>
    /// Pure race-designer rules (behavior-specs-10/race-designer-ui-and-availability.md):
    /// the seven economic slots with their clamp tables, the stepper rule, the Alternate Reality
    /// locks, and the draft-local working copy (copy a whole race, or one section of it).
    /// </summary>
    public static class RaceDesignerRules
    {
        /// <summary>The Alternate Reality primary-trait code.</summary>
        public const string AlternateReality = "AR";

        /// <summary>The Jack of All Trades primary-trait code.</summary>
        public const string JackOfAllTrades = "JOAT";

        /// <summary>The Germanium-discount checkbox ("factories cost 1 kT less Germanium").</summary>
        public const string CheapFactories = "CF";

        /// <summary>The "expensive research starts at tech 3 (4 for JOAT)" checkbox.</summary>
        public const string ExtraTech = "ExtraTech";

        /// <summary>
        /// The 14 lesser racial traits in wizard checkbox order (= trait bit 0-13), spec
        /// "Identity and archetype stage", stage-5 bullet.
        /// </summary>
        public static readonly string[] LesserTraitOrder =
        {
            "IFE", "TT", "ARM", "ISB", "GR", "UR", "MA", "NRS", "CE", "OBRM", "NAS", "LSP", "BET", "RS"
        };

        /// <summary>The ten primary racial traits by PRT index 0-9 (HE 0 ... JOAT 9).</summary>
        public static readonly string[] PrimaryTraitOrder =
        {
            "HE", "SS", "WM", "CA", "IS", "SD", "PP", "IT", "AR", "JOAT"
        };

        /// <summary>
        /// Economic slots 0-6 ("Economic-settings stage" table): minimum, maximum and the
        /// Humanoid default, in stored units (slot 0 is shown x 100 colonists).
        /// </summary>
        public static readonly (int Min, int Max, int Default)[] EconomySlots =
        {
            (7, 25, 10), // 0 colonists per resource (x 100)
            (5, 15, 10), // 1 resources per 10 factories
            (5, 25, 10), // 2 resources per factory
            (5, 25, 10), // 3 factories per 10,000 colonists
            (5, 25, 10), // 4 kT per 10 mines
            (2, 15, 5),  // 5 resources per mine
            (5, 25, 10), // 6 mines per 10,000 colonists
        };

        /// <summary>Slot 7, the leftover-points choice: min 0, max 6 (5 and 6 have no label and act as Surface minerals).</summary>
        public const int LeftoverSlotMaximum = 6;

        /// <summary>The five labelled leftover-point choices (slot 7 values 0-4), in the spelling the home-world adjuster reads.</summary>
        public static readonly string[] LeftoverPointTargets =
        {
            "Surface minerals", "Mineral concentration", "Mines", "Factories", "Defenses"
        };

        /// <summary>Research cost class radio values (slots 8-13): 0 = 75% extra, 1 = standard, 2 = 50% less, as Nova's percent.</summary>
        public static readonly int[] ResearchCostByClass = { 175, 100, 50 };

        /// <summary>The six research fields in the spec's slot order (Energy, Weapons, Propulsion, Construction, Electronics, Biotechnology).</summary>
        public static readonly TechLevel.ResearchField[] ResearchSlotOrder =
        {
            TechLevel.ResearchField.Energy, TechLevel.ResearchField.Weapons, TechLevel.ResearchField.Propulsion,
            TechLevel.ResearchField.Construction, TechLevel.ResearchField.Electronics, TechLevel.ResearchField.Biotechnology
        };

        /// <summary>
        /// The shared clamp step: the candidate is raised to the slot's minimum, then lowered to
        /// its maximum (spec: "raised to the slot's minimum, then lowered to the slot's maximum").
        /// </summary>
        public static int ClampSlot(int slot, int value)
        {
            (int min, int max, int _) = EconomySlots[slot];
            value = Math.Max(min, value);
            value = Math.Min(max, value);
            return value;
        }

        /// <summary>
        /// One stepper click ("Stepping behavior"): 1 per click, 3 while Shift is held, in the
        /// given direction (+1 / -1), through the clamp. Auto-repeat simply calls this again.
        /// </summary>
        public static int Step(int slot, int current, int direction, bool shiftHeld)
        {
            int amount = shiftHeld ? 3 : 1;
            return ClampSlot(slot, current + (Math.Sign(direction) * amount));
        }

        /// <summary>A race's economic slot 0-6 in stored (slot) units.</summary>
        public static int GetSlot(Race race, int slot)
        {
            switch (slot)
            {
                case 0: return race.ColonistsPerResource / 100;
                case 1: return race.FactoryProduction;
                case 2: return race.FactoryBuildCost;
                case 3: return race.OperableFactories;
                case 4: return race.MineProductionRate;
                case 5: return race.MineBuildCost;
                case 6: return race.OperableMines;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        /// <summary>Stores a race's economic slot 0-6 (through the clamp; slot 0 is kept x 100).</summary>
        public static void SetSlot(Race race, int slot, int value)
        {
            value = ClampSlot(slot, value);
            switch (slot)
            {
                case 0: race.ColonistsPerResource = value * 100; break;
                case 1: race.FactoryProduction = value; break;
                case 2: race.FactoryBuildCost = value; break;
                case 3: race.OperableFactories = value; break;
                case 4: race.MineProductionRate = value; break;
                case 5: race.MineBuildCost = value; break;
                case 6: race.OperableMines = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        /// <summary>
        /// "Earlier selections lock later controls": for an Alternate Reality race rows 2-7 (slots
        /// 1-6) are drawn disabled with their step buttons disabled ("Alternate Reality variant");
        /// row 1 stays editable as AR's income divisor. In a non-editable context nothing is.
        /// </summary>
        public static bool IsEconomySlotEditable(Race race, int slot, bool editable = true)
        {
            if (!editable)
            {
                return false;
            }

            return slot == 0 || !IsAlternateReality(race);
        }

        public static bool IsAlternateReality(Race race)
        {
            return race.Traits.Primary is not null && race.Traits.Primary.Code == AlternateReality;
        }

        /// <summary>
        /// Selecting a primary trait. When the PRT stage selects Alternate Reality, slots 1-6 are
        /// reset to exactly 10, 10, 10, 10, 5, 10 and the Germanium-discount bit is cleared
        /// (spec "Defaults", lines 93759-93766). Other PRTs change nothing else.
        /// </summary>
        public static void ApplyPrimaryTrait(Race race, string code)
        {
            race.Traits.SetPrimary(code);
            if (code == AlternateReality)
            {
                for (int slot = 1; slot <= 6; slot++)
                {
                    SetSlot(race, slot, EconomySlots[slot].Default);
                }

                if (race.Traits.Contains(CheapFactories))
                {
                    race.Traits.Remove(CheapFactories);
                }
            }
        }

        /// <summary>
        /// The starting tech level the research stage's checkbox names: 4 for Jack of All Trades,
        /// otherwise 3 (spec: the control is labelled "starts at tech level 3" or "...4" by
        /// whether the PRT is JOAT).
        /// </summary>
        public static int ExtraTechStartLevel(Race race)
        {
            return race.Traits.Primary is not null && race.Traits.Primary.Code == JackOfAllTrades ? 4 : 3;
        }

        /// <summary>Whether the race carries a lesser trait (or one of the two flat-cost checkboxes).</summary>
        public static bool HasLesserTrait(Race race, string code)
        {
            return race.Traits.Contains(code) && (race.Traits.Primary is null || race.Traits.Primary.Code != code);
        }

        /// <summary>Turns a lesser trait (or CF / ExtraTech) on or off.</summary>
        public static void SetLesserTrait(Race race, string code, bool on)
        {
            bool has = HasLesserTrait(race, code);
            if (on && !has)
            {
                race.Traits.Add(code);
            }
            else if (!on && has)
            {
                race.Traits.Remove(code);
            }
        }

        /// <summary>A fresh race holding a field-by-field copy of <paramref name="source"/> (the draft-local working copy).</summary>
        public static Race CopyOf(Race source)
        {
            Race copy = new Race();
            CopyAll(source, copy);
            return copy;
        }

        /// <summary>Copies every design field of <paramref name="source"/> onto <paramref name="target"/> (accept = commit the whole draft).</summary>
        public static void CopyAll(Race source, Race target)
        {
            foreach (RaceDraftSection section in Enum.GetValues(typeof(RaceDraftSection)))
            {
                CopySection(source, target, section);
            }

            target.Password = source.Password;
        }

        /// <summary>
        /// Copies one section's fields (race-designer-ui-and-availability.md "Cross-stage
        /// persistence": each stage's accept writes only that stage's fields; its cancel restores
        /// them). Identity is the names, emblem and primary trait; Traits the 14 LRTs; Environment
        /// the three axes and the growth rate; Production the seven economic slots and the
        /// Germanium checkbox; Research the six cost classes and the tech-3 checkbox;
        /// LeftoverPoints the leftover-points choice.
        /// </summary>
        public static void CopySection(Race source, Race target, RaceDraftSection section)
        {
            switch (section)
            {
                case RaceDraftSection.Identity:
                    target.Name = source.Name;
                    target.PluralName = source.PluralName;
                    target.Icon = source.Icon;
                    if (source.Traits.Primary is not null)
                    {
                        target.Traits.SetPrimary(source.Traits.Primary);
                    }

                    break;

                case RaceDraftSection.Traits:
                    foreach (string code in LesserTraitOrder)
                    {
                        SetLesserTrait(target, code, HasLesserTrait(source, code));
                    }

                    break;

                case RaceDraftSection.Environment:
                    CopyTolerance(source.GravityTolerance, target.GravityTolerance);
                    CopyTolerance(source.TemperatureTolerance, target.TemperatureTolerance);
                    CopyTolerance(source.RadiationTolerance, target.RadiationTolerance);
                    target.GrowthRate = source.GrowthRate;
                    break;

                case RaceDraftSection.Production:
                    target.ColonistsPerResource = source.ColonistsPerResource;
                    target.FactoryProduction = source.FactoryProduction;
                    target.FactoryBuildCost = source.FactoryBuildCost;
                    target.OperableFactories = source.OperableFactories;
                    target.MineProductionRate = source.MineProductionRate;
                    target.MineBuildCost = source.MineBuildCost;
                    target.OperableMines = source.OperableMines;
                    SetLesserTrait(target, CheapFactories, HasLesserTrait(source, CheapFactories));
                    break;

                case RaceDraftSection.Research:
                    foreach (TechLevel.ResearchField field in ResearchSlotOrder)
                    {
                        target.ResearchCosts[field] = source.ResearchCosts[field];
                    }

                    SetLesserTrait(target, ExtraTech, HasLesserTrait(source, ExtraTech));
                    break;

                case RaceDraftSection.LeftoverPoints:
                    target.LeftoverPointTarget = source.LeftoverPointTarget;
                    break;
            }
        }

        /// <summary>The sections whose fields differ between two races (for "modified" displays and tests).</summary>
        public static IEnumerable<RaceDraftSection> ChangedSections(Race a, Race b)
        {
            foreach (RaceDraftSection section in Enum.GetValues(typeof(RaceDraftSection)))
            {
                Race probe = CopyOf(a);
                CopySection(b, probe, section);
                if (!SameSection(a, probe, section))
                {
                    yield return section;
                }
            }
        }

        private static bool SameSection(Race a, Race b, RaceDraftSection section)
        {
            switch (section)
            {
                case RaceDraftSection.Identity:
                    return a.Name == b.Name && a.PluralName == b.PluralName
                        && (a.Icon?.Source == b.Icon?.Source)
                        && a.Traits.Primary?.Code == b.Traits.Primary?.Code;
                case RaceDraftSection.Traits:
                    return LesserTraitOrder.All(code => HasLesserTrait(a, code) == HasLesserTrait(b, code));
                case RaceDraftSection.Environment:
                    return SameTolerance(a.GravityTolerance, b.GravityTolerance)
                        && SameTolerance(a.TemperatureTolerance, b.TemperatureTolerance)
                        && SameTolerance(a.RadiationTolerance, b.RadiationTolerance)
                        && a.GrowthRate == b.GrowthRate;
                case RaceDraftSection.Production:
                    return Enumerable.Range(0, 7).All(slot => GetSlot(a, slot) == GetSlot(b, slot))
                        && a.ColonistsPerResource == b.ColonistsPerResource
                        && HasLesserTrait(a, CheapFactories) == HasLesserTrait(b, CheapFactories);
                case RaceDraftSection.Research:
                    return ResearchSlotOrder.All(field => a.ResearchCosts[field] == b.ResearchCosts[field])
                        && HasLesserTrait(a, ExtraTech) == HasLesserTrait(b, ExtraTech);
                default:
                    return a.LeftoverPointTarget == b.LeftoverPointTarget;
            }
        }

        private static void CopyTolerance(EnvironmentTolerance source, EnvironmentTolerance target)
        {
            target.Immune = source.Immune;
            target.MinimumValue = source.MinimumValue;
            target.MaximumValue = source.MaximumValue;
        }

        private static bool SameTolerance(EnvironmentTolerance a, EnvironmentTolerance b)
        {
            return a.Immune == b.Immune && a.MinimumValue == b.MinimumValue && a.MaximumValue == b.MaximumValue;
        }
    }
}
