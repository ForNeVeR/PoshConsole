using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.FSharp.Collections;

namespace Huddled.Wpf.Controls
{
	public delegate IEnumerable<string> TabExpansionLister(string commandLine);

	public class TabExpansion
	{
		private IEnumerable<string> _choices;
		private string _command;
		private int _index;

		public TabExpansion()
		{
			_choices = new List<string>();
			TabComplete += cmd => new List<string>();
		}

		public event TabExpansionLister TabComplete;

		public IEnumerable<string> GetChoices(string currentCommand)
		{
			ConsoleControl.TabExpansionTrace.TraceEvent(TraceEventType.Information, 1, "GetChoices for '{0}'", currentCommand);
			if (_command != currentCommand ||
				(_choices == null || !_choices.Any()) && (_command == null || _command != currentCommand))
			{
				_command = currentCommand;
				_choices = SeqModule.Cache(TabComplete(currentCommand));
			}

			if (ConsoleControl.TabExpansionTrace.Switch.Level >= SourceLevels.Information)
			{
				ConsoleControl.TabExpansionTrace.TraceEvent(TraceEventType.Information, 2, "Choice List:");
			}

			return _choices;
		}

		public string Next(string currentCommand)
		{
			return Move(currentCommand, true);
		}

		public string Previous(string currentCommand)
		{
			return Move(currentCommand, false);
		}

		public void Reset()
		{
			_index = 0;
			_command = null;
			_choices = null;
		}

		private string Move(string currentCommand, bool forward)
		{
			if (!_choices.Any())
			{
				GetChoices(currentCommand);
			}

			_index += forward ? 1 : -1;

			if (_index < 0)
			{
				_index = 0;
			}

			var element = _choices.Skip(_index).FirstOrDefault();
			if (element == null)
			{
				_index = 0;
				element = _choices.FirstOrDefault() ?? _command;
			}

			return element;
		}
	}
}
