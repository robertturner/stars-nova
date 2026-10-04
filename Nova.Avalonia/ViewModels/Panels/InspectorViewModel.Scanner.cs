using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Nova.Client.Shell;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The planet inspector's "scanner display" percentage control (behavior-specs-10/
/// client-ui-dialog-catalog.md "Planet inspector"; rules in Nova.Client.Shell.ScannerPercentInput):
/// a "N%" text field (Enter commits a typed 2-100 value, Escape reverts to the stored one) with
/// a slider kept in step; either commit turns the map's scan circles on (MapViewOptions); a live
/// value tooltip shows while the value is changing (400 ms after the last change).
/// The stored value is the map's (StarMapDocumentViewModel.ScannerPercentage), which the map's
/// own View popup slider also edits - NovaDockFactory connects the two with
/// <see cref="AttachScannerDisplay"/>. Desktop only: the Android screen does not attach it and
/// shows the planet's scanner range as an Overview row instead.
/// </summary>
public partial class InspectorViewModel
{
    private Func<int>? readScannerPercentage;
    private Action<int>? writeScannerPercentage;
    private DispatcherTimer? scannerTooltipTimer;
    private DateTime scannerLastChange = DateTime.MinValue;
    private bool scannerHooked;

    /// <summary>Connects the control to the map's stored percentage.</summary>
    public void AttachScannerDisplay(Func<int> read, Action<int> write)
    {
        readScannerPercentage = read;
        writeScannerPercentage = write;
        if (!scannerHooked)
        {
            scannerHooked = true;
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Kind))
                {
                    OnPropertyChanged(nameof(ShowScannerDisplay));
                }
            };
        }

        RefreshScannerDisplay();
        OnPropertyChanged(nameof(ShowScannerDisplay));
    }

    /// <summary>Shown for a planet (own or reported) once attached.</summary>
    public bool ShowScannerDisplay => writeScannerPercentage != null && Kind.StartsWith("Planet", StringComparison.Ordinal);

    private string scannerPercentText = ScannerPercentInput.Format(100);

    /// <summary>The "N%" text field (typing does not commit until Enter).</summary>
    public string ScannerPercentText
    {
        get => scannerPercentText;
        set => SetProperty(ref scannerPercentText, value);
    }

    /// <summary>The slider position (2-100); moving it commits at once.</summary>
    public int ScannerPercentSlider
    {
        get => readScannerPercentage?.Invoke() ?? 100;
        set => CommitScannerPercentage(value);
    }

    private bool isScannerTooltipVisible;

    /// <summary>The live-value tooltip's visibility (ScannerPercentInput.TooltipWindow).</summary>
    public bool IsScannerTooltipVisible
    {
        get => isScannerTooltipVisible;
        private set => SetProperty(ref isScannerTooltipVisible, value);
    }

    public string ScannerTooltipText => "Scanner display " + ScannerPercentInput.Format(ScannerPercentSlider);

    private IRelayCommand? commitScannerTextCommand;

    /// <summary>Enter in the text field.</summary>
    public IRelayCommand CommitScannerTextCommand =>
        commitScannerTextCommand ??= new RelayCommand(() =>
            CommitScannerPercentage(ScannerPercentInput.Commit(ScannerPercentText, ScannerPercentSlider)));

    private IRelayCommand? revertScannerTextCommand;

    /// <summary>Escape in the text field: back to the stored value; like Enter it also turns
    /// the scan circles on ("either action ... unconditionally turns on" the overlay).</summary>
    public IRelayCommand RevertScannerTextCommand =>
        revertScannerTextCommand ??= new RelayCommand(() => CommitScannerPercentage(ScannerPercentSlider));

    /// <summary>Re-reads the stored value (after the map's own slider changed it).</summary>
    public void RefreshScannerDisplay()
    {
        ScannerPercentText = ScannerPercentInput.Format(ScannerPercentSlider);
        OnPropertyChanged(nameof(ScannerPercentSlider));
        OnPropertyChanged(nameof(ScannerTooltipText));
    }

    private void CommitScannerPercentage(int value)
    {
        if (writeScannerPercentage == null)
        {
            return;
        }

        int before = ScannerPercentSlider;
        writeScannerPercentage(value);
        RefreshScannerDisplay();
        if (ScannerPercentSlider != before)
        {
            ShowScannerTooltip();
        }
    }

    private void ShowScannerTooltip()
    {
        scannerLastChange = DateTime.UtcNow;
        IsScannerTooltipVisible = true;
        if (scannerTooltipTimer == null)
        {
            scannerTooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            scannerTooltipTimer.Tick += (_, _) =>
            {
                if (!ScannerPercentInput.IsTooltipVisible(scannerLastChange, DateTime.UtcNow))
                {
                    IsScannerTooltipVisible = false;
                    scannerTooltipTimer.Stop();
                }
            };
        }

        scannerTooltipTimer.Start();
    }
}
