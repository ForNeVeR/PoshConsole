using System.Management.Automation.Runspaces;

namespace PoshConsole.PowerShell.Delegates
{
	public delegate void RunspaceReadyHandler(object source, RunspaceState stateEventArgs);
}
