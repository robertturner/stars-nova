using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client.Map;

namespace Nova.Avalonia.Views.Panels;

public partial class StarMapDocumentView : UserControl
{
    private StarMapDocumentViewModel? viewModel;

    // The scale the map is currently drawn at, so a zoom change can recentre on the world point
    // that was at the centre of the viewport before it (see OnViewModelPropertyChanged).
    private double currentScale = 1.0;

    private TopLevel? keyTopLevel;

    /// <summary>The windowed (desktop) UI gets the keyboard shortcuts, Find and the right-click
    /// picker; the Android port keeps its touch-first equivalents.</summary>
    private static bool IsWindowed => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

    public StarMapDocumentView()
    {
        InitializeComponent();

        // Tunnel, not the default Bubble a plain PointerPressed="..." XAML attribute on each
        // marker's own Button would use - a Button marks its own internal press handling
        // Handled during the tunnel phase before bubbling back out. Attaching here, on the shared
        // ancestor, ahead of that swallowing, is what lets this resolve a star/fleet tie BEFORE
        // either marker's own Button click can fire - see
        // StarMapDocumentViewModel.FindNearestStarOrFleetMarker for the actual arbitration.
        MapPanel.AddHandler(InputElement.PointerPressedEvent, OnMapPointerPressedTunnel, RoutingStrategies.Tunnel);

        FindButton.IsVisible = IsWindowed;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (viewModel != null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.CenterOnRequested -= OnCenterOnRequested;
            viewModel.Search.PropertyChanged -= OnSearchPropertyChanged;
        }

        viewModel = DataContext as StarMapDocumentViewModel;
        if (viewModel != null)
        {
            currentScale = viewModel.Zoom;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.CenterOnRequested += OnCenterOnRequested;
            viewModel.Search.PropertyChanged += OnSearchPropertyChanged;
        }
    }

