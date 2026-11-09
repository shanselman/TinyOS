using System;
using System.Linq;
using TinyOS.Protocol;
using Windows.Foundation;
using Microsoft.UI.Xaml.Media;

namespace TinyOS_TimeMachine;

/// <summary>Static, deterministic decoration geometry and small text helpers.</summary>
internal static class SceneArt
{
    /// <summary>Horizontal scanlines. Built once; never animated.</summary>
    public static Geometry Scanlines(double width, double height, double pitch = 4, double thickness = 1.6)
    {
        var group = new GeometryGroup();
        for (double y = 0; y < height; y += pitch)
            group.Children.Add(new RectangleGeometry { Rect = new Rect(0, y, width, thickness) });
        return group;
    }

    /// <summary>Notebook ruling: evenly spaced horizontal rules below a header band.</summary>
    public static Geometry Ruling(double width, double height, double top, double pitch)
    {
        var group = new GeometryGroup();
        for (double y = top; y < height - 8; y += pitch)
            group.Children.Add(new LineGeometry { StartPoint = new Point(0, y), EndPoint = new Point(width, y) });
        return group;
    }

    /// <summary>
    /// Splits a TinyOS program line ("6   r1, $3276834 ;move a big number into r1") into
    /// the operands as written and the author's comment. The numeric opcode is replaced by
    /// the engine's own decoded opcode name.
    /// </summary>
    public static (string Code, string Comment) Describe(SourceLine line)
    {
        string text = line.Text;
        string comment = "";
        int semi = text.IndexOf(';');
        if (semi >= 0)
        {
            comment = text[(semi + 1)..].Trim();
            text = text[..semi];
        }
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1));
        return ((line.OpCode + " " + text).Trim(), comment);
    }

    public static string FormatRegister(uint value, bool hex) => hex ? "0x" + value.ToString("X") : value.ToString();

    public static string FormatByte(byte value) => value == 0 ? "-" : value.ToString();
}
