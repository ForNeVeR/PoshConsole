using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interactivity;
using System.Windows.Threading;
using Huddled.Interop;
using Huddled.Wpf;
using Huddled.Wpf.Controls;
using Huddled.Wpf.Controls.Interfaces;
using Huddled.Wpf.Controls.Utility;

namespace PoshConsole.Host
{
   /// <summary>
   /// A sample implementation of the PSHost abstract class for console
   /// applications. Not all members are implemented. Those that are 
   /// not implemented throw a NotImplementedException exception.
   /// </summary>
   public partial class PoshHost : PSHost, IPSBackgroundHost
   {

      #region  Fields (16)
      /// <summary>
      /// A ConsoleRichTextBox for output
      /// </summary>
      private readonly IPoshConsoleControl _buffer;
      /// <summary>
      /// A Console Window wrapper that hides the console
      /// </summary>
      private NativeConsole _console;
      private static readonly Guid _INSTANCE_ID = Guid.NewGuid();
      public bool IsClosing;
      private readonly PoshRawUI _rawUI;
      /// <summary>
      /// The PSHostUserInterface implementation
      /// </summary>
// ReSharper disable InconsistentNaming
      // UI is an abbreviation as in _rawUI
      private readonly PoshUI _UI;
// ReSharper restore InconsistentNaming
      /// <summary>
      /// This API is called before an external application process is started.
      /// </summary>
      int _native;
      //private IInput inputHandler = null; 
      internal PoshOptions Options;
      private readonly CultureInfo _originalCultureInfo = Thread.CurrentThread.CurrentCulture;
      private readonly CultureInfo _originalUICultureInfo = Thread.CurrentThread.CurrentUICulture;
      private readonly IPSUI _psUi;
      private string _savedTitle = String.Empty;
      private readonly Command _outDefault;
      private readonly CommandRunner _runner;

      #endregion

      #region  Constructors (1)

      //internal List<string> StringHistory;
      [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "PoshConsole")]
      public PoshHost(IPSUI psUi)
      {
         _buffer = psUi.Console;

         MakeConsole();

         //StringHistory = new List<string>();
         Options = new PoshOptions(this, _buffer);
         _psUi = psUi;

         try
         {
            // we have to be careful here, because this is an interface member ...
            // but in the current implementation, _buffer.RawUI returns _buffer
            _rawUI = new PoshRawUI(_buffer.RawUI);
            _UI = new PoshUI(_rawUI, psUi);

            // pre-create this
            _outDefault = new Command("Out-Default");
            // for now, merge the errors with the rest of the output
            _outDefault.MergeMyResults(PipelineResultTypes.Error, PipelineResultTypes.Output);
            _outDefault.MergeUnclaimedPreviousCommandResults = PipelineResultTypes.Error | PipelineResultTypes.Output;
         }
         catch (Exception ex)
         {
            MessageBox.Show(
               "Can't create PowerShell interface, are you sure PowerShell is installed? \n" + ex.Message + "\nAt:\n" +
               ex.Source, "Error Starting PoshConsole", MessageBoxButton.OK, MessageBoxImage.Stop);
            throw;
         }
         _buffer.CommandBox.IsEnabled = false;
         _buffer.Expander.TabComplete += BufferTabComplete;
         _buffer.Command += OnGotUserInput;
         //_buffer.CommandEntered +=new ConsoleRichTextBox.CommandHandler(buffer_CommandEntered);

         //_buffer.GetHistory +=new ConsoleRichTextBox.HistoryHandler(buffer_GetHistory);

         // this.ShouldExit += new ExitHandler(WeShouldExit);
         //myUI.ProgressUpdate += new PoshUI.WriteProgressDelegate( delegate(long sourceId, ProgressRecord record){if(ProgressUpdate!=null) ProgressUpdate(sourceId, record);} );
         //myUI.Input += new PoshUI.InputDelegate(GetInput);
         //myUI.Output += new PoshUI.OutputEventHandler(OnOutput);
         //myUI.OutputLine += new PoshUI.OutputEventHandler(OnOutputLine);
         //myUI.WritePrompt += new PoshUI.PromptDelegate(WritePrompt);

         // Some delegates we think we can get away with making only once...
         Properties.Settings.Default.PropertyChanged += SettingsPropertyChanged;
         // Properties.Settings.Default.SettingChanging += new System.Configuration.SettingChangingEventHandler(SettingsSettingChanging);
         // Properties.Colors.Default.PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler(ColorsPropertyChanged);
         _runner = new CommandRunner(this);
         _runner.RunspaceReady += (source, args) => _buffer.Dispatcher.BeginInvoke((Action) (() => {
            _buffer.CommandBox.IsEnabled = true;
            ExecutePromptFunction(null, PipelineState.Completed);
         }));


         _runner.ShouldExit += (source, args) => SetShouldExit(args);

         //// Finally, STARTUP!
         //ExecuteStartupProfile();
      }


