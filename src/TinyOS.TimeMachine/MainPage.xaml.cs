using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TinyOS.Protocol;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace TinyOS_TimeMachine;

/// <summary>
/// The Time Machine scene. This partial owns input: buttons, toggles, disks, tabs,
/// keyboard shortcuts, and the two dialogs. Worker lifecycle lives in MainPage.Session.cs
/// and drawing lives in MainPage.Render.cs.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly bool _initialized;
    private bool _suppressToggleEvents;

    public MainPage()
    {
        InitializeComponent();
        InitializeSession();
        InitializeRender();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        _initialized = true;
        ShowDisk(_preset);
        _dirty = true;
    }

    #region Machine controls

    private void OnRunPause(object sender, RoutedEventArgs e) => RunPause();

    private void RunPause()
    {
        switch (_state)
        {
            case MachineState.Off:
            case MachineState.Completed:
            case MachineState.Fault:
                StartSession(CommandTypes.Run);
                break;
            case MachineState.Running:
            case MachineState.Blocked:
                Send(CommandTypes.Pause);
                break;
            default:
                Send(CommandTypes.Run);
                break;
        }
        _dirty = true;
    }

    private void OnStep(object sender, RoutedEventArgs e) => Step();

    private void Step()
    {
        if (_state is MachineState.Off or MachineState.Completed or MachineState.Fault)
            StartSession(CommandTypes.Step);
        else
            Send(CommandTypes.Step, new StepCommand(1));
        _dirty = true;
    }

    private void OnReset(object sender, RoutedEventArgs e) => Reset();

    private void Reset()
    {
        if (_state == MachineState.Off)
        {
            Note("The machine is off. Press Power to boot the disk.");
            return;
        }
        StartSession(null);
        Note("Reset · " + _preset.Title + " · back to the first instruction");
    }

    private void OnPower(object sender, RoutedEventArgs e)
    {
        if (_state == MachineState.Off) StartSession(null, warmUp: true);
        else PowerOff();
    }

    private void OnSpeedChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        Send(CommandTypes.SetSpeed, new SetSpeedCommand(SelectedSpeed));
        _dirty = true;
    }

    private double SelectedSpeed =>
        new[] { Speed1, Speed5, Speed20, SpeedFast }.FirstOrDefault(r => r.IsChecked == true) is { Tag: string tag }
            ? double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture)
            : 5;

    private string SpeedText => SelectedSpeed <= 0 ? "fast" : SelectedSpeed + "/s";

    private void OnDiskChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _preset = sender switch
        {
            RadioButton r when r == DiskSmall => Presets.SmallRam,
            RadioButton r when r == DiskComfortable => Presets.ComfortableRam,
            RadioButton r when r == DiskC => Presets.TakingTurns,
            _ => Presets.FirstProgram,
        };
        ShowDisk(_preset);
        StartSession(null, warmUp: true);
    }

    private void OnHoldSwapChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _suppressToggleEvents) return;
        bool on = HoldSwapToggle.IsChecked == true;
        _holdArmedThisRun = on;
        _holdFired = false;
        Send(CommandTypes.HoldOnNextSwap, new HoldOnNextSwapCommand(on));
        Note(on ? "Hold armed · pauses right after the next swap-file write or read" : "Hold disarmed");
    }

    private void OnEffectsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        ApplyEffects(EffectsToggle.IsChecked == true);
    }

    private void OnLegacyDumpsChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        bool on = LegacyDumpsToggle.IsChecked == true;
        if (_state != MachineState.Off)
        {
            StartSession(null);
            Note(on ? "Restarted with the 2002 register and memory dumps" : "Restarted with the notebook instead of dumps");
        }
        else
        {
            Note(on ? "2002 dumps will print on the next boot" : "Dumps off for the next boot");
        }
    }

    private void OnHexChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _hex = HexToggle.IsChecked == true;
        _dirty = true;
    }

    #endregion

    #region Notebook navigation

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        RegistersPage.Visibility = TabRegisters.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        MemoryPage.Visibility = TabMemory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SourcePage.Visibility = TabSource.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _scrollSourceToNext = true;
        _dirty = true;
    }

    private void OnFollowCpu(object sender, RoutedEventArgs e)
    {
        _selectedPid = null;
        _dirty = true;
    }

    private void OnFramesPrev(object sender, RoutedEventArgs e) => MoveFramePage(-1);

    private void OnFramesNext(object sender, RoutedEventArgs e) => MoveFramePage(+1);

    private void MoveFramePage(int delta)
    {
        int pages = FramePageCount;
        _followFrames = false;
        _framePage = Math.Clamp(_framePage + delta, 0, Math.Max(0, pages - 1));
        _selectedFrame = _framePage * FramesPerPage;
        _dirty = true;
    }

    #endregion

    #region Keyboard

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsDown(VirtualKey.Control);
        switch (e.Key)
        {
            case VirtualKey.Space when !ctrl:
                if (!e.KeyStatus.WasKeyDown) RunPause();
                e.Handled = true;
                break;
            case VirtualKey.F10:
                Step();
                e.Handled = true;
                break;
            case VirtualKey.R when ctrl:
                Reset();
                e.Handled = true;
                break;
            case VirtualKey.Number1 or VirtualKey.NumberPad1 when ctrl:
                TabRegisters.IsChecked = true;
                e.Handled = true;
                break;
            case VirtualKey.Number2 or VirtualKey.NumberPad2 when ctrl:
                TabMemory.IsChecked = true;
                e.Handled = true;
                break;
            case VirtualKey.Number3 or VirtualKey.NumberPad3 when ctrl:
                TabSource.IsChecked = true;
                e.Handled = true;
                break;
        }
    }

    // Pointer state keeps the focus rectangle hidden; Power is never disabled.
    public void TakeKeyboardFocus()
    {
        if (!RunPauseButton.Focus(FocusState.Pointer)) PowerButton.Focus(FocusState.Pointer);
    }

    private void OnPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        // Keep Space from also "clicking" whichever key cap has focus.
        if (e.Key == VirtualKey.Space) e.Handled = true;
    }

    #endregion

    #region Dialogs

    private async void OnTranscript(object sender, RoutedEventArgs e)
    {
        string text = _terminal.Transcript;
        // AcceptsReturn must be set before Text, or a single-line TextBox keeps only the first line.
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            IsReadOnly = true,
            PlaceholderText = "Nothing has been printed yet. Boot a disk first.",
            FontFamily = (FontFamily)Application.Current.Resources["MonoFont"],
            FontSize = 16,
            Width = 860,
            Height = 480,
        };
        box.Text = text;
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Transcript · exactly what TinyOS printed",
            Content = box,
            PrimaryButtonText = "Copy all",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = text.Length > 0,
        };
        Widen(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Note("Transcript copied to the clipboard");
        }
    }

    private async void OnAbout(object sender, RoutedEventArgs e)
    {
        var mono = (FontFamily)Application.Current.Resources["MonoFont"];
        var panel = new StackPanel { Spacing = 10, Width = 860 };

        void Heading(string text) => panel.Children.Add(new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 21, Margin = new Thickness(0, 6, 0, 0) });
        void Body(string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 17 });
        void Mono(string text) => panel.Children.Add(new TextBlock { Text = text, FontFamily = mono, FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });

        Heading("Real, from 2002");
        Body("The CPU, opcodes, scheduler, paging memory manager, and the sample programs are Scott's CST352 TinyOS from this repository. The engine runs in its own worker process and prints exactly what it printed in 2002.");
        string header = HeaderLines();
        Mono(header.Length > 0 ? header : "(Boot a disk to see the engine's own banner here.)");

        Heading("New in 2026");
        Body("A tiny observer interface (HostHooks.cs) the engine calls before and after each instruction and on every page swap, a console worker that reports snapshots as JSON lines, and this WinUI 3 window. The worker runs inside a private session folder because the 2002 memory manager writes its swap files to the current directory.");

        Heading("Visual fiction");
        Body("The beige case, curved glass, scanlines, glow, floppy disks, key caps, paper, and handwriting. Nothing in the notebook is invented: every register, byte, page, and swap file comes from the worker's snapshots.");

        Heading("Running now");
        var sb = new StringBuilder();
        sb.AppendLine("Engine:   " + (_ready?.EngineAssembly ?? "(not booted)"));
        sb.AppendLine("Worker:   " + (_ready is null ? "(not booted)" : _ready.Runtime + " · pid " + _ready.WorkerProcessId));
        sb.AppendLine("App:      " + RuntimeInformation.FrameworkDescription + " · Windows App SDK " + WindowsAppSdkVersion);
        sb.AppendLine("Commit:   " + CommitText);
        sb.AppendLine("Protocol: v" + ProtocolInfo.Version);
        if (_session is not null) sb.Append("Session:  " + _session.SessionDirectory);
        Mono(sb.ToString().TrimEnd());

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Provenance",
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        Widen(dialog);
        await dialog.ShowAsync();
    }

    // ContentDialog's template reads its 548px cap from theme dictionaries that shadow resource
    // overrides, so set the cap on the template part directly. Too narrow for a projector otherwise.
    private static void Widen(ContentDialog dialog)
    {
        dialog.Loaded += (_, _) =>
        {
            if (Find(dialog, "BackgroundElement") is FrameworkElement background)
            {
                background.MaxWidth = Math.Min(1000, dialog.XamlRoot.Size.Width - 48);
                background.MaxHeight = dialog.XamlRoot.Size.Height - 48;
            }
        };

        static DependencyObject? Find(DependencyObject parent, string name)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Name == name) return child;
                if (Find(child, name) is { } found) return found;
            }
            return null;
        }
    }

    private string HeaderLines()
    {
        var lines = _terminal.Transcript.Split('\n')
            .Take(12)
            .Where(l => l.Contains("CST352") || l.Contains("Copyright") || l.Contains("physical memory") || l.Contains("virtual (addressable)") || l.StartsWith("Scott", StringComparison.Ordinal));
        return string.Join("\n", lines).Trim();
    }

    private static string WindowsAppSdkVersion =>
        typeof(MainPage).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "WindowsAppSdkVersion")?.Value ?? "(unknown)";

    private static string CommitText
    {
        get
        {
            string? info = typeof(MainPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            int plus = info?.IndexOf('+') ?? -1;
            if (plus < 0) return "(not recorded in this build)";
            string sha = info![(plus + 1)..];
            return sha.Length > 12 ? sha[..12] : sha;
        }
    }

    #endregion
}
