using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Management.Automation.Host;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using System.Management.Automation;
using System.Threading;
using System.Collections.ObjectModel;
using System.Text;

namespace Huddled.PoshConsole
{
    /// <summary>
    /// Here we EXPLICITLY implement the IPSConsole interface.
    /// Importantly, this implementation just calls the existing methods on the our RichTextConsole class
    /// Each call is wrapped in Dispatcher methods so that the interface is thread-safe!
    /// </summary>
    public partial class RichTextConsole : IPSConsole  //, IPSConsole, IConsoleControlBuffered
    {
        // ToDo: we really should consider using some alternative output method for these other output types...
        // Possibly an alternative panel that pops up and can be closed?
        #region IPSConsole Members

        Dictionary<string, PSObject> IPSConsole.Prompt(string caption, string message, Collection<FieldDescription> descriptions)
        {
            ((IPSConsole)this).WriteLine(ConsoleColor.Blue, ConsoleColor.Black, caption + "\n" + message + " ");

            Dictionary<string, PSObject> results = new Dictionary<string, PSObject>();
            foreach (FieldDescription fd in descriptions)
            {
                string[] label = GetHotkeyAndLabel(fd.Label);

                if (!String.IsNullOrEmpty(fd.HelpMessage)) ((IPSConsole)this).Write(fd.HelpMessage);

                ((IPSConsole)this).WriteLine(ConsoleColor.Blue, ConsoleColor.Black, String.Format("\n{0}: ", label[1]));

                string userData = ((IPSConsole)this).ReadLine();
                if (userData == null) return null;
                results[fd.Name] = PSObject.AsPSObject(userData);
            }
            return results;
        }

        int IPSConsole.PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
        {
            // Write the caption and message strings in Blue.
            ((IPSConsole)this).WriteLine(ConsoleColor.Blue, ConsoleColor.Black, caption + "\n" + message + "\n");

            // Convert the choice collection into something that's a little easier to work with
            // See the BuildHotkeysAndPlainLabels method for details.
            Dictionary<string, PSObject> results = new Dictionary<string, PSObject>();
            string[,] promptData = BuildHotkeysAndPlainLabels(choices);

            // Format the overall choice prompt string to display...
            StringBuilder sb = new StringBuilder();
            for (int element = 0; element < choices.Count; element++)
            {
                sb.Append(String.Format("|{0}> {1} ", promptData[0, element], promptData[1, element]));
            }
            sb.Append(String.Format("[Default is ({0}]", promptData[0, defaultChoice]));

            // Loop reading prompts until a match is made, the default is
            // chosen or the loop is interrupted with ctrl-C.
            while (true)
            {
                ((IPSConsole)this).WriteLine(ConsoleColor.Cyan, ConsoleColor.Black, sb.ToString());
                string data = ((IPSConsole)this).ReadLine().Trim().ToUpper();

                // If the choice string was empty, use the default selection.
                if (data.Length == 0)
                    return defaultChoice;

                // See if the selection matched and return the
                // corresponding index if it did...
                for (int i = 0; i < choices.Count; i++)
                {
                    if (promptData[0, i] == data)
                        return i;
                }
                ((IPSConsole)this).WriteErrorLine("Invalid choice: " + data);
            }

        }

        /// <summary>
        /// Parse a string containing a hotkey character.
        /// 
        /// Take a string of the form: 
        /// "Yes to &amp;all"
        /// And return a two-dimensional array split out as
        ///    "A", "Yes to all".
        /// </summary>
        /// <param name="input">The string to process</param>
        /// <returns>
        /// A two dimensional array containing the parsed components.
        /// </returns>
        private static string[] GetHotkeyAndLabel(string input)
        {
            string[] result = new string[] { String.Empty, String.Empty };
            string[] fragments = input.Split('&');
            if (fragments.Length == 2)
            {
                if (fragments[1].Length > 0)
                    result[0] = fragments[1][0].ToString().ToUpper();
                result[1] = (fragments[0] + fragments[1]).Trim();
            }
            else
            {
                result[1] = input;
            }
            return result;
        }

        /// <summary>
        /// This is a private worker function that splits out the
        /// accelerator keys from the menu and builds a two dimentional 
        /// array with the first access containing the
        /// accelerator and the second containing the label string
        /// with &amp; removed.
        /// </summary>
        /// <param name="choices">The choice collection to process</param>
        /// <returns>
        /// A two dimensional array containing the accelerator characters
        /// and the cleaned-up labels</returns>
        private static string[,] BuildHotkeysAndPlainLabels(Collection<ChoiceDescription> choices)
        {
            // Allocate the result array
            string[,] hotkeysAndPlainLabels = new string[2, choices.Count];

            for (int i = 0; i < choices.Count; ++i)
            {
                string[] hotkeyAndLabel = GetHotkeyAndLabel(choices[i].Label);
                hotkeysAndPlainLabels[0, i] = hotkeyAndLabel[0];
                hotkeysAndPlainLabels[1, i] = hotkeyAndLabel[1];
            }
            return hotkeysAndPlainLabels;
        }

