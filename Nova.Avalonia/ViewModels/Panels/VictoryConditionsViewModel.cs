using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The in-session Victory Conditions summary (behavior-specs-10/client-ui-dialog-catalog.md
/// "Additional confirmed surfaces": distinct from the setup wizard's page), read-only, built from
/// the game's settings by Nova.Client.VictorySummary (see its SPEC GAP note). The settings are
/// re-read whenever the panel is refreshed (the Star Map restores them from the game's
/// .settings file when the game opens).
/// </summary>
public class VictoryConditionsViewModel : Tool
{
    public VictoryConditionsViewModel(string id, string title)
    {
        Id = id;
        Title = title;
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    private IReadOnlyList<VictorySummaryRow> rows = Array.Empty<VictorySummaryRow>();

    public IReadOnlyList<VictorySummaryRow> Rows
    {
        get => rows;
        private set => SetProperty(ref rows, value);
    }

    private string conditionsLine = "";

    public string ConditionsLine
    {
        get => conditionsLine;
        private set => SetProperty(ref conditionsLine, value);
    }

    private string yearGateLine = "";

    public string YearGateLine
    {
        get => yearGateLine;
        private set => SetProperty(ref yearGateLine, value);
    }

    public IRelayCommand RefreshCommand { get; }

    public void Refresh()
    {
        GameSettings settings = GameSettings.Data;
        Rows = VictorySummary.Rows(settings);
        ConditionsLine = VictorySummary.ConditionsLine(settings);
        YearGateLine = VictorySummary.YearGateLine(settings);
    }
}
