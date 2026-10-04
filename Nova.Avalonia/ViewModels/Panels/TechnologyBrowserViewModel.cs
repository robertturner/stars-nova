using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Technology Browser (F2; behavior-specs-10/client-interface.md command 256/136 and
/// client-ui-dialog-catalog.md "Additional confirmed surfaces": Prev/Next paging through tech
/// entries, a category combo and a "Show Only Available Technology" checkbox; the race's six
/// tech levels as "Ener: / Weap: / Prop: / Const: / Elect: / Bio:"). The paging and filtering
/// are Nova.Client.TechBrowser (see its SPEC GAP note); the card shows the component's cost,
/// mass, required levels, the detail card's status line and its description.
/// </summary>
public class TechnologyBrowserViewModel : Tool
{
    private readonly ClientData clientState;
    private readonly TechBrowser browser;

    public TechnologyBrowserViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;

        IEnumerable<Component> components;
        try
        {
            components = new AllComponents().GetAll.Values;
        }
        catch (Exception)
        {
            components = Enumerable.Empty<Component>();
        }

        RaceComponents? available = clientState.EmpireState.AvailableComponents;
        browser = new TechBrowser(
            components,
            component => available != null && available.Contains(component.Name),
            component => clientState.EmpireState.GrantedSpecialComponents != null
                && clientState.EmpireState.GrantedSpecialComponents.Contains(component.Name));
        Categories = browser.Categories;

        PreviousCommand = new RelayCommand(() => { browser.Previous(); Refresh(); }, () => browser.CanPrevious);
        NextCommand = new RelayCommand(() => { browser.Next(); Refresh(); }, () => browser.CanNext);
        Refresh();
    }

    public IReadOnlyList<string> Categories { get; }

    public int CategoryIndex
    {
        get => browser.CategoryIndex;
        set
        {
            if (value >= 0 && value < browser.Categories.Count && value != browser.CategoryIndex)
            {
                browser.SetCategory(value);
                Refresh();
            }
        }
    }

    public bool ShowOnlyAvailable
    {
        get => browser.ShowOnlyAvailable;
        set
        {
            if (value != browser.ShowOnlyAvailable)
            {
                browser.ShowOnlyAvailable = value;
                Refresh();
            }
        }
    }

    public IRelayCommand PreviousCommand { get; }

    public IRelayCommand NextCommand { get; }

    public string LevelsLine => TechBrowser.LevelsLine(clientState.EmpireState.ResearchLevels);

    public bool HasEntry => browser.Current != null;

    public string PositionText => browser.Current == null
        ? (browser.ShowOnlyAvailable ? "Nothing available in this category yet." : "No entries.")
        : $"{browser.Index + 1} of {browser.Entries.Count}";

    public string EntryName => browser.Current?.Name ?? "";

    public string EntryCategory => browser.Current == null ? "" : TechBrowser.CategoryNames[TechBrowser.CategoryOf(browser.Current)];

    public string EntryCost => browser.Current == null ? "" : ResourceFormat.Cost(browser.Current.Cost);

    public string EntryMass => browser.Current == null ? "" : browser.Current.Mass.ToString(CultureInfo.InvariantCulture) + " kT";

    public string EntryRequirements => browser.Current == null ? "" : TechBrowser.RequirementLine(browser.Current.RequiredTech);

    public string EntryStatus
    {
        get
        {
            if (browser.Current == null)
            {
                return "";
            }

            EmpireData empire = clientState.EmpireState;
            int stillNeeded = TechStatusLine.ForComponent(browser.Current, empire.Race, empire.ResearchLevels, empire.ResearchResources);
            return TechStatusLine.Format(stillNeeded);
        }
    }

    public string EntryDescription => browser.Current?.Description ?? "";

    /// <summary>Opens the browser on a named component (a message's component reference).</summary>
    public bool ShowComponent(string componentName)
    {
        bool shown = browser.Show(componentName);
        Refresh();
        return shown;
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CategoryIndex));
        OnPropertyChanged(nameof(ShowOnlyAvailable));
        OnPropertyChanged(nameof(LevelsLine));
        OnPropertyChanged(nameof(HasEntry));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(EntryName));
        OnPropertyChanged(nameof(EntryCategory));
        OnPropertyChanged(nameof(EntryCost));
        OnPropertyChanged(nameof(EntryMass));
        OnPropertyChanged(nameof(EntryRequirements));
        OnPropertyChanged(nameof(EntryStatus));
        OnPropertyChanged(nameof(EntryDescription));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }
}
