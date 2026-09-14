using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Lists every empire this player has intel on and lets them set a diplomatic stance toward
/// each - ports PlayerRelations.cs. A relation change is a queued turn order, same as every
/// other order-issuing panel in this app - see EmpireRelationRowViewModel's doc comment for why
/// this now differs from the prior session's conclusion (recorded in PROJECT-STATUS.md's Phase 7
/// entry) that no such command existed.
/// </summary>
public class PlayerRelationsViewModel : Tool
{
    public IReadOnlyList<EmpireRelationRowViewModel> Empires { get; }

    public bool HasEmpires => Empires.Count > 0;

    public PlayerRelationsViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;

        Empires = clientState.EmpireState.EmpireReports.Values
            .Where(report => report.Id != clientState.EmpireState.Id)
            .Select(report => new EmpireRelationRowViewModel(report, clientState.Commands))
            .OrderBy(row => row.RaceName)
            .ToList();
    }
}
