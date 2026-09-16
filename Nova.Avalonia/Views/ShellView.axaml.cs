using Avalonia.Controls;
using Nova.Avalonia.ViewModels;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.Views;

/// <summary>
/// The single-view host used on platforms with no independent Windows (Android, and any other
/// ISingleViewApplicationLifetime/IActivityApplicationLifetime host) - a stand-in for the
/// Window-swapping App.axaml.cs already does for desktop (OpenGameWindow -> MainWindow, plus
/// AboutWindow as a modal). This swaps CONTENT within one root Control instead: start on
/// OpenGameView, swap to MainView once a game opens, swap to AboutView (with a "back" affordance
/// instead of "close") when About is requested from there, and back to MainView again. Built
/// entirely from the same portable Views/ViewModels the desktop head already uses - see
/// OpenGameView/MainView/AboutView's own comments for why each raises events instead of acting
/// directly, which is exactly what makes this single-view host possible without touching them.
/// </summary>
public partial class ShellView : UserControl
{
    private MobileMainView? mainView;

    public ShellView()
    {
        InitializeComponent();

        // See App.ApplySavedThemePreference's own comment for why this needs calling again
        // here, specifically - on Android, App.OnFrameworkInitializationCompleted's own call
        // runs too early to see PlatformHooks.LoadThemePreference at all.
        App.ApplySavedThemePreference();

        ShowOpenGame();

        // See PlatformHooks.TryHandleBackRequest's own comment for why this exists at all: this
        // single-Activity host has no back *stack* to fall back on, so without this, Android's OS
        // back button/gesture would just exit the whole app from any screen. Only one ShellView
        // is ever alive at a time in this app, so registering unconditionally here (rather than
        // on attach/detach) is enough.
        PlatformHooks.TryHandleBackRequest = TryGoBack;
    }

    /// <summary>Steps back one level in whatever's currently showing, mirroring that screen's own
    /// on-screen "back"/"close" control. Returns false when the current screen has nowhere to go
    /// back to (the OpenGame choices screen, or the game itself) - the caller (Android's back
    /// button/gesture) should fall back to its own default in that case.</summary>
    public bool TryGoBack()
    {
        switch (ContentHost.Content)
        {
            case AboutView:
                ShowMain();
                return true;

            case OpenGameView openGameView:
                return openGameView.TryGoBack();

            default:
                return false;
        }
    }

    private void ShowOpenGame()
    {
        var openGameView = new OpenGameView();
        openGameView.GameOpened += OnGameOpened;
        ContentHost.Content = openGameView;
    }

    private void OnGameOpened(ClientData clientState) => ShowMain(clientState, jumpToMessagesIfAny: false);

    /// <summary>The same screen construction as opening a game fresh, except a turn that just
    /// advanced (see GameShellViewModelBase.TurnAdvanced) also jumps straight to the Messages page
    /// when it produced any - "this turn's events" (see MessagesViewModel's own comment) - rather
    /// than leaving the player to notice the burger menu's Messages row themselves. Only on a
    /// genuine turn advance, not the initial open, so opening a game you're resuming mid-session
    /// doesn't get redirected away from the Map the moment it loads.</summary>
    private void OnTurnAdvanced(ClientData clientState) => ShowMain(clientState, jumpToMessagesIfAny: true);

    private void ShowMain(ClientData clientState, bool jumpToMessagesIfAny)
    {
        // MobileMainViewModel/MobileMainView, not MainViewModel/MainView - the desktop dock
        // layout doesn't translate to a phone at all (see MobileMainViewModel's own comment).
        // This single-view host is only ever used on Android, so it always wants the mobile
        // screen; the desktop head's own MainWindow.axaml.cs is untouched and keeps using
        // MainViewModel/MainView directly.
        var viewModel = new MobileMainViewModel(clientState);
        if (jumpToMessagesIfAny && clientState.Messages.Count > 0)
        {
            viewModel.SelectedPageLabel = "Messages";
        }

        viewModel.AboutRequested += ShowAbout;
        viewModel.TurnAdvanced += OnTurnAdvanced;
        viewModel.GameCloseRequested += ShowOpenGame;

        mainView = new MobileMainView { DataContext = viewModel };
        ContentHost.Content = mainView;
    }

    private void ShowAbout()
    {
        var aboutView = new AboutView();
        aboutView.CloseRequested += ShowMain;
        ContentHost.Content = aboutView;
    }

    private void ShowMain()
    {
        ContentHost.Content = mainView;
    }
}
