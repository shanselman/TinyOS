using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;

namespace TinyOS_TimeMachine;

/// <summary>
/// Hosts the single Time Machine page. Owns window-level concerns only:
/// size, presentation (full screen) mode, and stopping the worker on close.
/// </summary>
public sealed partial class MainWindow : Window
{
    public static new MainWindow? Current { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Current = this;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Title = "TinyOS Time Machine";
        SizeToDisplay();

        RootFrame.Navigate(typeof(MainPage));
        AddShortcut(VirtualKey.F11, () => { SetPresentationMode(!IsPresentationMode); return true; });
        AddShortcut(VirtualKey.Escape, () => { if (!IsPresentationMode) return false; SetPresentationMode(false); return true; });
        Activated += OnActivated;
        Closed += OnClosed;
    }

    public bool IsPresentationMode => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    public void SetPresentationMode(bool on)
    {
        AppWindow.SetPresenter(on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        AppTitleBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        FocusPage();
    }

    // Window-level accelerators fire wherever keyboard focus is, unlike the page's key handler.
    private void AddShortcut(VirtualKey key, Func<bool> action)
    {
        var accelerator = new KeyboardAccelerator { Key = key };
        accelerator.Invoked += (_, e) => e.Handled = action();
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    // Switching presenters or clicking a non-focusable area can leave no focused element,
    // which silently swallows Space/F10; put focus back on the page.
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated &&
            Content?.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is null)
        {
            FocusPage();
        }
    }

    private void FocusPage() =>
        DispatcherQueue.TryEnqueue(() => (RootFrame.Content as MainPage)?.TakeKeyboardFocus());

    private void SizeToDisplay()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int titleBar = (int)(48 * (Content?.XamlRoot?.RasterizationScale ?? 1.0));
        int width = (int)(area.Width * 0.92);
        int height = width * 9 / 16 + titleBar;
        if (height > area.Height * 0.94)
        {
            height = (int)(area.Height * 0.94);
            width = (height - titleBar) * 16 / 9;
        }
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        (RootFrame.Content as MainPage)?.Shutdown();
    }
}
