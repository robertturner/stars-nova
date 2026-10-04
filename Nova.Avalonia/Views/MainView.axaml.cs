using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Nova.Avalonia.ViewModels;
using Nova.Client.Shell;

namespace Nova.Avalonia.Views;

/// <summary>
/// The actual game screen (menu, status bar, docked panels) - a plain UserControl so it can be
/// hosted either as a desktop Window's content (MainWindow) or as content swapped into a
/// single-view shell (Android, once that exists). See ShowAboutCommand/AboutRequested on
/// MainViewModel for how "About" is handled per-host instead of living here.
///
/// Also fills the File menu's Recent Files items (between its two adjacent separators) each
/// time the menu opens, and builds the toolbar's customisation menus.
/// </summary>
public partial class MainView : UserControl
{
    private readonly List<MenuItem> recentItems = new List<MenuItem>();

    public MainView()
    {
        InitializeComponent();
        FileMenu.SubmenuOpened += (_, _) => RebuildRecentFiles();
        ToolbarStrip.ContextRequested += OnToolbarStripContextRequested;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // ---------------- recent files ----------------

    /// <summary>Replaces the recent-file items between the RecentFilesStart and RecentFilesEnd
    /// separators ("_1 path" ... "_9 path"; none at all when the list is empty, leaving the two
    /// separators adjacent, as in the original's menu resource).</summary>
    private void RebuildRecentFiles()
    {
        MainViewModel? viewModel = ViewModel;
        if (viewModel == null)
        {
            return;
        }

        foreach (MenuItem item in recentItems)
        {
            FileMenu.Items.Remove(item);
        }

        recentItems.Clear();
        viewModel.RefreshRecentFiles();

        int insertAt = FileMenu.Items.IndexOf(RecentFilesEnd);
        foreach (RecentFileEntry entry in viewModel.RecentFileEntries)
        {
            var item = new MenuItem
            {
                Header = entry.Caption,
                Command = viewModel.OpenRecentFileCommand,
                CommandParameter = entry.Path,
            };
            FileMenu.Items.Insert(insertAt++, item);
            recentItems.Add(item);
        }
    }

    // ---------------- toolbar customisation ----------------

    private void OnToolbarButtonContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        MainViewModel? viewModel = ViewModel;
        if (viewModel == null || sender is not Control { DataContext: ToolbarButtonViewModel button } control)
        {
            return;
        }

        var menu = new ContextMenu();
        AddItem(menu, "Move _Up", () => viewModel.MoveToolbarButton(button.Id, up: true));
        AddItem(menu, "Move _Down", () => viewModel.MoveToolbarButton(button.Id, up: false));
        AddItem(menu, "_Remove", () => viewModel.RemoveToolbarButton(button.Id));
        menu.Items.Add(new Separator());
        AddStripItems(menu, viewModel);
        menu.Open(control);
        e.Handled = true;
    }

    private void OnToolbarStripContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        MainViewModel? viewModel = ViewModel;
        if (viewModel == null || e.Handled)
        {
            return;
        }

        var menu = new ContextMenu();
        AddStripItems(menu, viewModel);
        menu.Open(ToolbarStrip);
        e.Handled = true;
    }

    private static void AddStripItems(ContextMenu menu, MainViewModel viewModel)
    {
        var add = new MenuItem { Header = "_Add Button" };
        IReadOnlyList<ToolbarButtonInfo> hidden = viewModel.HiddenToolbarButtons;
        foreach (ToolbarButtonInfo info in hidden)
        {
            string id = info.Id;
            var item = new MenuItem { Header = new TextBlock { Text = info.Tooltip } };
            item.Click += (_, _) => viewModel.AddToolbarButton(id);
            add.Items.Add(item);
        }

        add.IsEnabled = hidden.Any();
        menu.Items.Add(add);
        AddItem(menu, "Re_set Toolbar", viewModel.ResetToolbar);
        AddItem(menu, "_Hide Toolbar", () => viewModel.ShowToolbar = false);
    }

    private static void AddItem(ContextMenu menu, string header, System.Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
