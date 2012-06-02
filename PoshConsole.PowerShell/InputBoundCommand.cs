using System.Collections;
using PoshConsole.PowerShell.Delegates;

namespace PoshConsole.PowerShell
{
	public struct InputBoundCommand
	{
		public bool AddToHistory;
		public PipelineOutputHandler Callback;
		public string[] Commands;
		public bool DefaultOutput;
		public IEnumerable Input;
		public bool RunAsScript;
		public bool UseLocalScope;

		//public Pipeline Pipeline;

		public InputBoundCommand( /*Pipeline pipeline,*/
		   string[] commands, IEnumerable input, PipelineOutputHandler callback)
		{
			//Pipeline = pipeline;
			Commands = commands;
			Input = input;
			Callback = callback;

			AddToHistory = true;
			DefaultOutput = true;
			RunAsScript = true;
			UseLocalScope = false;
		}

		public InputBoundCommand( /*Pipeline pipeline,*/
		   string[] commands, IEnumerable input, bool addToHistory, PipelineOutputHandler callback)
		{
			//Pipeline = pipeline;
			Commands = commands;
			Input = input;
			Callback = callback;
			AddToHistory = addToHistory;

			DefaultOutput = true;
			RunAsScript = true;
			UseLocalScope = false;
		}
	}
}