      #endregion

      #region  Properties (7)

      /// <summary>
      /// Return the culture info to use - this implementation just snapshots the
      /// culture info of the thread that created this object.
      /// </summary>
      public override CultureInfo CurrentCulture
      {
         get { return _originalCultureInfo; }
      }

      /// <summary>
      /// Return the UI culture info to use - this implementation just snapshots the
      /// UI culture info of the thread that created this object.
      /// </summary>
      public override CultureInfo CurrentUICulture
      {
         get { return _originalUICultureInfo; }
      }

      /// <summary>
      /// This implementation always returns the GUID allocated at instantiation time.
      /// </summary>
      public override Guid InstanceId
      {
         get { return _INSTANCE_ID; }
      }

      /// <summary>
      /// Return an appropriate string to identify your host implementation.
      /// Keep in mind that this string may be used by script writers to identify
      /// when your host is being used.
      /// </summary>
      public override string Name
      {
         get { return "PoshConsole"; }
      }

      public override PSObject PrivateData
      {
         get
         {
            return PSObject.AsPSObject(Options);
         }
      }

      /// <summary>
      /// Return an instance of the implementation of the PSHostUserInterface
      /// class for this application. This instance is allocated once at startup time
      /// and returned every time thereafter.
      /// </summary>
      public override PSHostUserInterface UI
      {
         get { return _UI; }
      }

      //private PoshUI myHostUserInterface = new PoshUI();
      /// <summary>
      /// Return the version object for this application. Typically this should match the version
      /// resource in the application.
      /// </summary>
      public override Version Version
      {
         get
         {
             return Assembly.GetEntryAssembly().GetName().Version;
         }
      }

      #endregion

      #region  Methods (14)

      //  Public Methods (6)

