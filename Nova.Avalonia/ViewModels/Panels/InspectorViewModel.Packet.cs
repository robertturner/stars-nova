using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The mineral-packet half of the Inspector (behavior-specs-10/production-queue.md §10b;
/// fleet-movement-scanning-cargo.md: the packet destination is a planet order, set only while
/// the planet's starbase carries a mass driver):
/// - for one of this empire's planets with a mass driver, its packet destination and chosen
///   packet speed (Star.PacketDestination / PacketWarp), changed through a
///   PacketDestinationCommand queued like every other order and applied to the client's copy at
///   once (Nova.Client.PacketOrders.Issue); the speed choices are 5..(best driver warp + 3) and
///   the "launches at" line is the speed a packet would really fly at;
/// - for a packet marker picked on the map (EmpireData.MineralPacketReports), its details.
/// </summary>
public partial class InspectorViewModel
{
    private Star? packetStar;

    // Set while the controls are being refreshed from the star, so the setters do not echo the
    // refreshed values back as new orders.
    private bool refreshingPacketControls;

    private bool canSetPacketDestination;

    /// <summary>The selected planet is this empire's and its starbase carries a mass driver.</summary>
    public bool CanSetPacketDestination
    {
        get => canSetPacketDestination;
        private set => SetProperty(ref canSetPacketDestination, value);
    }

    private IReadOnlyList<string> packetDestinationChoices = Array.Empty<string>();

    /// <summary>"(none)" then every planet this empire knows of except this one.</summary>
    public IReadOnlyList<string> PacketDestinationChoices
    {
        get => packetDestinationChoices;
        private set => SetProperty(ref packetDestinationChoices, value);
    }

    private string? selectedPacketDestination;

    /// <summary>The planet's packet destination; changing it queues a PacketDestinationCommand
    /// ("(none)" clears it).</summary>
    public string? SelectedPacketDestination
    {
        get => selectedPacketDestination;
        set
        {
            if (value == null || value == selectedPacketDestination)
            {
                return;
            }

            selectedPacketDestination = value;
            OnPropertyChanged();
            if (!refreshingPacketControls && packetStar != null)
            {
                IssuePacketOrder(value, SelectedPacketWarp());
            }
        }
    }

    private bool hasPacketDestination;

    /// <summary>A destination is set (the speed choice applies only then).</summary>
    public bool HasPacketDestination
    {
        get => hasPacketDestination;
        private set => SetProperty(ref hasPacketDestination, value);
    }

    private IReadOnlyList<int> packetSpeeds = Array.Empty<int>();

    private IReadOnlyList<string> packetSpeedChoices = Array.Empty<string>();

    /// <summary>"Warp 5" .. "Warp (best driver warp + 3)".</summary>
    public IReadOnlyList<string> PacketSpeedChoices
    {
        get => packetSpeedChoices;
        private set => SetProperty(ref packetSpeedChoices, value);
    }

    private int selectedPacketSpeedIndex = -1;

    /// <summary>The chosen packet speed; changing it queues a PacketDestinationCommand with the
    /// current destination.</summary>
    public int SelectedPacketSpeedIndex
    {
        get => selectedPacketSpeedIndex;
        set
        {
            if (value < 0 || value == selectedPacketSpeedIndex)
            {
                return;
            }

            selectedPacketSpeedIndex = value;
            OnPropertyChanged();
            if (!refreshingPacketControls && packetStar != null && MineralPacketRules.HasTarget(packetStar))
            {
                IssuePacketOrder(packetStar.PacketDestination, SelectedPacketWarp());
            }
        }
    }

    private string packetLaunchSpeedText = "";

    /// <summary>"Packets launch at warp N" - the chosen speed when it lies in 5..(best driver
    /// warp + 3), otherwise the launch rating (production-queue.md §10b).</summary>
    public string PacketLaunchSpeedText
    {
        get => packetLaunchSpeedText;
        private set => SetProperty(ref packetLaunchSpeedText, value);
    }

    private string packetStatusMessage = "";

    public string PacketStatusMessage
    {
        get => packetStatusMessage;
        private set => SetProperty(ref packetStatusMessage, value);
    }

    private int SelectedPacketWarp()
    {
        return selectedPacketSpeedIndex >= 0 && selectedPacketSpeedIndex < packetSpeeds.Count
            ? packetSpeeds[selectedPacketSpeedIndex]
            : packetStar?.PacketWarp ?? 0;
    }

    private void IssuePacketOrder(string destination, int warp)
    {
        if (packetStar == null)
        {
            return;
        }

        PacketDestinationCommand command = PacketOrders.DestinationOrder(packetStar, destination, warp);
        PacketStatusMessage = PacketOrders.Issue(clientState, command) ? "" : "That destination cannot be set.";

        // Re-show the planet (its Rows carry the setting) and let the other panels know.
        Star star = packetStar;
        ShowStar(star);
        selection.NotifyMutated();
    }

    /// <summary>Adds the packet rows to an owned planet's Rows and refreshes the controls.
    /// Called from ShowStar.</summary>
    private void ShowPacketControls(List<InspectorRow> rowList, Star star)
    {
        packetStar = star;
        bool canSet = PacketOrders.CanSetDestination(star);
        CanSetPacketDestination = canSet;
        if (!canSet)
        {
            HasPacketDestination = false;
            PacketSpeedChoices = Array.Empty<string>();
            packetSpeeds = Array.Empty<int>();
            PacketLaunchSpeedText = "";
            return;
        }

        bool hasTarget = MineralPacketRules.HasTarget(star);
        int launchSpeed = PacketOrders.LaunchSpeed(star);
        rowList.Add(new InspectorRow("Packet destination", PacketOrders.DestinationText(star)));
        if (hasTarget)
        {
            rowList.Add(new InspectorRow("Packet speed", $"Warp {launchSpeed}"));
        }

        refreshingPacketControls = true;
        try
        {
            // The lists are only replaced when they change, so re-showing the planet after an
            // order (from inside a ComboBox's own selection change) leaves the open box alone.
            IReadOnlyList<string> destinations = PacketOrders.DestinationChoices(star, clientState.EmpireState);
            if (!destinations.SequenceEqual(PacketDestinationChoices))
            {
                PacketDestinationChoices = destinations;
            }

            selectedPacketDestination = hasTarget ? star.PacketDestination : PacketOrders.NoDestination;
            OnPropertyChanged(nameof(SelectedPacketDestination));
            HasPacketDestination = hasTarget;

            packetSpeeds = PacketOrders.SpeedChoices(star);
            List<string> speedLabels = packetSpeeds.Select(speed => $"Warp {speed}").ToList();
            if (!speedLabels.SequenceEqual(PacketSpeedChoices))
            {
                PacketSpeedChoices = speedLabels;
            }

            selectedPacketSpeedIndex = packetSpeeds.ToList().IndexOf(launchSpeed);
            OnPropertyChanged(nameof(SelectedPacketSpeedIndex));
            PacketLaunchSpeedText = hasTarget ? $"Packets launch at warp {launchSpeed}" : "Set a destination to launch packets";
        }
        finally
        {
            refreshingPacketControls = false;
        }
    }

    private void ClearPacketControls()
    {
        packetStar = null;
        CanSetPacketDestination = false;
        HasPacketDestination = false;
        PacketStatusMessage = "";
    }

    /// <summary>A mineral packet in flight picked on the map (read-only).</summary>
    private void ShowPacket(MineralPacket packet)
    {
        Kind = "Mineral Packet";
        Name = string.IsNullOrEmpty(packet.Name) ? "Mineral Packet" : packet.Name;

        string? owner = packet.Owner == clientState.EmpireState.Id
            ? "You"
            : clientState.EmpireState.EmpireReports.TryGetValue(packet.Owner, out EmpireIntel? intel)
                ? intel.RaceName
                : null;

        Rows = PacketOrders.DescribePacket(packet, owner)
            .Select(row => new InspectorRow(row.Key, row.Value))
            .ToList();
    }
}