        IPSRawConsole IPSConsole.RawUI
        {
            get { return this; }
        }

        #region ReadLine 

        private AutoResetEvent _gotInput = new AutoResetEvent(false);
        private string _lastInputString = null;
        public bool _waitingForInput = false;


        /// <summary>
        /// Handles the CommandEntered event of the Console buffer
        /// </summary>
        /// <param name="command">The command.</param>
        private void OnCommand(string command)
        {
            if (_waitingForInput)
            {
                //if (command.EndsWith("\n"))
                //{
                _lastInputString = command;
                //}
                //else
                //{
                //    lastInputString = command + "\n";
                //}
                _gotInput.Set();
            }
            else
            {
                ProcessCommand(command);
            }
        }

	
        /// <summary>
        /// Provides a way for scripts to request user input ...
        /// </summary>
        /// <returns></returns>
        string IPSConsole.ReadLine()
        {
            string result = null;

            _waitingForInput = true;
            _gotInput.WaitOne();
            _waitingForInput = false;

            result = _lastInputString;
            return result;
        }
        #endregion ReadLine


        void IPSConsole.Write(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(null, null, message);
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), null, null, message);
            }
        }

        void IPSConsole.Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(_consoleBrushes.BrushFromConsoleColor(foregroundColor), _consoleBrushes.BrushFromConsoleColor(backgroundColor), message);
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), 
                    _consoleBrushes.BrushFromConsoleColor(foregroundColor), 
                    _consoleBrushes.BrushFromConsoleColor(backgroundColor), message);
            }
        }

        void IPSConsole.WriteLine(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(null, null, message + "\n");
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), null, null, message + "\n");
            }
        }

        void IPSConsole.WriteLine(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(_consoleBrushes.BrushFromConsoleColor(foregroundColor), _consoleBrushes.BrushFromConsoleColor(backgroundColor), message + "\n");
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), _consoleBrushes.BrushFromConsoleColor(foregroundColor), _consoleBrushes.BrushFromConsoleColor(backgroundColor), message + "\n");
            }
        }

        void IPSConsole.WriteDebugLine(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(_consoleBrushes.DebugForeground, _consoleBrushes.DebugBackground, String.Format("DEBUG: {0}\n", message));
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), _consoleBrushes.DebugForeground, _consoleBrushes.DebugBackground, String.Format("DEBUG: {0}\n", message));
            }
        }

        void IPSConsole.WriteErrorLine(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(_consoleBrushes.ErrorForeground, _consoleBrushes.ErrorBackground, message + "\n");
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), _consoleBrushes.ErrorForeground, _consoleBrushes.ErrorBackground, message + "\n");
            }
        }

        void IPSConsole.WriteVerboseLine(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(_consoleBrushes.VerboseForeground, _consoleBrushes.VerboseBackground, String.Format("VERBOSE: {0}\n", message));
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), _consoleBrushes.VerboseForeground, _consoleBrushes.VerboseBackground, String.Format("VERBOSE: {0}\n", message));
            }
        }

        void IPSConsole.WriteWarningLine(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(_consoleBrushes.WarningForeground, _consoleBrushes.WarningBackground, String.Format("WARNING: {0}\n", message));
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), _consoleBrushes.WarningForeground, _consoleBrushes.WarningBackground, String.Format("WARNING: {0}\n", message));
            }
        }

        void IPSConsole.WriteNativeLine(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(_consoleBrushes.NativeOutputForeground, _consoleBrushes.NativeOutputBackground, message + "\n");
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), _consoleBrushes.NativeOutputForeground, _consoleBrushes.NativeOutputBackground, message + "\n");
            }
        }

        void IPSConsole.WriteNativeErrorLine(string message)
        {
            if (Dispatcher.CheckAccess())
            {
                this.Write(_consoleBrushes.NativeErrorForeground, _consoleBrushes.NativeErrorBackground, message + "\n");
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, 
                    new WriteOutputDelegate(this.Write), _consoleBrushes.NativeErrorForeground, _consoleBrushes.NativeErrorBackground, message + "\n");
            }
        }

        #endregion
    }
}