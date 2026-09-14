using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>One row in the Score Report - ports ScoreReport.cs's OnLoad column-by-column.
/// The original shows only the empire's hex id, no name lookup - kept as-is for parity.</summary>
public class ScoreReportRowViewModel
{
    public string EmpireId { get; }

    public int Rank { get; }

    public int Score { get; }

    public int Planets { get; }

    public int Starbases { get; }

    public int UnarmedShips { get; }

    public int EscortShips { get; }

    public int CapitalShips { get; }

    public int TechLevel { get; }

    public int Resources { get; }

    public ScoreReportRowViewModel(ScoreRecord score)
    {
        EmpireId = score.EmpireId.ToString("X");
        Rank = score.Rank;
        Score = score.Score;
        Planets = score.Planets;
        Starbases = score.Starbases;
        UnarmedShips = score.UnarmedShips;
        EscortShips = score.EscortShips;
        CapitalShips = score.CapitalShips;
        TechLevel = score.TechLevel;
        Resources = score.Resources;
    }
}