    // However Find was opened (Ctrl+F, the Find button, View > Find), the query box gets the
    // focus.
    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StarMapSearchViewModel.IsOpen) && viewModel?.Search.IsOpen == true && IsWindowed)
        {
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Background);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The digit keys and Ctrl+F act on the map wherever the focus is in the window
        // (behavior-specs-10/client-interface.md, "Reusable control behavior": the digit keys
        // are dispatched to the view-option slots "rather than to whichever control currently has
        // focus") - except a text box, where typing a number must still type it.
        if (IsWindowed)
        {
            keyTopLevel = TopLevel.GetTopLevel(this);
            keyTopLevel?.AddHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        keyTopLevel?.RemoveHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown);
        keyTopLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel == null || e.Handled)
        {
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            viewModel.Search.IsOpen = true;
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Background);
            e.Handled = true;
            return;
        }

        if (keyTopLevel?.FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        if (e.Key >= Key.D0 && e.Key <= Key.D9 && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            if (viewModel.HandleDigitKey(e.Key - Key.D0, shift))
            {
                e.Handled = true;
            }
        }
    }

    private void OnMapPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (viewModel == null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(MapPanel);
        if (point.Properties.IsRightButtonPressed)
        {
            ShowObjectPicker(point.Position);
            e.Handled = true;
            return;
        }

        // Shift+left-click with a mass-driver planet selected sets its packet destination
        // (desktop only - a touchscreen has no Shift; the Inspector has the same control).
        if (IsWindowed && point.Properties.IsLeftButtonPressed && (e.KeyModifiers & KeyModifiers.Shift) != 0
            && viewModel.TrySetPacketDestinationAt(point.Position.X, point.Position.Y))
        {
            e.Handled = true;
            return;
        }

        MapMarkerViewModel? nearest = viewModel.FindNearestStarOrFleetMarker(point.Position.X, point.Position.Y);
        if (nearest != null)
        {
            nearest.SelectCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// The right-click object-disambiguation picker (behavior-specs-10/client-interface.md, "Map
    /// canvas"): every fleet at the clicked position, then every planet there, divided, with the
    /// current selection checked. Choosing a line only selects that object. Names go in a
    /// TextBlock header so an underscore in a name is never read as an access key (the original
    /// escapes "&amp;" for the same reason).
    /// </summary>
    private void ShowObjectPicker(Point position)
    {
        if (viewModel == null)
        {
            return;
        }

        var lines = viewModel.BuildPickerLines(position.X, position.Y);
        if (lines.Count == 0)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (MapPickerLine line in lines)
        {
            if (line.SeparatorBefore)
            {
                menu.Items.Add(new Separator());
            }

            object item = line.Entry.Item;
            var menuItem = new MenuItem
            {
                Header = new TextBlock { Text = line.Entry.Name },
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = line.IsChecked,
            };
            menuItem.Click += (_, _) => viewModel?.SelectObject(item);
            menu.Items.Add(menuItem);
        }

        menu.Open(MapPanel);
    }

    // Plain wheel steps through the 9 fixed zoom levels. Marking the event handled stops the
    // ScrollViewer from also scrolling on the same wheel input.
    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (viewModel != null)
        {
            viewModel.ZoomLevel += e.Delta.Y > 0 ? 1 : -1;
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (viewModel == null || e.PropertyName != nameof(StarMapDocumentViewModel.Zoom))
        {
            return;
        }

        // Zoom recentres on the same world point (client-interface.md, "Navigation controls":
        // the map "does not simply rescale in place around its top-left corner").
        double newScale = viewModel.Zoom;
        (double x, double y) = MapZoom.RecenteredOffset(MapScroll.Offset.X, MapScroll.Offset.Y,
            MapScroll.Viewport.Width, MapScroll.Viewport.Height, currentScale, newScale);
        currentScale = newScale;
        ScrollAfterLayout(x, y);
    }

    private void OnCenterOnRequested(double worldX, double worldY)
    {
        (double x, double y) = MapZoom.CentredOn(worldX, worldY, MapScroll.Viewport.Width, MapScroll.Viewport.Height, currentScale);
        ScrollAfterLayout(x, y);
    }

    // The new extent only exists once the scale change has been laid out, so the offset is set
    // after layout (the ScrollViewer clamps it to the scrollable range).
    private void ScrollAfterLayout(double x, double y)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MapScroll.UpdateLayout();
            MapScroll.Offset = new Vector(System.Math.Max(0, x), System.Math.Max(0, y));
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// File > Print Map (client-interface.md command 213): renders the whole map at the current
    /// zoom and writes it tiled over across x down pages, one PNG image per page
    /// (Nova.Client.Shell.MapPrintLayout - this client has no printer path, so a page is an
    /// image file). Returns the files written; throws when the map cannot be rendered.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string> WriteMapPages(string folder, string raceName, int year, int across, int down)
    {
        int width = (int)System.Math.Ceiling(MapScaleHost.Bounds.Width);
        int height = (int)System.Math.Ceiling(MapScaleHost.Bounds.Height);
        if (width <= 0 || height <= 0)
        {
            throw new System.InvalidOperationException("The map has not been drawn yet.");
        }

        var written = new System.Collections.Generic.List<string>();
        var background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#0d1117"));
        using var full = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
        full.Render(MapScaleHost);
        foreach (Nova.Client.Shell.MapPrintPage page in Nova.Client.Shell.MapPrintLayout.Pages(width, height, across, down))
        {
            using var pageBitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(page.Width, page.Height));
            using (global::Avalonia.Media.DrawingContext context = pageBitmap.CreateDrawingContext())
            {
                var target = new Rect(0, 0, page.Width, page.Height);
                context.FillRectangle(background, target);
                context.DrawImage(full, new Rect(page.X, page.Y, page.Width, page.Height), target);
            }

            string path = Nova.Client.Shell.MapPrintLayout.PageFileName(folder, raceName, year, page.Number);
            pageBitmap.Save(path);
            written.Add(path);
        }

        return written;
    }

    private void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        viewModel?.Search.GoCommand.Execute(null);
    }

    // Avalonia's built-in Holding gesture, same pattern ShipDesignView.axaml.cs's
    // OnComponentItemHolding uses.
    private void OnFleetMarkerHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StarMapFleetViewModel fleet } || viewModel == null)
        {
            return;
        }

        switch (e.HoldingState)
        {
            case HoldingState.Started:
                viewModel.HullViewer.Show(fleet.PrimaryDesign);
                break;
            case HoldingState.Completed:
            case HoldingState.Canceled:
                viewModel.HullViewer.Hide();
                break;
        }
    }
}
