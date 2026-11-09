using System;
using System.Text;

namespace TinyOS.Worker
{
	/// <summary>
	/// Replaces Console.Out inside the worker. The original engine keeps calling
	/// Console.WriteLine; complete lines are buffered here and sent to the app as
	/// protocol "output" events, so the engine's text never corrupts the JSON stream.
	/// </summary>
	internal sealed class OutputCapture : System.IO.TextWriter
	{
		private readonly object _lock = new object();
		private readonly StringBuilder _pending = new StringBuilder();
		private readonly StringBuilder _currentLine = new StringBuilder();

		public override Encoding Encoding => Encoding.UTF8;

		public override void Write(char value)
		{
			lock (_lock)
			{
				if (value == '\r') return;
				if (value == '\n')
				{
					_pending.Append(_currentLine).Append('\n');
					_currentLine.Clear();
				}
				else
				{
					_currentLine.Append(value);
				}
			}
		}

		public override void Write(string value)
		{
			if (value == null) return;
			foreach (char c in value) Write(c);
		}

		public override void WriteLine(string value)
		{
			Write(value);
			Write('\n');
		}

		public override void WriteLine() => Write('\n');

		/// <summary>Returns and clears complete lines (and, if <paramref name="includePartial"/>, any partial line).</summary>
		public string Drain(bool includePartial)
		{
			lock (_lock)
			{
				if (includePartial && _currentLine.Length > 0)
				{
					_pending.Append(_currentLine);
					_currentLine.Clear();
				}
				if (_pending.Length == 0) return null;
				string text = _pending.ToString();
				_pending.Clear();
				return text;
			}
		}
	}
}
