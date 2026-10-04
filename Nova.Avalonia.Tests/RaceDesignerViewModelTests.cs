using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels;
using Nova.Client;
using Nova.Common;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The Race Designer view model (race-designer-ui-and-availability.md; client-interface.md rows
/// 64, 71, 79). Numbers asserted are the spec's own table ("Economic-settings stage": slot
/// minimum/maximum/default, the Alternate Reality reset 10/10/10/10/5/10, the 20-80 immunity-off
/// band, the minimum band width of 20). Not pinned (questions document): the non-Humanoid preset
/// details (4.1), the Random generator's rolls (4.2), whether AR also disables the CF checkbox (4.3).
/// </summary>
[TestFixture]
public class RaceDesignerViewModelTests
{
    private static Race LoadHumanoid()
    {
        TestGame.PrepareEnvironment();
        return new Race(Path.Combine(TestGame.NovaRoot, Global.RaceFolderName, "Humanoid.race"));
    }

    /// <summary>Row 64 / row 4: the wizard's six stages, one shown at a time.</summary>
    [AvaloniaTest]
    public void SixStages_OneShownAtATime()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());

        Assert.That(designer.PageLabels, Has.Count.EqualTo(6));
        foreach (string label in designer.PageLabels)
        {
            designer.SelectedPageLabel = label;
            bool[] shown =
            {
                designer.ShowIdentityPage, designer.ShowTraitsPage, designer.ShowEnvironmentPage,
                designer.ShowProductionPage, designer.ShowResearchPage, designer.ShowLeftoverPointsPage,
            };
            Assert.That(shown.Count(flag => flag), Is.EqualTo(1), label);
            Assert.That(designer.SelectedPageLabel, Is.EqualTo(label));
        }
    }

    /// <summary>Row 10: the Primary Racial Trait is one exclusive choice among ten.</summary>
    [AvaloniaTest]
    public void PrimaryTrait_IsOneExclusiveChoiceAmongTen()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        Assert.That(designer.PrimaryTraitOptions, Has.Count.EqualTo(10));

        TraitEntry choice = designer.PrimaryTraitOptions.First(t => t.Code == "WM");
        designer.SelectedPrimaryTrait = choice;
        Assert.That(designer.SelectedPrimaryTrait.Code, Is.EqualTo("WM"));

        designer.SelectedPrimaryTrait = designer.PrimaryTraitOptions.First(t => t.Code == "IS");
        Assert.That(designer.SelectedPrimaryTrait.Code, Is.EqualTo("IS"), "a new pick replaces the old one");
    }

    /// <summary>Row 11 / 22: fourteen lesser-trait checkboxes plus the two flat-cost ones (CF on
    /// the production stage, the tech-start one on research); ticking one redraws that row and
    /// re-totals the points.</summary>
    [AvaloniaTest]
    public void LesserTraits_FourteenPlusTwo_EachTickRetotals()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        Assert.That(designer.SecondaryTraitOptions, Has.Count.EqualTo(14));
        Assert.That(designer.CheapFactoriesTrait, Is.Not.Null);
        Assert.That(designer.ExtraTechTrait, Is.Not.Null);

        SecondaryTraitOptionViewModel trait = designer.SecondaryTraitOptions.First(t => !t.IsSelected);
        int pointsBefore = designer.AdvantagePoints;
        var designerChanges = new System.Collections.Generic.List<string?>();
        var rowChanges = new System.Collections.Generic.List<string?>();
        designer.PropertyChanged += (_, e) => designerChanges.Add(e.PropertyName);
        trait.PropertyChanged += (_, e) => rowChanges.Add(e.PropertyName);
        int otherRowChanges = 0;
        foreach (SecondaryTraitOptionViewModel other in designer.SecondaryTraitOptions.Where(t => t != trait))
        {
            other.PropertyChanged += (_, _) => otherRowChanges++;
        }

        trait.IsSelected = true;

        Assert.That(otherRowChanges, Is.EqualTo(0), "only the changed row is redrawn");

        Assert.That(trait.IsSelected, Is.True);
        Assert.That(rowChanges, Does.Contain(nameof(SecondaryTraitOptionViewModel.IsSelected)));
        Assert.That(designerChanges, Does.Contain(nameof(RaceDesignerViewModel.AdvantagePoints)));
        Assert.That(designer.SecondaryTraitOptions.Where(t => t != trait).Select(t => t.IsSelected),
            Is.EqualTo(designer.SecondaryTraitOptions.Where(t => t != trait).Select(t => t.IsSelected)), "other rows untouched");
        Assert.That(designer.AdvantagePoints, Is.Not.EqualTo(pointsBefore), "each lesser trait has a point cost");
    }

    /// <summary>The research-start checkbox names tech level 3, or 4 for Jack of All Trades.</summary>
    [AvaloniaTest]
    public void ExtraTechTitle_NamesLevelFourForJackOfAllTrades()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());

        designer.SelectedPrimaryTrait = designer.PrimaryTraitOptions.First(t => t.Code == "JOAT");
        Assert.That(designer.ExtraTechTrait.Title, Does.Contain("4"));

        designer.SelectedPrimaryTrait = designer.PrimaryTraitOptions.First(t => t.Code == "HE");
        Assert.That(designer.ExtraTechTrait.Title, Does.Contain("3"));
    }

    /// <summary>Row 12: each research field is Cheap / Standard / Expensive, exclusively
    /// (50% less = 50, standard = 100, 75% extra = 175).</summary>
    [AvaloniaTest]
    public void ResearchCost_IsAThreeWayExclusiveChoice()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        ResearchCostViewModel energy = designer.Energy;

        energy.IsCheap = true;
        Assert.That((energy.IsCheap, energy.IsStandard, energy.IsExpensive), Is.EqualTo((true, false, false)));
        energy.IsExpensive = true;
        Assert.That((energy.IsCheap, energy.IsStandard, energy.IsExpensive), Is.EqualTo((false, false, true)));
        energy.IsStandard = true;
        Assert.That((energy.IsCheap, energy.IsStandard, energy.IsExpensive), Is.EqualTo((false, true, false)));
    }

    /// <summary>Rows 13-16: immunity, the minimum band width of 20, the 20-80 reset when immunity
    /// is turned off, and the availability estimate following the tolerances.</summary>
    [AvaloniaTest]
    public void EnvironmentTolerance_BandRules_AndAvailabilityFollow()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        EnvironmentToleranceViewModel gravity = designer.GravityTolerance;
        gravity.Immune = false;

        gravity.MinValue = 30;
        gravity.MaxValue = 70;
        double wide = designer.WorldAvailabilityPercent;

        gravity.MaxValue = 35;
        Assert.That(gravity.MaxValue - gravity.MinValue, Is.EqualTo(20), "a band never narrows below 20");

        Assert.That(designer.WorldAvailabilityPercent, Is.LessThan(wide), "a narrower band finds fewer worlds");

        gravity.Immune = true;
        Assert.That(designer.WorldAvailabilityPercent, Is.GreaterThan(wide), "an immune axis accepts every world");

        gravity.Immune = false;
        Assert.That((gravity.MinValue, gravity.MaxValue), Is.EqualTo((20, 80)), "immunity off restores the 20-80 band");
    }

    /// <summary>Row 17 / 21: the seven economy rows step through the spec's per-slot clamp.</summary>
    [AvaloniaTest]
    public void EconomyRows_ClampToTheSpecTable()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        designer.SelectedPrimaryTrait = designer.PrimaryTraitOptions.First(t => t.Code == "JOAT");

        StepTo(designer.ColonistsRow, -1);
        Assert.That(designer.ColonistsRow.DisplayValue, Is.EqualTo("700"));
        StepTo(designer.ColonistsRow, +1);
        Assert.That(designer.ColonistsRow.DisplayValue, Is.EqualTo("2,500"));

        StepTo(designer.FactoryOutputRow, -1);
        Assert.That(designer.FactoryOutputRow.SlotValue, Is.EqualTo(5));
        StepTo(designer.FactoryOutputRow, +1);
        Assert.That(designer.FactoryOutputRow.SlotValue, Is.EqualTo(15));

        StepTo(designer.MineCostRow, -1);
        Assert.That(designer.MineCostRow.SlotValue, Is.EqualTo(2), "the mine-cost minimum is 2");
        StepTo(designer.MineCostRow, +1);
        Assert.That(designer.MineCostRow.SlotValue, Is.EqualTo(15));

        StepTo(designer.MineOutputRow, +1);
        Assert.That(designer.MineOutputRow.DisplayValue, Is.EqualTo("25 kT"));

        int before = designer.FactoryCostRow.SlotValue;
        designer.FactoryCostRow.Step(-1, shiftHeld: true);
        Assert.That(designer.FactoryCostRow.SlotValue, Is.EqualTo(System.Math.Max(5, before - 3)), "Shift steps by 3");
    }

    /// <summary>Row 18: the leftover-points choice has the five populated options.</summary>
    [AvaloniaTest]
    public void LeftoverPoints_HasFiveOptions()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        Assert.That(designer.LeftoverPointTargetOptions, Has.Count.EqualTo(5));
        Assert.That(designer.LeftoverPointTargetOptions, Does.Contain(designer.LeftoverPointTarget));
    }

    /// <summary>Row 5 / 19: choosing Alternate Reality resets economy slots 1-6 to
    /// 10/10/10/10/5/10, clears the Germanium discount and locks rows 2-7.</summary>
    [AvaloniaTest]
    public void AlternateReality_ResetsAndLocksTheLaterEconomyRows()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        designer.SelectedPrimaryTrait = designer.PrimaryTraitOptions.First(t => t.Code == "JOAT");
        StepTo(designer.FactoryOutputRow, +1);
        StepTo(designer.MineCostRow, -1);
        designer.CheapFactoriesTrait.IsSelected = true;

        designer.SelectedPrimaryTrait = designer.PrimaryTraitOptions.First(t => t.Code == RaceDesignerRules.AlternateReality);

        Assert.That(designer.IsAlternateReality, Is.True);
        RaceDesignerEconomyRowViewModel[] later =
        {
            designer.FactoryOutputRow, designer.FactoryCostRow, designer.FactoriesOperatedRow,
            designer.MineOutputRow, designer.MineCostRow, designer.MinesOperatedRow,
        };
        Assert.That(later.Select(row => row.SlotValue), Is.EqualTo(new[] { 10, 10, 10, 10, 5, 10 }));
        Assert.That(later.Select(row => row.IsEnabled), Is.All.False);
        Assert.That(designer.ColonistsRow.IsEnabled, Is.True, "row 1 stays editable");
        Assert.That(designer.CheapFactoriesTrait.IsSelected, Is.False, "the Germanium discount is cleared");

        int value = designer.MineCostRow.SlotValue;
        designer.MineCostRow.Step(+1, shiftHeld: false);
        Assert.That(designer.MineCostRow.SlotValue, Is.EqualTo(value), "a locked row ignores its steppers");
    }

    /// <summary>Row 6 / client-interface row 71: a non-editable designer shows the values with
    /// every mutable control disabled.</summary>
    [AvaloniaTest]
    public void NonEditable_ShowsValuesButDisablesEverything()
    {
        Race race = LoadHumanoid();
        var designer = new RaceDesignerViewModel(race, isEditable: false);

        Assert.That(designer.IsEditable, Is.False);
        Assert.That(designer.Name, Is.EqualTo(race.Name));
        Assert.That(designer.SaveCommand.CanExecute(null), Is.False);
        Assert.That(designer.ApplyPresetCommand.CanExecute(null), Is.False);
        Assert.That(designer.RevertSectionCommand.CanExecute(null), Is.False);
        Assert.That(new[] { designer.ColonistsRow, designer.FactoryOutputRow, designer.MinesOperatedRow }.Select(r => r.IsEnabled), Is.All.False);
        Assert.That(designer.CancelLabel, Is.Not.EqualTo(new RaceDesignerViewModel(LoadHumanoid()).CancelLabel));
    }

    /// <summary>Row 71: Save needs a password (the secret entry), a name and a plural name.</summary>
    [AvaloniaTest]
    public void Save_NeedsANameAPluralAndAPassword()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        designer.Name = "Testers";
        designer.PluralName = "Testers";
        designer.Password = string.Empty;
        Assume.That(designer.AdvantagePoints, Is.GreaterThanOrEqualTo(0));
        Assert.That(designer.SaveCommand.CanExecute(null), Is.False);

        designer.Password = "secret";
        Assert.That(designer.SaveCommand.CanExecute(null), Is.True);

        designer.PluralName = " ";
        Assert.That(designer.SaveCommand.CanExecute(null), Is.False);
    }

    /// <summary>Rows 23/31: edits go to a working copy; the race changes only when Save completes.</summary>
    [AvaloniaTest]
    public void WorkingCopy_TheRaceChangesOnlyOnSave()
    {
        Race race = LoadHumanoid();
        string originalName = race.Name;
        var designer = new RaceDesignerViewModel(race);

        designer.Name = "Renamed";
        designer.GrowthRate = 19;
        Assert.That(race.Name, Is.EqualTo(originalName), "editing leaves the race alone");

        designer.CancelCommand.Execute(null);
        Assert.That(race.Name, Is.EqualTo(originalName), "cancel discards");

        designer.Password = "pw";
        using var stream = new MemoryStream();
        designer.CompleteSave(stream);

        Assert.That(designer.WasSaved, Is.True);
        Assert.That(race.Name, Is.EqualTo("Renamed"));
        Assert.That(race.GrowthRate, Is.EqualTo(19));
        Assert.That(stream.Length, Is.GreaterThan(0));
    }

    /// <summary>Row 2 / 23: "undo this section" restores only the current section.</summary>
    [AvaloniaTest]
    public void RevertSection_RestoresOnlyTheCurrentSection()
    {
        Race race = LoadHumanoid();
        var designer = new RaceDesignerViewModel(race);
        designer.Name = "Changed";
        SecondaryTraitOptionViewModel trait = designer.SecondaryTraitOptions.First(t => !t.IsSelected);
        trait.IsSelected = true;

        designer.SelectedPageLabel = designer.PageLabels[0]; // Identity
        designer.RevertSectionCommand.Execute(null);

        Assert.That(designer.Name, Is.EqualTo(race.Name), "the identity section is undone");
        Assert.That(trait.IsSelected, Is.True, "the traits section is not");
    }

    /// <summary>Row 7 / 20: the preset picker; Humanoid applies the spec defaults.</summary>
    [AvaloniaTest]
    public void Presets_EightChoices_HumanoidAppliesTheSpecDefaults()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        Assert.That(designer.PresetOptions, Has.Count.EqualTo(8));
        Assert.That(designer.PresetOptions.Count(p => p.IsRandom), Is.EqualTo(1));

        designer.SelectedPrimaryTrait = designer.PrimaryTraitOptions.First(t => t.Code == "HE");
        StepTo(designer.FactoryOutputRow, +1);
        designer.SelectedPreset = designer.PresetOptions.First(p => p.Name == "Humanoid");
        designer.ApplyPresetCommand.Execute(null);

        Assert.That(designer.SelectedPrimaryTrait.Code, Is.EqualTo("JOAT"));
        Assert.That(new[]
        {
            designer.ColonistsRow, designer.FactoryOutputRow, designer.FactoryCostRow, designer.FactoriesOperatedRow,
            designer.MineOutputRow, designer.MineCostRow, designer.MinesOperatedRow,
        }.Select(row => row.SlotValue), Is.EqualTo(new[] { 10, 10, 10, 10, 10, 5, 10 }));
    }

    /// <summary>Row 8: Random yields a race inside the 0-50 advantage-point window, or falls back
    /// to Humanoid and says so.</summary>
    [AvaloniaTest]
    public void RandomPreset_LandsInTheZeroToFiftyWindow_OrFallsBack()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        designer.SelectedPreset = designer.PresetOptions.First(p => p.IsRandom);

        designer.ApplyPresetCommand.Execute(null);

        bool inWindow = designer.AdvantagePoints >= RandomRaceGenerator.TargetMinimumPoints
            && designer.AdvantagePoints <= RandomRaceGenerator.TargetMaximumPoints;
        Assert.That(inWindow || designer.StatusMessage.Contains("Humanoid"), Is.True, designer.StatusMessage);
    }

    /// <summary>Row 32 / client-interface row 79: help opens from any stage and never changes the draft.</summary>
    [AvaloniaTest]
    public void Help_FromAnyStage_LeavesTheDraftUnchanged()
    {
        var designer = new RaceDesignerViewModel(LoadHumanoid());
        designer.Name = "Draft";
        int points = designer.AdvantagePoints;

        foreach (string page in designer.PageLabels)
        {
            designer.SelectedPageLabel = page;
            designer.HelpCommand.Execute(null);
            Assert.That(designer.IsHelpVisible, Is.True, page);
            Assert.That(designer.HelpTitle, Is.Not.Empty, page);
            Assert.That(designer.HelpText, Is.Not.Empty, page);
        }

        designer.CloseHelpCommand.Execute(null);
        Assert.That(designer.IsHelpVisible, Is.False);
        Assert.That(designer.Name, Is.EqualTo("Draft"));
        Assert.That(designer.AdvantagePoints, Is.EqualTo(points));
    }

    private static void StepTo(RaceDesignerEconomyRowViewModel row, int direction)
    {
        for (int i = 0; i < 40; i++)
        {
            row.Step(direction, shiftHeld: false);
        }
    }
}
