using System;
using System.Collections.Generic;
using System.Text;

namespace TinyOS_TimeMachine;

/// <summary>
/// The CRT's text model: exactly the characters TinyOS wrote, in order.
/// Only newline is interpreted (the worker already strips carriage returns), which is
/// all the 2002 engine ever prints. The screen is a fixed character grid; long lines
/// wrap at the column limit like a real terminal.
/// </summary>
internal sealed class TerminalBuffer
{
    private const int MaxLines = 4000;
    private readonly List<string> _lines = new();
    private readonly StringBuilder _partial = new();
    private readonly StringBuilder _transcript = new();

    public int Version { get; private set; }

    public string Transcript => _transcript.ToString();

    public bool IsEmpty => _lines.Count == 0 && _partial.Length == 0;

    public void Clear()
    {
        _lines.Clear();
        _partial.Clear();
        _transcript.Clear();
        Version++;
    }

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _transcript.Append(text);
        foreach (char c in text)
        {
            if (c == '\r') continue;
            if (c == '\n')
            {
                _lines.Add(_partial.ToString());
                _partial.Clear();
            }
            else
            {
                _partial.Append(c);
            }
        }
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        Version++;
    }

    /// <summary>The last <paramref name="rows"/> screen rows, wrapped at <paramref name="columns"/>.</summary>
    public string Screen(int columns, int rows)
    {
        columns = Math.Max(10, columns);
        var screen = new List<string>(rows);
        if (_partial.Length > 0) AddWrappedReversed(_partial.ToString(), columns, rows, screen);
        for (int i = _lines.Count - 1; i >= 0 && screen.Count < rows; i--)
            AddWrappedReversed(_lines[i], columns, rows, screen);
        screen.Reverse();
        return string.Join("\n", screen);
    }

    private static void AddWrappedReversed(string line, int columns, int rows, List<string> screen)
    {
        if (line.Length <= columns)
        {
            screen.Add(line);
            return;
        }
        var parts = new List<string>();
        for (int i = 0; i < line.Length; i += columns)
            parts.Add(line.Substring(i, Math.Min(columns, line.Length - i)));
        for (int i = parts.Count - 1; i >= 0 && screen.Count < rows; i--)
            screen.Add(parts[i]);
    }
}
