using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>Read-only table of this turn's score standings - ports ScoreReport.cs. A single-turn
/// snapshot (clientState.InputTurn.AllScores), not a historical series - matches the original.</summary>
public class ScoreReportViewModel : Tool
{
    public IReadOnlyList<ScoreReportRowViewModel> Scores { get; }

    public ScoreReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;

        Scores = (clientState.InputTurn?.AllScores ?? new List<Nova.Common.ScoreRecord>())
            .Select(score => new ScoreReportRowViewModel(score))
            .ToList();
    }
}
