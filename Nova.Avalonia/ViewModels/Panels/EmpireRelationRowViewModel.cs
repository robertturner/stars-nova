using System.Collections.Generic;
using Nova.Common;
using Nova.Common.Commands;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Player Relations panel - a known empire and this empire's stance toward it.
/// Mirrors PlayerRelations.cs's RelationChanged: a relation change is a queued turn order (see
/// docs/behavior-specs-3/diplomacy-relations.md §2, verified against a decompile of the exported
/// client - this corrects the prior session's conclusion, recorded in PROJECT-STATUS.md's Phase 7
/// entry, that no such command existed), pushed onto ClientData.Commands for the eventual
/// .orders file and applied to the local EmpireIntel immediately after for optimistic UI feedback -
/// the same pattern every other order-issuing panel in this app already follows.
/// </summary>
public class EmpireRelationRowViewModel : ViewModelBase
{
    private readonly EmpireIntel report;
    private readonly Stack<ICommand> commands;

    public static IReadOnlyList<PlayerRelation> RelationOptions { get; } =
        new[] { PlayerRelation.Enemy, PlayerRelation.Neutral, PlayerRelation.Friend };

    public ushort Id => report.Id;

    public string RaceName => report.RaceName;

    public PlayerRelation Relation
    {
        get => report.Relation;
        set
        {
            if (report.Relation != value)
            {
                commands.Push(new RelationCommand(report.Id, value));
                report.Relation = value;
                OnPropertyChanged();
            }
        }
    }

    public EmpireRelationRowViewModel(EmpireIntel report, Stack<ICommand> commands)
    {
        this.report = report;
        this.commands = commands;
    }
}