      /// <summary>
      /// Not implemented by this example class. The call fails with an exception.
      /// </summary>
      [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "EnterNestedPrompt"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "MyHost")]
      public override void EnterNestedPrompt()
      {
         // TODO: IMPLEMENT PSHost.EnterNestedPrompt()
         throw new NotImplementedException("Cannot suspend the shell, EnterNestedPrompt() method is not implemented by MyHost.");
      }

      /// <summary>
      /// Not implemented by this example class. The call fails with an exception.
      /// </summary>
      [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "ExitNestedPrompt"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "MyHost")]
      public override void ExitNestedPrompt()
      {
         // TODO: IMPLEMENT PSHost.ExitNestedPrompt()
         throw new NotImplementedException("The ExitNestedPrompt() method is not implemented by MyHost.");
      }


      private void MakeConsole()
      {
         if (_console == null)
         {
            try
            {
               // don't initialize in the constructor
               _console = new NativeConsole(false);
               // this way, we can handle (report) any exception...
               _console.Initialize();
               _console.WriteOutput += (source, args) => _buffer.WriteNativeOutput(args.Text.TrimEnd('\n'));
               _console.WriteError += (source, args) => _buffer.WriteNativeError(args.Text.TrimEnd('\n'));
            }
            catch (ConsoleInteropException cie)
            {
               _buffer.WriteErrorRecord(new ErrorRecord(cie, "Couldn't initialize the Native Console", ErrorCategory.ResourceUnavailable, null));
            }
         }
      }

      public void KillConsole()
      {
         if (_console != null)
         {
            _console.Dispose();
            _runner.Dispose();
         }
         _console = null;
      }

      public override void NotifyBeginApplication()
      {
         _savedTitle = _UI.RawUI.WindowTitle;

         _native++;
         //MakeConsole();
      }

      /// <summary>
      /// This API is called after an external application process finishes.
      /// </summary>
      public override void NotifyEndApplication()
      {
         _UI.RawUI.WindowTitle = _savedTitle;

         _native--;
         //if (_native == 0) KillConsole();
      }

      /// <summary>
      /// Method used to handle control-C's from the user. It calls the
      /// pipeline Stop() method to stop execution. If any exceptions occur,
      /// they are printed to the console; otherwise they are ignored.
      /// </summary>
      public void StopPipeline()
      {
         if (_native > 0)
         {
            NativeConsole.SendCtrlC();
            //_console.SendCtrlC();
         }
         else
         {
            _runner.StopPipeline();
         }

         if (_buffer.IsInputFocused)
         {
            _buffer.ClearInput();
         }
         else
         {
            _buffer.FocusInput();
         }
      }
      //public void StopPipeline()
      //{
      //    lock (instanceLock)
      //    {
      //        if (currentPipeline != null && currentPipeline.PipelineStateInfo.State == PipelineState.Running)
      //        {
      //            if (_native > 0)
      //            {
      //                _console.WriteInput("\n" + (char)26);
      //            }
      //            else
      //            {
      //                currentPipeline.StopAsync();
      //            }
      //        }
      //        //else
      //        //    ApplicationCommands.Copy.Execute(null, _buffer);
      //    }
      //}

      //  Private Methods (6)

      /// <summary>
      /// Basic script execution routine - any runtime exceptions are
      /// caught and passed back into the runtime to display.
      /// </summary>
      /// <param name="cmd">The command to execute</param>
      public void Execute(string cmd)
      {
         if (!string.IsNullOrEmpty(cmd))
         {
            ExecutePipelineOutDefault(cmd, true, result =>
                                                    {
                                                       if (result.Failure != null)
                                                       {
                                                          // ToDo: if( result.Failure is IncompleteParseException ) { // trigger multiline entry
                                                          WriteErrorRecord(((RuntimeException)(result.Failure)).ErrorRecord);
                                                       }
                                                       if (!IsClosing)
                                                       {
                                                          ExecutePromptFunction(cmd, result.State);
                                                       }
                                                    });
         }
         else if (!IsClosing)
         {
            ExecutePromptFunction(cmd, PipelineState.NotStarted);
         }
      }

      //void Execute(string cmd)
      //{
      //    try
      //    {
      //        // execute the command with no Input...
      //        ExecuteHelper(cmd, null, true);
      //    }
      //    catch (RuntimeException rte)
      //    {
      //        // An exception occurred that we want to display ...
      //        // We have to run another pipeline, and pass in the error record.
      //        // The runtime will bind the Input to the $Input variable
      //        ExecuteHelper("write-host ($Input | out-string) -fore darkyellow", rte.ErrorRecord, false);
      //    }
      //}
      ///// <summary>
      ///// Invoke's the user's PROMPT function to display a prompt.
      ///// Called after each command completes
      ///// </summary>
      //internal void Prompt()
      //{
      //    StringBuilder sb = new StringBuilder();
      //    try
      //    {
      //        Collection<PSObject> output = InvokePipeline("prompt");

      //        foreach (PSObject thing in output)
      //        {
      //            sb.Append(thing.ToString());
      //        }
      //        //sb.Append(PromptPadding);
      //    }
      //    catch (RuntimeException rte)
      //    {
      //        // An exception occurred that we want to display ...
      //        // We have to run another pipeline, and pass in the error record.
      //        // The runtime will bind the Input to the $Input variable
      //        ExecuteHelper("write-host \"ERROR: Your prompt function crashed!\n\" -fore darkyellow", null, false);
      //        ExecuteHelper("write-host ($Input | out-string) -fore darkyellow", rte.ErrorRecord, false);
      //        sb.Append("\n> ");
      //    }
      //    finally
      //    {
      //        _buffer.Prompt(sb.ToString());
      //    }
      //}
      private void ExecutePromptFunction(String command, PipelineState lastState)
      {
         _buffer.OnCommandFinished(command, lastState);
         // It is IMPERATIVE that we call "New-Paragraph" before Prompt
         _runner.Enqueue(new InputBoundCommand(new[] {"New-Paragraph", "Prompt"}, EmptyArray, result =>
             {
                StringBuilder str = new StringBuilder();

                foreach(PSObject obj in result.Output)
                {
                   str.Append(obj);
                }
                // ToDo: write errors from PROMPT the same as we would for a regular command...
                //if(result.State == PipelineState.Failed ) {
                //   str.Append(result.Failure.Message);
                //   str.Append(result.Failure.Message);

                _buffer.Prompt(str.ToString());
             }){AddToHistory = false, RunAsScript = false, DefaultOutput = false});
      }


      //void runSpace_StateChanged(object sender, RunspaceStateEventArgs e)
      //{
      //   if (e.RunspaceStateInfo.State == RunspaceState.Opened)
      //   {
      //      _buffer.Dispatcher.BeginInvoke(DispatcherPriority.Render,
      //                                           (Action)(() => _psUi.Console.CommandBox.IsEnabled = true));
      //      ExecuteStartupProfile();
      //   }
      //   else
      //   {
      //      _buffer.Dispatcher.BeginInvoke(DispatcherPriority.Render,
      //                                           (Action)(() => _psUi.Console.CommandBox.IsEnabled = false));
      //   }
      //}

      /// <summary>
      /// Handler for the IInput.GotUserInput event.
      /// </summary>
      /// <param name="source">Source control</param>
      /// <param name="command">The command line.</param>
      void OnGotUserInput(Object source, CommandEventArgs command)
      {
         string commandLine = command.Command;
         if (_native == 0)
         {
            // output the text back to the console control (is this weird, or is it just me?)
            _buffer.WriteLine( command.Command );
            Execute(commandLine);
         }
         else
         {
            if (commandLine.Length > 0 && commandLine[commandLine.Length - 1].Equals('\n'))
            {
               _console.WriteInput(commandLine);
            }
            else _console.WriteInput(commandLine + '\n');
         }
      }

      private void WriteErrorRecord(ErrorRecord record)
      {
         _buffer.WriteErrorRecord(record);
      }

      //  Internal Methods (2)


      #endregion

      #region ConsoleRichTextBox Event Handlers
      /// <summary>
      /// Indicate to the host application that exit has
      /// been requested. Pass the exit code that the host
      /// application should use when exiting the process.
      /// </summary>
      /// <param name="exitCode"></param>
      public override void SetShouldExit(int exitCode)
      {
         if (!IsClosing)
         {
            IsClosing = true;
            //((IPSConsole)buffer).WriteVerboseLine("Running Exit Scripts...");
            _runner.ExecuteShutdownProfile(exitCode);
            //((IPSConsole)buffer).WriteVerboseLine("Shutting Down.");
         } else {
          //Application.Current.Shutdown(exitCode);
          _psUi.SetShouldExit(exitCode);
         }

      }

      private List<string> BufferTabComplete(string cmdline)
      {
         var completions = new List<string>();
         Collection<PSObject> set;
         var lastWord = cmdline.GetLastWord();
         ConsoleControl.TabExpansionTrace.TraceEvent(TraceEventType.Information, 1, "Line: '{0}'\nWord: '{1}'", cmdline, lastWord);
         // Finally, call the TabExpansion string);
         try
         {
            set = InvokePipeline("TabExpansion -Line '" + cmdline + "' -LastWord '" + lastWord + "'");
            if (set != null)
            {
               completions.AddRange(set.Select(opt => opt.ToString()));
            }
         } // hide the error
         catch (RuntimeException) { }

         if(completions.Count > 0)
         {
            return completions;
         }

         // Still need to do more Tab Completion
         // WishList: Make PowerTab only necessariy for true POWER users.
         // Ideally, you should be able to choose which TabExpansions you want
         // but get them all at _compiled_ speeds ... 
         //   TabComplete Parameters
         //   TabComplete Variables
         //   TabComplete Aliases
         //   TabComplete Executables in (current?) path

         if (!string.IsNullOrEmpty(lastWord))
         {
            // TODO: find a way to access the ExecutionContext.SessionState to get the Commands list.
            //// TabComplete Cmdlets inside the pipeline
            //try
            //{
               
            //   foreach (CmdletConfigurationEntry cmdlet in _runner.RunspaceConfiguration.Cmdlets)
            //   {
            //      if (cmdlet.Name.StartsWith(lastWord, true, null))
            //      {
            //         completions.Add(cmdlet.Name);
            //      }
            //   }
            //}  // hide the error (no error unless RunspaceConfiguration is missing)
            //catch (RuntimeException) { }
            //catch (NullReferenceException) { }

            // TabComplete Paths
            try
            {
               if (lastWord[0] == '$')
               {
                  set = InvokePipeline("get-variable " + lastWord.Substring(1) + "*");
                  if (set != null)
                  {
                     completions.AddRange(set.Select(opt => opt.ImmediateBaseObject).OfType<PSVariable>().Select(var => "$" + var.Name));
                  }
               }
            }// hide the error
            catch (RuntimeException) { }
            //finally
            //{
            //   set = null;
            //}


            try
            {
               set = InvokePipeline("resolve-path \"" + lastWord + "*\"");
               if (set != null)
               {
                  completions.AddRange(
                     set.Select(opt => opt.ToString()).Select(
                        completion => ((String) completion).Contains(" ") ? string.Format("\"{0}\"", completion) : completion
                     )
                  );
               }
            }// hide the error
            catch (RuntimeException) { }
            //finally
            //{
            //   set = null;
            //}


            //finally {
            //    set = null;
            //}
         }
         return completions;
      }
      #endregion
      #region
      ///// <summary>
      ///// A helper method which builds and executes a pipeline that returns it's output.
      ///// </summary>
      ///// <param name="cmd">The script to run</param>
      ///// <param name="Input">Any Input arguments to pass to the script.</param>
      //public Collection<PSObject> InvokeHelper(string cmd, object Input)
      //{
      //    Collection<PSObject> output = new Collection<PSObject>();
      //    if (_ready.WaitOne(10000, true))
      //    {
      //        // Ignore empty command lines.
      //        if (String.IsNullOrEmpty(cmd))
      //            return null;

      //        // Create the pipeline object and make it available
      //        // to the ctrl-C handle through the currentPipeline instance
      //        // variable.
      //        lock (instanceLock)
      //        {
      //            _ready.Reset();
      //            currentPipeline = myRunSpace.CreatePipeline(cmd, false);
      //        }

      //        // Create a pipeline for this execution. Place the result in the currentPipeline
      //        // instance variable so that it is available to be stopped.
      //        try
      //        {
      //            // currentPipeline.Commands[0].MergeMyResults(PipelineResultTypes.Error, PipelineResultTypes.Output);

      //            // If there was any Input specified, pass it in, and execute the pipeline.
      //            if (Input != null)
      //            {
      //                output = currentPipeline.Invoke(new object[] { Input });
      //            }
      //            else
      //            {
      //                output = currentPipeline.Invoke();
      //            }

      //        }

      //        finally
      //        {
      //            // Dispose of the pipeline line and set it to null, locked because currentPipeline
      //            // may be accessed by the ctrl-C handler.
      //            lock (instanceLock)
      //            {
      //                currentPipeline.Dispose();
      //                currentPipeline = null;
      //            }
      //        }
      //        _ready.Set();
      //    }
      //    return output;
      //}
      ///// <summary>
      ///// A helper method which builds and executes a pipeline that writes to the default output.
      ///// Any exceptions that are thrown are just passed to the caller. 
      ///// Since all output goes to the default output, this method won't return anything.
      ///// </summary>
      ///// <param name="cmd">The script to run</param>
      ///// <param name="Input">Any Input arguments to pass to the script.</param>
      //void ExecuteHelper(string cmd, object Input, bool history)
      //{
      //    //// Ignore empty command lines.
      //    if (String.IsNullOrEmpty(cmd))
      //    {
      //        history = false;
      //        //return;
      //    }

      //    if (_ready.WaitOne(10000, true))
      //    {
      //        //if (history) StringHistory.Add(cmd);

      //        // Create the pipeline object and make it available
      //        // to the ctrl-C handle through the currentPipeline instance
      //        // variable.
      //        lock (instanceLock)
      //        {
      //            currentPipeline = myRunSpace.CreatePipeline(cmd, history);
      //            currentPipeline.StateChanged += new EventHandler<PipelineStateEventArgs>(Pipeline_StateChanged);
      //        }

      //        _outDefault.MergeUnclaimedPreviousCommandResults = PipelineResultTypes.Error | PipelineResultTypes.Output;

      //        // Create a pipeline for this execution. Place the result in the currentPipeline
      //        // instance variable so that it is available to be stopped.
      //        try
      //        {
      //            // currentPipeline.Commands.AddScript(cmd);

      //            //foreach (Command c in currentPipeline.Commands)
      //            //{
      //            //    c.MergeMyResults(PipelineResultTypes.Error, PipelineResultTypes.Output);
      //            //}

      //            // Now add the default outputter to the end of the pipe and indicate
      //            // that it should handle both output and errors from the previous
      //            // commands. This will result in the output being written using the PSHost
      //            // and PSHostUserInterface classes instead of returning objects to the hosting
      //            // application.
      //            currentPipeline.Commands.Add( _outDefault );

      //            currentPipeline.InvokeAsync();
      //            if (Input != null)
      //            {
      //                currentPipeline.Input.Write(Input);
      //            }
      //            currentPipeline.Input.Close();

      //            _ready.Reset();
      //        }
      //        catch
      //        {
      //            // Dispose of the pipeline line and set it to null, locked because currentPipeline
      //            // may be accessed by the ctrl-C handler.
      //            lock (instanceLock)
      //            {
      //                currentPipeline.Dispose();
      //                currentPipeline = null;
      //            }
      //        }
      //    }
      //    else
      //    {
      //        _buffer.WriteError("Timeout - Console Busy, To Cancel Running Pipeline press Esc");
      //    }
      //}
      #endregion ConsoleRichTextBox Event Handlers
      #region Settings
      //void SettingsSettingChanging(object sender, System.Configuration.SettingChangingEventArgs e)
      //{
      //   //e.SettingClass
      //}
      void SettingsPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
      {
         switch (e.PropertyName)
         {
            case "CopyOnMouseSelect":
               {
                  // do nothing, this setting is checked each time you select
               } break;
            //ToDo: REIMPLEMENT 
            case "ScrollBarVisibility":
               {
                  _buffer.Dispatcher.BeginInvoke((Action) delegate
                   {
                      _buffer.VerticalScrollBarVisibility = (ConsoleScrollBarVisibility) (int) Properties.Settings.Default.ScrollBarVisibility;
                   });

               } break;
            default:
               break;
         }
         // we save on every change.
         Properties.Settings.Default.Save();
      }
      #endregion

      #region IPSBackgroundHost Members


      public bool RemoveHotkey(KeyGesture key)
      {


         foreach (HotkeysBehavior behavior in Interaction.GetBehaviors(_psUi as Window).OfType<HotkeysBehavior>())
         {
               behavior.Hotkeys.Remove(new KeyBinding(new ScriptCommand(OnGotUserInput, null), key));
               return true;
         }
         return false;
      }

      public bool AddHotkey(KeyGesture key, ScriptBlock script)
      {
         return (bool)((UIElement)_psUi).Dispatcher.Invoke(DispatcherPriority.Normal, (Func<bool>)(() =>
         {
            try
            {

               HotkeysBehavior hk = Interaction.GetBehaviors(_psUi as Window).OfType<HotkeysBehavior>().FirstOrDefault();
                     if (hk != null)
                     {
                        hk.Hotkeys.Add(new KeyBinding(new ScriptCommand(OnGotUserInput, script), key));
                        return true;
                     }
             
               return false;
            }
            catch { return false; }
         }));
      }

      public IEnumerable<KeyValuePair<KeyGesture, ScriptBlock>> Hotkeys()
      {
         return from behavior in Interaction.GetBehaviors(_psUi as Window).OfType<HotkeysBehavior>() 
                from keyBinding in behavior.Hotkeys 
                select new KeyValuePair<KeyGesture, ScriptBlock>(
                              keyBinding.Gesture as KeyGesture, 
                              keyBinding.Command as ScriptCommand);
      }

      #endregion

      #region IPSXamlHost Members

      public IPSWpfConsole GetWpfConsole()
      {
         return _psUi.Console;
      }

      #endregion
   }
}
