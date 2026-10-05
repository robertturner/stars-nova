using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Research panel: current tech levels and banked resources across all six fields, plus
/// the research budget and target field - empire-wide, so (unlike Production/Inspector) this
/// doesn't depend on the current Navigator selection.
///
/// Stars! research has no priority/weight system - ResearchDialog.cs's own
/// "TODO: Implement a proper hierarchy of research" comment confirms it's deliberately a
/// single one-hot target field (EmpireData.ResearchTopics has exactly one field set to 1).
/// Mirrors that dialog's commit pattern too: budget and target-field edits are free to change
/// without side effects, and a single ResearchCommand (Budget + the one-hot Topics) is only
/// built/pushed/applied when Apply is clicked - unlike Phase 1/2's per-action instant commits,
/// since this is one whole-empire setting being tuned to a final value, not a list of
/// independent actions.
///
/// Also ports ResearchDialog's "Expected Research Benefits" preview (PopulateResearchBenefits/
/// ParameterChanged): as the user tries out a different target field or budget percentage
/// (before clicking Apply - a live preview, exactly like the WinForms dialog recomputes on every
/// RadioButton/NumericUpDown change), this shows every not-yet-available component that
/// completing more levels of the target field alone would eventually unlock, how many years
/// that's likely to take at the currently-entered budget, and how many resources are still
/// needed to reach just the next level.
/// </summary>
public class ResearchViewModel : Tool
{
    private readonly ClientData clientState;

    private IReadOnlyList<ResearchFieldRowViewModel> fields = Array.Empty<ResearchFieldRowViewModel>();

    public IReadOnlyList<ResearchFieldRowViewModel> Fields
    {
        get => fields;
        private set => SetProperty(ref fields, value);
    }

    public IReadOnlyList<string> TargetFieldOptions { get; } = Enum.GetValues<TechLevel.ResearchField>()
        .Select(field => field.ToString())
        .ToList();

    private int editableBudget;

    public int EditableBudget
    {
        get => editableBudget;
        set
        {
            if (SetProperty(ref editableBudget, value))
            {
                RefreshPreview();
            }
        }
    }

    private string selectedTargetField = "";

    public string SelectedTargetField
    {
        get => selectedTargetField;
        set
        {
            if (SetProperty(ref selectedTargetField, value))
            {
                RefreshPreview();
            }
        }
    }

    private static readonly List<KeyValuePair<string, int>> NextFieldChoices = ResearchNextField.Choices();

    /// <summary>The "next field to research" combo (research-tech-tree.md section 4; Nova.Client.
    /// ResearchNextField): the field research moves to once the current target gains a level.</summary>
    public IReadOnlyList<string> NextFieldOptions { get; } = NextFieldChoices.Select(choice => choice.Key).ToList();

    private int selectedNextFieldIndex;

    public int SelectedNextFieldIndex
    {
        get => selectedNextFieldIndex;
        set => SetProperty(ref selectedNextFieldIndex, Math.Clamp(value, 0, NextFieldChoices.Count - 1));
    }

    /// <summary>Total energy resources this empire's owned stars generate each turn - the same
    /// figure ResearchDialog.CountEnergy computes, before any research budget percentage is
    /// applied to it.</summary>
    private int availableEnergy;

    public int AvailableEnergy
    {
        get => availableEnergy;
        private set => SetProperty(ref availableEnergy, value);
    }

    private int budgetedEnergy;

    public int BudgetedEnergy
    {
        get => budgetedEnergy;
        private set => SetProperty(ref budgetedEnergy, value);
    }

    private string completionResourcesText = "";

    public string CompletionResourcesText
    {
        get => completionResourcesText;
        private set => SetProperty(ref completionResourcesText, value);
    }

    private string completionTimeText = "";

    public string CompletionTimeText
    {
        get => completionTimeText;
        private set => SetProperty(ref completionTimeText, value);
    }

    private IReadOnlyList<ResearchBenefitRowViewModel> benefits = Array.Empty<ResearchBenefitRowViewModel>();

    public IReadOnlyList<ResearchBenefitRowViewModel> Benefits
    {
        get => benefits;
        private set => SetProperty(ref benefits, value);
    }

