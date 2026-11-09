using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TinyOS.Protocol;
using Windows.UI.Text;
using Windows.UI.ViewManagement;

namespace TinyOS_TimeMachine;

/// <summary>
/// Drawing. Everything shown here is read from the latest worker snapshot, the last
/// reported instruction, and the swap events. Elements are built once and updated in
/// place so Fast mode stays smooth.
/// </summary>
public sealed partial class MainPage
{
    private const int FramesPerPage = 8;
    private const double GlassCornerRadius = 34;

    private uint? _selectedPid;
    private uint? _sourcePid;
    private bool _sourcePinned;
    private int _framePage;
    private bool _followFrames = true;
    private int _selectedFrame;
    private bool _hex;
    private bool _scrollSourceToNext = true;

    private int _columns = 65;
    private int _rows = 16;
    private int _renderedTerminalVersion = -1;
    private bool _terminalLayoutChanged = true;
    private SpriteVisual? _glow;
    private CompositionRoundedRectangleGeometry? _glassClip;
    private bool _effectsOn = true;
    private readonly bool _animations = new UISettings().AnimationsEnabled;

    private Brush _ink = null!, _pencil = null!, _accent = null!, _wash = null!;
    private readonly Brush _clear = new SolidColorBrush(Colors.Transparent);
    private readonly Brush _selectedRow = new SolidColorBrush(Windows.UI.Color.FromArgb(0x2E, 0x24, 0x3E, 0x52));
    private readonly Brush _ledOff = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3F, 0x3B, 0x32));
    private readonly Brush _ledOn = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x5B, 0xE0, 0x7A));
    private readonly Brush _ledBusy = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF2, 0xB8, 0x4B));
    private readonly Brush _ledFault = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xF0, 0x4B, 0x3A));
    private FontFamily _mono = null!, _hand = null!;

    private sealed class RegisterCell
    {
        public required string Label;
        public required int Index;
        public required Border Root;
        public required TextBlock Value;
    }

    private sealed class FrameRow
    {
        public required Button Button;
        public required TextBlock Label;
        public required TextBlock Owner;
        public required Border[] Cells;
        public required TextBlock[] Values;
        public int Frame;
    }

    private sealed class DetailCell
    {
        public required TextBlock Address;
        public required Border Box;
        public required TextBlock Value;
    }

    private sealed class ProcessRow
    {
        public required Button Button;
        public required TextBlock Marker;
        public required TextBlock Name;
        public required TextBlock State;
        public uint Pid;
    }

    private sealed class SourceRow
    {
        public required Border Root;
        public required TextBlock Marker;
        public required uint Address;
    }

    private static readonly (string Label, int Index, string Help)[] RegisterLayout =
    [
        ("r1", 1, "r1 · general purpose"), ("r2", 2, "r2 · general purpose"), ("r3", 3, "r3 · general purpose"),
        ("r4", 4, "r4 · general purpose"), ("r5", 5, "r5 · general purpose"), ("r6", 6, "r6 · general purpose"),
        ("r7", 7, "r7 · general purpose"), ("pid", 8, "r8 · id of the process that owns these registers"), ("data", 9, "r9 · data pointer"),
        ("ip", 11, "r11 · instruction pointer: address of the next instruction"), ("sp", 10, "r10 · stack pointer"), ("flags", -1, "sign flag (sf) and zero flag (zf), set by compares"),
    ];

    private readonly List<RegisterCell> _registerCells = new();
    private readonly List<FrameRow> _frameRows = new();
    private readonly List<DetailCell> _detailCells = new();
    private readonly List<ProcessRow> _processRows = new();
    private readonly List<SourceRow> _sourceRows = new();
    private int _renderedSwapsVersion = -1;
    private int _renderedPidButtonsVersion = -1;
    private (uint? Pid, int Version) _renderedSource = (null, -1);
    private SourceRow? _scrolledTo;

    private static object Res(string key) => Application.Current.Resources[key];

    private void InitializeRender()
    {
        _ink = (Brush)Res("InkBrush");
        _pencil = (Brush)Res("PencilBrush");
        _accent = (Brush)Res("AccentBrush");
        _wash = (Brush)Res("AccentWashBrush");
        _mono = (FontFamily)Res("MonoFont");
        _hand = (FontFamily)Res("HandFont");

        BuildRegisterCells();
        BuildFrameRows();
        BuildFrameDetail();

        Paper.SizeChanged += (_, e) => Ruling.Data = SceneArt.Ruling(e.NewSize.Width, e.NewSize.Height, 118, 34);
        ScreenContent.SizeChanged += (_, _) => UpdateTerminalGeometry();
        MeasureText.SizeChanged += (_, _) => UpdateTerminalGeometry();
        Glass.SizeChanged += (_, _) => UpdateGlass();
        TerminalText.SizeChanged += (_, _) => UpdateGlowLayout();
        Loaded += (_, _) =>
        {
            UpdateTerminalGeometry();
            UpdateGlass();
            ApplyEffects(EffectsToggle.IsChecked == true);
            RunPauseButton.Focus(FocusState.Programmatic);
        };
    }

    #region Building elements

    private TextBlock Text(string text, FontFamily font, double size, Brush brush) =>
        new() { Text = text, FontFamily = font, FontSize = size, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };

    private void BuildRegisterCells()
    {
        RegisterGrid.ColumnDefinitions.Clear();
        RegisterGrid.RowDefinitions.Clear();
        for (int c = 0; c < 3; c++) RegisterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 4; r++) RegisterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < RegisterLayout.Length; i++)
        {
            var (label, index, help) = RegisterLayout[i];
            // Sized so a 10-digit uint, 0x-hex, or "sf 0 zf 0" fits in a third of the notebook.
            var value = Text("—", _mono, 18, _ink);
            var labelText = Text(label, _hand, 20, _pencil);
            var grid = new Grid { ColumnSpacing = 6 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(labelText);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);
            var root = new Border { Padding = new Thickness(6, 3, 4, 3), CornerRadius = new CornerRadius(4), Background = _clear, Child = grid };
            ToolTipService.SetToolTip(root, help);
            Grid.SetRow(root, i / 3);
            Grid.SetColumn(root, i % 3);
            RegisterGrid.Children.Add(root);
            _registerCells.Add(new RegisterCell { Label = label, Index = index, Root = root, Value = value });
        }
    }

    private void BuildFrameRows()
    {
        for (int r = 0; r < FramesPerPage; r++)
        {
            var grid = new Grid { ColumnSpacing = 6, Height = 27 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Text("", _mono, 15, _ink);
            label.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            var owner = Text("", _hand, 16, _pencil);
            owner.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(owner, 1);
            var bytes = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(bytes, 2);
            var cells = new Border[16];
            var values = new TextBlock[16];
            for (int b = 0; b < 16; b++)
            {
                values[b] = new TextBlock { FontFamily = _mono, FontSize = 13, Foreground = _ink, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                cells[b] = new Border { Width = 23, Height = 23, CornerRadius = new CornerRadius(2), Background = _clear, Child = values[b] };
                bytes.Children.Add(cells[b]);
            }
            grid.Children.Add(label);
            grid.Children.Add(owner);
            grid.Children.Add(bytes);
            var button = new Button { Style = (Style)Res("PaperRowStyle"), Content = grid, Padding = new Thickness(2, 0, 2, 0) };
            var row = new FrameRow { Button = button, Label = label, Owner = owner, Cells = cells, Values = values };
            button.Click += (_, _) =>
            {
                _selectedFrame = row.Frame;
                _followFrames = false;
                _dirty = true;
            };
            FrameList.Children.Add(button);
            _frameRows.Add(row);
        }
    }

    private void BuildFrameDetail()
    {
        for (int c = 0; c < 8; c++) FrameDetailBytes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < 2; r++) FrameDetailBytes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < 16; i++)
        {
            var address = Text("", _mono, 11, _pencil);
            address.HorizontalAlignment = HorizontalAlignment.Center;
            var value = Text("", _mono, 21, _ink);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            var box = new Border { CornerRadius = new CornerRadius(3), Background = _clear, Margin = new Thickness(2, 0, 2, 0), Child = value, MinHeight = 30 };
            var stack = new StackPanel { Spacing = 0 };
            stack.Children.Add(address);
            stack.Children.Add(box);
            Grid.SetRow(stack, i / 8);
            Grid.SetColumn(stack, i % 8);
            FrameDetailBytes.Children.Add(stack);
            _detailCells.Add(new DetailCell { Address = address, Box = box, Value = value });
        }
    }

    private ProcessRow AddProcessRow()
    {
        var grid = new Grid { ColumnSpacing = 8, Height = 34 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var marker = Text("", Microsoft.UI.Xaml.Media.FontFamily.XamlAutoFontFamily, 16, _accent);
        var name = Text("", _mono, 17, _ink);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 1);
        var state = Text("", _hand, 18, _pencil);
        state.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(state, 2);
        grid.Children.Add(marker);
        grid.Children.Add(name);
        grid.Children.Add(state);
        var button = new Button { Style = (Style)Res("PaperRowStyle"), Content = grid };
        var row = new ProcessRow { Button = button, Marker = marker, Name = name, State = state };
        button.Click += (_, _) =>
        {
            _selectedPid = _snapshot?.CurrentPid == row.Pid || _selectedPid == row.Pid ? null : row.Pid;
            _dirty = true;
        };
        ProcessList.Children.Add(button);
        _processRows.Add(row);
        return row;
    }

    #endregion

    #region Scene effects

    private void UpdateTerminalGeometry()
    {
        double width = ScreenContent.ActualWidth;
        double height = ScreenContent.ActualHeight;
        if (width <= 0 || height <= 0) return;
        double charWidth = MeasureText.ActualWidth > 0 ? MeasureText.ActualWidth / 10 : 14.06;
        int columns = Math.Max(20, (int)Math.Floor(width / charWidth));
        double lineHeight = TerminalText.LineHeight > 0 ? TerminalText.LineHeight : 30;
        int rows = Math.Max(4, (int)Math.Floor(height / lineHeight));
        if (columns == _columns && rows == _rows) return;
        _columns = columns;
        _rows = rows;
        _terminalLayoutChanged = true;
        _dirty = true;
    }

    private void UpdateGlass()
    {
        double width = Glass.ActualWidth;
        double height = Glass.ActualHeight;
        if (width <= 0 || height <= 0) return;
        Scanlines.Data = SceneArt.Scanlines(width, height);

        var visual = ElementCompositionPreview.GetElementVisual(Glass);
        if (_glassClip is null)
        {
            _glassClip = visual.Compositor.CreateRoundedRectangleGeometry();
            visual.Clip = visual.Compositor.CreateGeometricClip(_glassClip);
        }
        _glassClip.Size = new Vector2((float)width, (float)height);
        _glassClip.CornerRadius = new Vector2(_effectsOn ? (float)GlassCornerRadius : 0f);
    }

    private void ApplyEffects(bool on)
    {
        _effectsOn = on;
        var visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Scanlines.Visibility = visibility;
        Vignette.Visibility = visibility;
        Reflection.Visibility = visibility;
        Glass.CornerRadius = new CornerRadius(on ? GlassCornerRadius : 0);
        UpdateGlass();
        if (on) EnsureGlow();
        else RemoveGlow();
    }

    private void EnsureGlow()
    {
        if (_glow is not null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(GlowHost).Compositor;
        var shadow = compositor.CreateDropShadow();
        var mask = TerminalText.GetAlphaMask();
        // The mask surface ignores the scene's Viewbox zoom; stretch it so the glow sits exactly on the text.
        if (mask is CompositionSurfaceBrush surfaceMask) surfaceMask.Stretch = CompositionStretch.Fill;
        shadow.Mask = mask;
        shadow.Color = (Windows.UI.Color)Res("PhosphorColor");
        shadow.BlurRadius = 9f;
        shadow.Opacity = 0.6f;
        shadow.Offset = Vector3.Zero;
        _glow = compositor.CreateSpriteVisual();
        _glow.Shadow = shadow;
        ElementCompositionPreview.SetElementChildVisual(GlowHost, _glow);
        UpdateGlowLayout();
    }

    private void RemoveGlow()
    {
        if (_glow is null) return;
        ElementCompositionPreview.SetElementChildVisual(GlowHost, null);
        _glow.Dispose();
        _glow = null;
    }

    private void UpdateGlowLayout()
    {
        if (_glow is null || TerminalText.ActualWidth <= 0) return;
        var origin = TerminalText.TransformToVisual(GlowHost).TransformPoint(new Windows.Foundation.Point(0, 0));
        _glow.Size = new Vector2((float)TerminalText.ActualWidth, (float)TerminalText.ActualHeight);
        _glow.Offset = new Vector3((float)origin.X, (float)origin.Y, 0);
    }

    private void PlayWarmUp(bool full)
    {
        if (!_animations || !_effectsOn) return;
        var storyboard = new Storyboard();
        var flash = new DoubleAnimation
        {
            From = full ? 0.55 : 0.22,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(full ? 750 : 320),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 3 },
        };
        Storyboard.SetTarget(flash, WarmUp);
        Storyboard.SetTargetProperty(flash, "Opacity");
        storyboard.Children.Add(flash);

        if (full)
        {
            // Old CRTs bloom from a bright horizontal line.
            var scale = new ScaleTransform { CenterX = ScreenContent.ActualWidth / 2, CenterY = ScreenContent.ActualHeight / 2, ScaleY = 0.01 };
            ScreenContent.RenderTransform = scale;
            var grow = new DoubleAnimation
            {
                From = 0.01,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(420),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 },
            };
            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, "ScaleY");
            storyboard.Children.Add(grow);
        }
        storyboard.Begin();
    }

    // The 2002 XmlTextWriter writes one long line; indent it so the bytes are readable on stage.
    private static string IndentXml(string xml)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            return (doc.Declaration is { } declaration ? declaration + "\n" : "") + doc.Root;
        }
        catch (System.Xml.XmlException)
        {
            return xml;
        }
    }

    private void ShowTripCard(SwapRecord trip)
    {
        var p = trip.Payload;
        uint pageSize = _snapshot?.PageSize is > 0 and var ps ? ps : 16;
        uint frame = p.Page.AddrPhysical / pageSize;
        string owner = PageOwner(p.Page, pageSize);
        string during = p.DuringPid is uint d ? "P" + d : "the OS";

        TripFileName.Text = p.FileName;
        TripCaption.Text = trip.IsOut
            ? $"Clock {p.Clock}: {during} needed room, so {owner} (virtual page {p.Page.PageNumber}) left frame {frame} and the 2002 code wrote it to disk."
            : $"Clock {p.Clock}: {owner} (virtual page {p.Page.PageNumber}) came back from disk into frame {frame}.";
        TripXml.Text = IndentXml(p.Xml ?? (_swapXml.TryGetValue(p.FileName, out var xml) ? xml : "(contents were not captured)"));
        AutomationProperties.SetName(TripCard, "Swap file " + p.FileName + ". " + TripCaption.Text);

        TripCard.Visibility = Visibility.Visible;
        if (!_animations)
        {
            TripCard.Opacity = 1;
            TripCardTransform.TranslateY = 0;
            return;
        }
        var storyboard = new Storyboard();
        var slide = new DoubleAnimation { From = 60, To = 0, Duration = TimeSpan.FromMilliseconds(420), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(slide, TripCardTransform);
        Storyboard.SetTargetProperty(slide, "TranslateY");
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(260) };
        Storyboard.SetTarget(fade, TripCard);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }

    private void HideTripCard() => TripCard.Visibility = Visibility.Collapsed;

    private void ShowDisk(Preset preset)
    {
        DiskTitle.Text = preset.Title;
        DiskCaption.Text = preset.Caption;
        _dirty = true;
    }

    #endregion

    #region Render

    private void Render()
    {
        RenderTerminal();
        RenderStatus();
        if (RegistersPage.Visibility == Visibility.Visible) RenderRegisters();
        if (MemoryPage.Visibility == Visibility.Visible) RenderMemory();
        if (SourcePage.Visibility == Visibility.Visible) RenderSource();
    }

    private void RenderTerminal()
    {
        if (_terminal.Version == _renderedTerminalVersion && !_terminalLayoutChanged) return;
        _renderedTerminalVersion = _terminal.Version;
        _terminalLayoutChanged = false;
        TerminalText.Text = _terminal.Screen(_columns, _rows);
    }

    private void RenderStatus()
    {
        var snap = _snapshot;
        string clock = snap is null ? "" : " · clock " + snap.Clock;
        string count = " · " + _totalInstructions + (_totalInstructions == 1 ? " instruction" : " instructions");
        string line = _state switch
        {
            MachineState.Off => "OFF · choose a disk, then press Power",
            MachineState.Booting => "BOOTING · " + (_bootedPreset ?? _preset).Title,
            MachineState.Paused when _pauseReason == "loaded" => "LOADED · " + ProgramsText + " · Run (Space) or Step (F10)",
            MachineState.Paused => "PAUSED · " + PauseReasonText + clock + count,
            MachineState.Running => "RUNNING · " + SpeedText + clock + count,
            MachineState.Blocked => "BLOCKED · every process is waiting" + clock,
            MachineState.Completed => "COMPLETED · No Processes" + count + clock,
            MachineState.Fault => "FAULT · " + _faultText,
            _ => "",
        };
        StatusText.Text = _note ?? line;

        bool running = _state is MachineState.Running or MachineState.Blocked;
        RunPauseIcon.Glyph = running ? "\uE769" : "\uE768";
        RunPauseLabel.Text = running ? "Pause" : "Run";
        AutomationProperties.SetName(RunPauseButton, running ? "Pause" : "Run");

        PowerLed.Fill = _state switch
        {
            MachineState.Off => _ledOff,
            MachineState.Fault => _ledFault,
            MachineState.Booting or MachineState.Blocked => _ledBusy,
            _ => _ledOn,
        };
    }

    private string PauseReasonText => _pauseReason switch
    {
        "step" => "stepped",
        "user" => "paused",
        "swap" => "held right after a swap",
        _ => _pauseReason,
    };

    private string ProgramsText => _listings.Count switch
    {
        0 => "no programs",
        1 => _listings[0].FileName,
        _ => _listings.Count + " programs",
    };

    private string FileFor(uint pid) => _listings.FirstOrDefault(l => l.Pid == pid)?.FileName ?? "?";

    private SourceLine? LineAt(uint pid, uint address) =>
        _listings.FirstOrDefault(l => l.Pid == pid)?.Lines.FirstOrDefault(l => l.Address == address);

    private string Fmt(uint value) => SceneArt.FormatRegister(value, _hex);

    private static string RegisterName(int index) => index switch { 8 => "pid", 9 => "data", 10 => "sp", 11 => "ip", _ => "r" + index };

    private void RenderRegisters()
    {
        var snap = _snapshot;
        var preset = _state == MachineState.Off ? _preset : _bootedPreset ?? _preset;
        DiskTitle.Text = preset.Title;
        DiskCaption.Text = preset.Caption;

        CpuLine.Text = _state switch
        {
            MachineState.Off => "The machine is off. Press Power, or tap a disk.",
            MachineState.Fault when snap is null => "The worker could not run this disk.",
            _ when snap is null => "Booting…",
            _ when snap.CurrentPid is uint pid => $"P{pid} ({FileFor(pid)}) is on the CPU",
            MachineState.Completed => "Every process has exited.",
            MachineState.Paused when _pauseReason == "loaded" => "Loaded. Nothing has run yet.",
            _ => "No process is on the CPU right now.",
        };

        var last = _lastInstruction;
        if (last is null)
        {
            LastLine.Text = "nothing yet";
            ToolTipService.SetToolTip(LastLine, null);
        }
        else
        {
            var line = LineAt(last.Pid, last.IpBefore);
            var (code, comment) = line is null ? (last.OpCode, "") : SceneArt.Describe(line);
            LastLine.Text = $"P{last.Pid} @{last.IpBefore}  {code}";
            ToolTipService.SetToolTip(LastLine, comment.Length > 0 ? comment : null);
        }

        if (snap?.CurrentPid is uint cur && snap.Registers is { } cpu)
        {
            var line = LineAt(cur, cpu[11]);
            var (code, comment) = line is null ? ("(no source line at this address)", "") : SceneArt.Describe(line);
            NextLine.Text = $"P{cur} @{cpu[11]}  {code}";
            ToolTipService.SetToolTip(NextLine, comment.Length > 0 ? comment : null);
        }
        else
        {
            NextLine.Text = _state switch
            {
                MachineState.Completed => "nothing: every process has exited",
                MachineState.Paused when _pauseReason == "loaded" => "the scheduler picks when you press Run or Step",
                _ => "—",
            };
            ToolTipService.SetToolTip(NextLine, null);
        }

        // Which registers: the live CPU, or a selected process's saved copy in its PCB.
        uint[]? regs = null;
        bool sf = false, zf = false, showingCpu = true;
        var selected = _selectedPid is uint sp ? snap?.Processes.FirstOrDefault(p => p.Pid == sp) : null;
        if (selected is null) _selectedPid = null;
        if (selected is not null && selected.Pid != snap!.CurrentPid)
        {
            regs = selected.Registers;
            sf = selected.Sf;
            zf = selected.Zf;
            showingCpu = false;
            RegisterHeading.Text = $"P{selected.Pid}'s saved registers (in its PCB)";
        }
        else if (snap?.Registers is { } live)
        {
            regs = live;
            sf = snap.Sf;
            zf = snap.Zf;
            RegisterHeading.Text = $"CPU registers · P{snap.CurrentPid}";
        }
        else
        {
            RegisterHeading.Text = "CPU registers";
        }
        FollowCpuButton.Visibility = showingCpu ? Visibility.Collapsed : Visibility.Visible;

        bool highlight = showingCpu && last is not null && snap?.CurrentPid == last.Pid;
        var changed = highlight ? last!.ChangedRegisters.ToHashSet() : new HashSet<int>();
        bool flagsChanged = highlight && (last!.SfBefore != sf || last.ZfBefore != zf);

        foreach (var cell in _registerCells)
        {
            string value;
            bool isChanged;
            if (cell.Index < 0)
            {
                value = regs is null ? "—" : $"sf {(sf ? 1 : 0)} zf {(zf ? 1 : 0)}";
                isChanged = flagsChanged;
            }
            else
            {
                value = regs is null ? "—" : Fmt(regs[cell.Index]);
                isChanged = changed.Contains(cell.Index);
            }
            cell.Value.Text = value;
            cell.Root.Background = isChanged ? _wash : _clear;
            cell.Value.TextDecorations = isChanged ? TextDecorations.Underline : TextDecorations.None;
            AutomationProperties.SetName(cell.Root, cell.Label + " " + value + (isChanged ? ", just changed" : ""));
        }

        MarginNotes.Text = MarginNoteText(snap, last, showingCpu, selected);
        RenderProcesses(snap);
    }

    private string MarginNoteText(MachineSnapshot? snap, InstructionInfo? last, bool showingCpu, ProcessInfo? selected)
    {
        if (!showingCpu && selected is not null)
            return $"Saved when P{selected.Pid} left the CPU. The scheduler copies them back at its next turn.";
        if (last is null || snap is null) return "";
        if (snap.CurrentPid != last.Pid)
            return snap.CurrentPid is uint next
                ? $"P{last.Pid} ran last, then the scheduler switched to P{next}."
                : $"P{last.Pid} ran last.";

        var notes = last.ChangedRegisters.Where(r => r != 11)
            .Select(r => $"{RegisterName(r)}: {Fmt(last.RegistersBefore[r])} → {Fmt(last.RegistersAfter[r])}")
            .ToList();
        if (last.SfBefore != snap.Sf) notes.Add($"sf: {(last.SfBefore ? 1 : 0)} → {(snap.Sf ? 1 : 0)}");
        if (last.ZfBefore != snap.Zf) notes.Add($"zf: {(last.ZfBefore ? 1 : 0)} → {(snap.Zf ? 1 : 0)}");
        string text = notes.Count > 0 ? string.Join("    ", notes) : $"Only ip moved: @{last.IpBefore} → @{last.IpAfter}";
        if (_lastInstructionBatch > 1) text += $"   (last of {_lastInstructionBatch} instructions since the previous look)";
        return text;
    }

    private void RenderProcesses(MachineSnapshot? snap)
    {
        var processes = snap?.Processes ?? Array.Empty<ProcessInfo>();
        while (_processRows.Count < processes.Count) AddProcessRow();
        for (int i = 0; i < _processRows.Count; i++)
        {
            var row = _processRows[i];
            if (i >= processes.Count)
            {
                row.Button.Visibility = Visibility.Collapsed;
                continue;
            }
            var p = processes[i];
            bool onCpu = snap!.CurrentPid == p.Pid;
            row.Pid = p.Pid;
            row.Button.Visibility = Visibility.Visible;
            row.Marker.Text = onCpu ? "▶" : "";
            row.Name.Text = $"P{p.Pid} {FileFor(p.Pid)}";
            row.State.Text = FriendlyState(p) + $" · {p.ClockCycles} {(p.ClockCycles == 1 ? "cycle" : "cycles")}";
            row.Button.Background = _selectedPid == p.Pid ? _selectedRow : _clear;
            AutomationProperties.SetName(row.Button, $"Process {p.Pid}, {FileFor(p.Pid)}, {FriendlyState(p)}");
        }
    }

    private static string FriendlyState(ProcessInfo p) => p.State switch
    {
        "Running" => "on the CPU",
        "Ready" => "ready, waiting its turn",
        "NewProcess" => "new, not started",
        "WaitingAsleep" => $"asleep ({p.SleepCounter} left)",
        "WaitingOnLock" => $"waiting for lock {p.WaitingLock}",
        "WaitingOnEvent" => $"waiting for event {p.WaitingEvent}",
        "Terminated" => "exited",
        _ => p.State,
    };

    private int FrameCount => _snapshot is { PageSize: > 0 } s ? s.PhysicalMemory.Length / (int)s.PageSize : 0;

    private int FramePageCount => Math.Max(1, (FrameCount + FramesPerPage - 1) / FramesPerPage);

    private void RenderMemory()
    {
        var snap = _snapshot;
        int frames = FrameCount;
        if (snap is null || frames == 0)
        {
            RamSummary.Text = _state == MachineState.Off ? "RAM appears when a disk boots." : "Waiting for the first snapshot…";
            FramesPageText.Text = "";
            FramesPrev.IsEnabled = FramesNext.IsEnabled = false;
            foreach (var row in _frameRows) row.Button.Visibility = Visibility.Collapsed;
            FrameDetail.Visibility = Visibility.Collapsed;
            RenderSwaps(0);
            return;
        }

        int pageSize = (int)snap.PageSize;
        byte[] ram = snap.PhysicalMemory;
        var owners = new PageInfo?[frames];
        foreach (var page in snap.Pages)
        {
            if (!page.IsValid) continue;
            int f = (int)(page.AddrPhysical / snap.PageSize);
            if (f >= 0 && f < frames) owners[f] = page;
        }
        RamSummary.Text = $"{ram.Length} bytes of RAM · {frames} frames × {pageSize} · {owners.Count(o => o is not null)} in use";

        int pages = FramePageCount;
        _framePage = Math.Clamp(_framePage, 0, pages - 1);
        int first = _framePage * FramesPerPage;
        int lastFrame = Math.Min(frames, first + FramesPerPage) - 1;
        FramesPageText.Text = pages > 1 ? $"frames {first}–{lastFrame} of {frames}" : $"all {frames} frames";
        FramesPrev.IsEnabled = _framePage > 0;
        FramesNext.IsEnabled = _framePage < pages - 1;
        _selectedFrame = Math.Clamp(_selectedFrame, 0, frames - 1);

        for (int r = 0; r < _frameRows.Count; r++)
        {
            var row = _frameRows[r];
            int frame = first + r;
            if (frame >= frames)
            {
                row.Button.Visibility = Visibility.Collapsed;
                continue;
            }
            row.Frame = frame;
            row.Button.Visibility = Visibility.Visible;
            row.Button.Background = frame == _selectedFrame ? _selectedRow : _clear;
            row.Label.Text = "F" + frame;
            row.Owner.Text = OwnerShort(owners[frame], pageSize);
            for (int b = 0; b < 16; b++)
            {
                if (b >= pageSize)
                {
                    row.Cells[b].Visibility = Visibility.Collapsed;
                    continue;
                }
                int index = frame * pageSize + b;
                row.Cells[b].Visibility = Visibility.Visible;
                row.Values[b].Text = SceneArt.FormatByte(ram[index]);
                row.Cells[b].Background = _changedBytes.Contains(index) ? _wash : _clear;
            }
            AutomationProperties.SetName(row.Button, $"Frame {frame}, {OwnerLong(owners[frame], pageSize)}");
        }

        FrameDetail.Visibility = Visibility.Visible;
        int sel = _selectedFrame;
        int start = sel * pageSize;
        FrameDetailTitle.Text = $"Frame {sel} · physical bytes {start}–{start + pageSize - 1} · {OwnerLong(owners[sel], pageSize)}";
        for (int i = 0; i < _detailCells.Count; i++)
        {
            var cell = _detailCells[i];
            if (i >= pageSize)
            {
                cell.Box.Visibility = cell.Address.Visibility = Visibility.Collapsed;
                continue;
            }
            int index = start + i;
            cell.Box.Visibility = cell.Address.Visibility = Visibility.Visible;
            cell.Address.Text = index.ToString();
            cell.Value.Text = SceneArt.FormatByte(ram[index]);
            cell.Box.Background = _changedBytes.Contains(index) ? _wash : _clear;
        }

        RenderSwaps(frames * pageSize);
    }

    private static string OwnerShort(PageInfo? page, int pageSize)
    {
        if (page is null) return "free";
        string dirty = page.IsDirty ? " ✎" : "";
        if (page.SharedMemoryRegion > 0) return $"shared {page.SharedMemoryRegion}{dirty}";
        return $"P{page.PidOwner} · page {page.AddrProcessIndex / (uint)pageSize}{dirty}";
    }

    private static string OwnerLong(PageInfo? page, int pageSize)
    {
        if (page is null) return "free";
        string dirty = page.IsDirty ? " · dirty (changed since it arrived)" : "";
        if (page.SharedMemoryRegion > 0) return $"shared memory region {page.SharedMemoryRegion} (virtual page {page.PageNumber}){dirty}";
        return $"P{page.PidOwner}'s bytes {page.AddrProcessIndex}–{page.AddrProcessIndex + pageSize - 1} (virtual page {page.PageNumber}){dirty}";
    }

    // Process-relative page number, matching the frame list ("P6 · page 3"); PageNumber is the global virtual page.
    private static string PageOwner(PageInfo page, uint pageSize) =>
        page.SharedMemoryRegion > 0
            ? $"shared region {page.SharedMemoryRegion}"
            : $"P{page.PidOwner}'s page {page.AddrProcessIndex / pageSize}";

    private void RenderSwaps(int physicalBytes)
    {
        if (_swaps.Count == 0)
        {
            SwapSummary.Text = _snapshot is null
                ? ""
                : physicalBytes >= _virtualMemory
                    ? "Everything fits in RAM, so no page ever needs to leave."
                    : "No page has needed to leave RAM since loading.";
        }
        else
        {
            SwapSummary.Text = $"Since loading: {_swapOuts} evicted ({_swapOutFiles} written to disk as XML) · {_swapIns} brought back ({_swapInFiles} read from disk)";
        }

        if (_renderedSwapsVersion == _swapsVersion) return;
        _renderedSwapsVersion = _swapsVersion;
        SwapEvents.Children.Clear();
        uint pageSize = _snapshot?.PageSize is > 0 and var ps ? ps : 16;
        for (int i = _swaps.Count - 1; i >= 0 && i >= _swaps.Count - 6; i--)
        {
            var (isOut, p) = (_swaps[i].IsOut, _swaps[i].Payload);
            string owner = PageOwner(p.Page, pageSize);
            string head = isOut
                ? (p.UsedSwapFile ? "↓ wrote " + p.FileName : "↓ dropped (clean, no file)")
                : (p.UsedSwapFile ? "↑ read " + p.FileName : "↑ fresh page (no file)");
            string tail = $"clock {p.Clock} · {owner} · {(isOut ? "left" : "into")} F{p.Page.AddrPhysical / pageSize}";
            var stack = new StackPanel();
            var headText = Text(head, _mono, 14, isOut ? _accent : _ink);
            headText.TextTrimming = TextTrimming.CharacterEllipsis;
            var tailText = Text(tail, _hand, 15, _pencil);
            tailText.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(headText);
            stack.Children.Add(tailText);
            AutomationProperties.SetName(stack, head + ", " + tail);
            SwapEvents.Children.Add(stack);
        }
    }

    private void RenderSource()
    {
        var snap = _snapshot;
        if (_renderedPidButtonsVersion != _listingsVersion)
        {
            _renderedPidButtonsVersion = _listingsVersion;
            SourcePidButtons.Children.Clear();
            foreach (var listing in _listings)
            {
                uint pid = listing.Pid;
                var button = new Button
                {
                    Style = (Style)Res("PaperRowStyle"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 2, 8, 2),
                    Tag = pid,
                    Content = Text($"P{pid} {listing.FileName}", _mono, 15, _ink),
                };
                button.Click += (_, _) =>
                {
                    _sourcePid = pid;
                    _sourcePinned = _snapshot?.CurrentPid != pid;
                    _scrollSourceToNext = true;
                    _dirty = true;
                };
                SourcePidButtons.Children.Add(button);
            }
        }

        if (!_sourcePinned) _sourcePid = snap?.CurrentPid ?? _sourcePid ?? _listings.FirstOrDefault()?.Pid;
        foreach (var child in SourcePidButtons.Children.OfType<Button>())
            child.Background = child.Tag is uint tag && tag == _sourcePid ? _selectedRow : _clear;

        var current = _listings.FirstOrDefault(l => l.Pid == _sourcePid);
        if (current is null)
        {
            SourceHeading.Text = _state == MachineState.Off ? "Boot a disk to read its programs." : "";
            SourceList.Children.Clear();
            _sourceRows.Clear();
            _renderedSource = (null, _listingsVersion);
            return;
        }

        if (_renderedSource != (current.Pid, _listingsVersion))
        {
            _renderedSource = (current.Pid, _listingsVersion);
            BuildSourceRows(current);
        }

        var process = snap?.Processes.FirstOrDefault(p => p.Pid == current.Pid);
        uint? next = null;
        if (snap?.CurrentPid == current.Pid && snap.Registers is { } cpu) next = cpu[11];
        else if (process is not null && process.State != "Terminated") next = process.Registers[11];
        uint? lastAddress = _lastInstruction?.Pid == current.Pid ? _lastInstruction.IpBefore : null;

        string state = process is null ? (_state == MachineState.Completed ? "exited" : "not loaded") : FriendlyState(process);
        SourceHeading.Text = $"{current.FileName} · P{current.Pid} · {current.Lines.Count} lines · {state}    ▶ next   ✓ last";

        SourceRow? nextRow = null;
        foreach (var row in _sourceRows)
        {
            bool isNext = next == row.Address;
            bool isLast = lastAddress == row.Address;
            row.Marker.Text = isNext ? "▶" : isLast ? "✓" : "";
            row.Marker.Foreground = isNext ? _ink : _accent;
            row.Root.Background = isLast ? _wash : _clear;
            row.Root.BorderBrush = isNext ? _ink : _clear;
            if (isNext) nextRow = row;
        }

        if (nextRow is not null && (_scrollSourceToNext || nextRow != _scrolledTo))
        {
            _scrolledTo = nextRow;
            nextRow.Root.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.4, AnimationDesired = false });
        }
        _scrollSourceToNext = false;
    }

    private void BuildSourceRows(ProgramListing listing)
    {
        SourceList.Children.Clear();
        _sourceRows.Clear();
        _scrolledTo = null;
        foreach (var line in listing.Lines)
        {
            string raw = line.Text;
            string comment = "";
            int semi = raw.IndexOf(';');
            if (semi >= 0)
            {
                comment = raw[(semi + 1)..].Trim();
                raw = raw[..semi];
            }
            string code = string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            var grid = new Grid { ColumnSpacing = 6, MinHeight = 30 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = Text("", FontFamily.XamlAutoFontFamily, 15, _ink);
            var number = Text(line.LineNumber.ToString(), _mono, 13, _pencil);
            Grid.SetColumn(number, 1);
            var address = Text("@" + line.Address, _mono, 13, _pencil);
            Grid.SetColumn(address, 2);
            var codeText = Text(code, _mono, 16, _ink);
            codeText.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(codeText, 3);
            var commentText = Text(comment, _hand, 17, _pencil);
            commentText.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(commentText, 4);
            grid.Children.Add(marker);
            grid.Children.Add(number);
            grid.Children.Add(address);
            grid.Children.Add(codeText);
            grid.Children.Add(commentText);

            var root = new Border
            {
                Child = grid,
                Padding = new Thickness(4, 1, 4, 1),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1.5),
                BorderBrush = _clear,
                Background = _clear,
            };
            ToolTipService.SetToolTip(root, $"Line {line.LineNumber}: {line.OpCode}, {line.Length} bytes at @{line.Address}");
            AutomationProperties.SetName(root, $"Line {line.LineNumber}, address {line.Address}, {line.OpCode} {code}. {comment}");
            SourceList.Children.Add(root);
            _sourceRows.Add(new SourceRow { Root = root, Marker = marker, Address = line.Address });
        }
    }

    #endregion
}