    private string statusMessage = "";

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
            {
                HasStatusMessage = !string.IsNullOrEmpty(value);
            }
        }
    }

    private bool hasStatusMessage;

    public bool HasStatusMessage
    {
        get => hasStatusMessage;
        private set => SetProperty(ref hasStatusMessage, value);
    }

    public IRelayCommand ApplyCommand { get; }

    public ResearchViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;

        ApplyCommand = new RelayCommand(Apply);
        RefreshFromEmpireState();
    }

    private int loadedBudget;

    private string loadedTargetField = "";

    private int loadedNextFieldIndex;

    private void RefreshFromEmpireState()
    {
        EmpireData empire = clientState.EmpireState;

        // Normally exactly one field is marked 1 (see this class's own doc comment), but a
        // brand-new empire that hasn't chosen a research priority yet - or any save where the
        // player simply hasn't touched this panel - has every field at 0. Falling back to
        // TechLevel.FirstField instead of throwing lets the panel open in that state instead of
        // crashing the whole game-open sequence (confirmed live: opening a real save with
        // all-zero Topics threw "Sequence contains no matching element" from the old .First()).
        TechLevel.ResearchField currentTarget = Enum.GetValues<TechLevel.ResearchField>()
            .FirstOrDefault(field => empire.ResearchTopics[field] == 1, TechLevel.FirstField);

        Fields = Enum.GetValues<TechLevel.ResearchField>()
            .Select(field => new ResearchFieldRowViewModel(
                field.ToString(),
                empire.ResearchLevels[field],
                empire.ResearchResources[field],
                field == currentTarget))
            .ToList();

        AvailableEnergy = CountEnergy();
        EditableBudget = empire.ResearchBudget;
        SelectedTargetField = currentTarget.ToString();
        SelectedNextFieldIndex = ResearchNextField.IndexOf(empire.ResearchNextField);

        // What the editor opened with - Apply writes only when one of these was changed.
        loadedBudget = EditableBudget;
        loadedTargetField = SelectedTargetField;
        loadedNextFieldIndex = SelectedNextFieldIndex;

        // EditableBudget/SelectedTargetField's setters already call RefreshPreview() when their
        // value actually changes - but if it happens to already equal the field's initial default
        // (0/"") neither setter fires, so call it once more explicitly to guarantee the preview
        // is populated from a fresh RefreshFromEmpireState() regardless.
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (string.IsNullOrEmpty(SelectedTargetField))
        {
            return;
        }

        TechLevel.ResearchField targetField = Enum.Parse<TechLevel.ResearchField>(SelectedTargetField);
        TechLevel currentLevel = clientState.EmpireState.ResearchLevels;

        BudgetedEnergy = (AvailableEnergy * EditableBudget) / 100;
        int bankedResources = clientState.EmpireState.ResearchResources[targetField];

        int resourcesRequired = 0;
        if (currentLevel[targetField] >= TechLevel.MaxLevel)
        {
            // Research.Cost's base-cost table only has entries for levels 1..MaxLevel; asking
            // for MaxLevel + 1 would throw. Nothing more to research here.
            CompletionResourcesText = "Maxed";
        }
        else
        {
            // docs/behavior-specs-4/research-tech-tree.md's forecast formula is just "outstanding
            // cost / per-turn contribution" - outstanding cost = the cost-table lookup minus
            // progress already banked. Subtracting currentLevel[targetField] (a raw tech-level
            // integer, e.g. 5) as well was a dimensional bug: it conflated a level number with a
            // resource-cost figure (e.g. 1330), throwing off both this and the years-to-complete
            // figure below.
            int targetCost = Research.Cost(targetField, clientState.EmpireState.Race, currentLevel, currentLevel[targetField] + 1);
            resourcesRequired = targetCost - bankedResources;
            CompletionResourcesText = resourcesRequired.ToString(CultureInfo.InvariantCulture);
        }

        // A GR race's target field only actually receives half of BudgetedEnergy per turn (see
        // Research.TargetFieldContributionFraction) - the forecast must divide by that effective
        // rate, not the full budgeted total (which is what BudgetedEnergy's own displayed figure
        // correctly still shows in full, since that IS the whole per-turn research contribution,
        // just not all of it landing on this one field).
        int targetFieldRate = (int)(BudgetedEnergy * Research.TargetFieldContributionFraction(clientState.EmpireState.Race));

        if (EditableBudget != 0 && targetFieldRate > 0 && currentLevel[targetField] < TechLevel.MaxLevel)
        {
            int yearsToComplete = (int)Math.Ceiling((double)resourcesRequired / targetFieldRate);
            CompletionTimeText = yearsToComplete.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            CompletionTimeText = "Never";
        }

        Benefits = BuildBenefits(targetField, currentLevel, clientState.EmpireState.Race, clientState.EmpireState.ResearchResources);
    }

    /// <summary>
    /// Every component not yet available that further research in <paramref name="targetField"/>
    /// alone (holding every other field at its current level) would eventually unlock - not just
    /// the very next level. A component needing a higher level in some OTHER, unrelated field is
    /// omitted, since researching targetField alone will never unlock it.
    /// Components the viewing race can never receive are left out: a part a primary or lesser
    /// trait bars (RaceComponents.IsRestrictedFor), and the one-time battle-gift parts
    /// (SpecialComponentGrants), which only a battle grant - never research - can hand out.
    /// Each row carries the detail card's single status line instead of the port's levels-away
    /// color grade: "Unavailable" (drawn in red), "Available", the research resources still
    /// needed, or that figure in thousands with a "k" from 100,000 up (behavior-specs-11/
    /// research-tech-tree.md section 7a, Nova.Client.TechStatusLine).
    /// </summary>
    private static IReadOnlyList<ResearchBenefitRowViewModel> BuildBenefits(TechLevel.ResearchField targetField, TechLevel currentLevel, Race race, TechLevel bankedResources)
    {
        var allComponents = new AllComponents();
        int currentFieldLevel = currentLevel[targetField];

        var benefits = new List<(int StillNeeded, ResearchBenefitRowViewModel Row)>();

        foreach (Component component in allComponents.GetAll.Values)
        {
            if (currentLevel.Meets(component.RequiredTech))
            {
                // Already available - nothing more to research for it.
                continue;
            }

            // Parts this race can never receive do not belong on a "what will research unlock"
            // list: a primary/lesser trait bars them, or they are one of the one-time battle-gift
            // specials that a grant (never research) is the only way to get.
            if (RaceComponents.IsRestrictedFor(component, race) || SpecialComponentGrants.IsSpecialGrant(component.Name))
            {
                continue;
            }

            int levelsAway = -1;

            for (int candidate = currentFieldLevel + 1; candidate <= TechLevel.MaxLevel; candidate++)
            {
                TechLevel trialLevel = new TechLevel(currentLevel);
                trialLevel[targetField] = candidate;

                if (trialLevel.Meets(component.RequiredTech))
                {
                    levelsAway = candidate - currentFieldLevel;
                    break;
                }
            }

            if (levelsAway < 0)
            {
                // Needs a higher level in some other field too - researching targetField alone
                // will never unlock this, so don't list it here.
                continue;
            }

            // The detail card's one status line replaces the levels-away grade. A row reads
            // "Available" even before the levels are bought when banked resources already cover
            // every still-missing level (TechStatusLine, exactly as the original card does).
            int stillNeeded = TechStatusLine.ForComponent(component, race, currentLevel, bankedResources);
            bool unavailable = TechStatusLine.KindOf(stillNeeded) == TechStatusLine.Kind.Unavailable;

            benefits.Add((stillNeeded, new ResearchBenefitRowViewModel(
                component.Name + " " + component.Type,
                unavailable ? Brushes.Red : Brushes.White,
                TechStatusLine.Format(stillNeeded),
                unavailable)));
        }

        return benefits
            .OrderBy(benefit => benefit.StillNeeded)
            .Select(benefit => benefit.Row)
            .ToList();
    }

    /// <summary>Total energy resources this empire's owned stars generate each turn - ports
    /// ResearchDialog.CountEnergy exactly.</summary>
    private int CountEnergy()
    {
        int totalEnergy = 0;

        foreach (Star star in clientState.EmpireState.OwnedStars.Values)
        {
            if (star.Owner == clientState.EmpireState.Id)
            {
                totalEnergy += star.GetResourceRate();
            }
        }

        return totalEnergy;
    }

    private void Apply()
    {
        var command = new ResearchCommand
        {
            Budget = EditableBudget,
            NextField = NextFieldChoices[selectedNextFieldIndex].Value,
        };
        command.Topics.Zero();
        command.Topics[Enum.Parse<TechLevel.ResearchField>(SelectedTargetField)] = 1;

        // ResearchCommand.IsValid's own "nothing changed" test compares the two TechLevel Topics
        // with ==, which TechLevel does not overload (reference equality, so never equal) - an
        // unchanged Apply used to queue an order anyway. Compare field by field here so the
        // editor writes only when something changed (race-designer-ui-and-availability.md,
        // research-preference accept).
        // Also nothing to write when the editor still shows what it opened with (a brand-new
        // empire stores no target field; the editor shows the first field, which is not a change
        // the player made).
        bool editedSinceLoad = EditableBudget != loadedBudget || SelectedTargetField != loadedTargetField || selectedNextFieldIndex != loadedNextFieldIndex;
        if (!editedSinceLoad || !command.IsValid(clientState.EmpireState) || !ChangesSomething(command, clientState.EmpireState))
        {
            StatusMessage = "No changes to apply.";
            return;
        }

        clientState.Commands.Push(command);
        command.ApplyToState(clientState.EmpireState);
        RefreshFromEmpireState();
        StatusMessage = "Applied.";
    }

    private static bool ChangesSomething(ResearchCommand command, EmpireData empire)
    {
        if (command.Budget != empire.ResearchBudget)
        {
            return true;
        }

        if (command.NextField.HasValue && command.NextField.Value != empire.ResearchNextField)
        {
            return true;
        }

        return Enum.GetValues<TechLevel.ResearchField>().Any(field => command.Topics[field] != empire.ResearchTopics[field]);
    }
}
