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

namespace Huddled.PoshConsole
{
    /// <summary>
    /// A derivative of RichTextBox ...
    /// Allow input only at the bottom, in plain text ... 
    /// but do context-sensitive highlighting or "error" highlighting?
    /// <remarks>
    /// Ultimately intended for use as a PowerShell console
    /// </remarks>
    /// </summary>
    public class RichTextConsole : RichTextBox
    {
        // events and such ...
        public delegate void CommandHandler(string commandLine);
        public event CommandHandler CommandEntered;

        public delegate List<string> TabCompleteHandler(string commandLine, string lastWord);
        public event TabCompleteHandler TabComplete;

        public delegate string HistoryHandler(ref int index);
        public event HistoryHandler GetHistory;

        ListBox intellisense = new ListBox();
        Popup popup = new Popup();
        EventHandler popupClosing = null;

        /// <summary>
        /// Static initialization of the <see cref="RichTextConsole"/> class.
        /// </summary>
        static RichTextConsole()
        {
            NullOutCommands();

            RegisterClipboardCommands();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RichTextConsole"/> class.
        /// </summary>
        public RichTextConsole()
            : base()
        {
            DataObject.AddPastingHandler(this, OnDataObjectPasting);
            intellisense.SelectionMode = SelectionMode.Single;
            intellisense.SelectionChanged += new SelectionChangedEventHandler(intellisense_SelectionChanged);
            intellisense.PreviewMouseDown += new MouseButtonEventHandler(popup_MouseDown);
            intellisense.IsTextSearchEnabled = true;

            // Add the popup to the logical branch of the console so keystrokes can be
            // processed from the popup by the console for the tab-complete scenario.
            // E.G.: $Host.Pri[tab].  => "$Host.PrivateData." instead of swallowing the period.
            this.AddLogicalChild(popup);

            popup.Child = intellisense;
            popup.PlacementTarget = this;
            intellisense.PreviewTextInput += new TextCompositionEventHandler(popup_TextInput);
            popup.PreviewKeyDown += new KeyEventHandler(popup_PreviewKeyDown);
            //popup.KeyDown += new KeyEventHandler(popup_KeyDown);
            //Popup.KeyDownEvent.AddOwner(GetType());
            //popup.PreviewKeyDown += new KeyEventHandler(OnPreviewKeyDown);
            //this.AddToEventRoute(new EventRoute(KeyDownEvent), new RoutedEventArgs(KeyDownEvent, popup));
            popup.Closed += new EventHandler(popup_Closed);
            popup.StaysOpen = false;

        }


        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            this.myHistory = new List<string>();
        }


        /// <summary>
        /// Registers the clipboard commands.
        /// <remarks>
        /// Command handlers for Cut/Copy/Paste:
        /// <list type="">
        /// <item>Paste automatically goes at the bottom (in the "command" line)</item>
        /// <item>And we only paste plain text</item>
        /// <item>CUT only works in the command line</item>
        /// </list>
        /// </remarks>
        /// </summary>
        private static void RegisterClipboardCommands()
        {

            CommandManager.RegisterClassCommandBinding(typeof(RichTextConsole),
                new CommandBinding(ApplicationCommands.Cut,
                new ExecutedRoutedEventHandler(OnCut),
                new CanExecuteRoutedEventHandler(OnCanExecuteCut)));

            CommandManager.RegisterClassCommandBinding(typeof(RichTextConsole),
                new CommandBinding(ApplicationCommands.Copy,
                new ExecutedRoutedEventHandler(OnCopy),
                new CanExecuteRoutedEventHandler(OnCanExecuteCopy)));

            CommandManager.RegisterClassCommandBinding(typeof(RichTextConsole),
                new CommandBinding(ApplicationCommands.Paste,
                new ExecutedRoutedEventHandler(OnPaste),
                new CanExecuteRoutedEventHandler(OnCanExecutePaste)));

            // TODO: handle dragging & dropping text
        }

        /// <summary>
        /// Null out the formatting commands.
        /// </summary>
        static void NullOutCommands()
        {
            // Disable all formatting by ... not doing anything.
            foreach (RoutedUICommand command in _formattingCommands)
            {
                CommandManager.RegisterClassCommandBinding(typeof(RichTextConsole),
                    new CommandBinding(command, new ExecutedRoutedEventHandler(OnIgnoredCommand),
                    new CanExecuteRoutedEventHandler(OnCanIgnoreCommand)));
            }
        }
        // TextPointers that track the range covering content where words are added.
        private TextPointer selectionStart;
        private TextPointer selectionEnd;

        /// <summary>
        /// Called when pasting.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.Windows.DataObjectPastingEventArgs"/> instance containing the event data.</param>
        private void OnDataObjectPasting(object sender, DataObjectPastingEventArgs e)
        {
            //this.wordsAddedFlag = true;
            this.selectionStart = this.Selection.Start;
            this.selectionEnd = this.Selection.IsEmpty ?
                this.Selection.End.GetPositionAtOffset(0, LogicalDirection.Forward) :
                this.Selection.End;
            e.FormatToApply = "UnicodeText";
            //e.FormatToApply
        }



        //------------------------------------------------------
        //
        //  Event Handlers
        //
        //------------------------------------------------------

        #region Event Handlers

        /// <summary>
        /// Called to check if we can "execute" a command we're ignoring.
        /// Of course, this is always true, since we're always prepared to ignore things...
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="args">The <see cref="System.Windows.Input.CanExecuteRoutedEventArgs"/> instance containing the event data.</param>
        private static void OnCanIgnoreCommand(object target, CanExecuteRoutedEventArgs args)
        {
            args.CanExecute = true;
        }

        /// <summary>
        /// Called when a command is to be ignored.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.Windows.Input.ExecutedRoutedEventArgs"/> instance containing the event data.</param>
        private static void OnIgnoredCommand(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = true; // Actively ignore it, to make sure the base class doesn't handle it.
        }

        /// <summary>
        /// Called on ApplicationCommands.Copy
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.Windows.Input.ExecutedRoutedEventArgs"/> instance containing the event data.</param>
        private static void OnCopy(object sender, ExecutedRoutedEventArgs e)
        {
            ((RichTextConsole)sender).Copy();
            e.Handled = true;
        }

        /// <summary>
        /// Called on ApplicationCommands.Cut
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.Windows.Input.ExecutedRoutedEventArgs"/> instance containing the event data.</param>
        private static void OnCut(object sender, ExecutedRoutedEventArgs e)
        {
            // Clipboard.SetText(((RichTextConsole)sender).Selection.Text);
            // ((RichTextConsole)sender).Selection.Text = String.Empty;

            // TODO: allow cut when on the "command" line, otherwise copy
            ((RichTextConsole)sender).Copy();
            e.Handled = true;
        }

        /// <summary>
        /// Called on ApplicationCommands.Paste
        /// <remarks>
        /// Pasting is only allowed at the END of the content
        /// </remarks>
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.Windows.Input.ExecutedRoutedEventArgs"/> instance containing the event data.</param>
        private static void OnPaste(object sender, ExecutedRoutedEventArgs e)
        {
            RichTextConsole box = (RichTextConsole)sender;

            if (Clipboard.ContainsText())
            {
                // TODO: check if focus is in the "command" line already, if so, insert text at cursor
                if (box.CaretInCommand)
                {
                    box.CaretPosition.InsertTextInRun(Clipboard.GetText(TextDataFormat.UnicodeText));
                }
                else
                {
                    box.Document.ContentEnd.InsertTextInRun(Clipboard.GetText(TextDataFormat.UnicodeText));
                }
            }
            box.ScrollToEnd();
            e.Handled = true;
        }


        /// <summary>
        /// Called when [can execute copy].
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="args">The <see cref="System.Windows.Input.CanExecuteRoutedEventArgs"/> instance containing the event data.</param>
        private static void OnCanExecuteCopy(object target, CanExecuteRoutedEventArgs args)
        {
            RichTextConsole box = (RichTextConsole)target;
            args.CanExecute = box.IsEnabled; // && !box.Selection.IsEmpty;
        }


        /// <summary>
        /// Called when [can execute cut].
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="args">The <see cref="System.Windows.Input.CanExecuteRoutedEventArgs"/> instance containing the event data.</param>
        private static void OnCanExecuteCut(object target, CanExecuteRoutedEventArgs args)
        {
            RichTextConsole box = (RichTextConsole)target;
            args.CanExecute = box.IsEnabled && !box.IsReadOnly && !box.Selection.IsEmpty;
        }


        /// <summary>
        /// Called when [can execute paste].
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="args">The <see cref="System.Windows.Input.CanExecuteRoutedEventArgs"/> instance containing the event data.</param>
        private static void OnCanExecutePaste(object target, CanExecuteRoutedEventArgs args)
        {
            RichTextConsole box = (RichTextConsole)target;
            args.CanExecute = box.IsEnabled && !box.IsReadOnly && Clipboard.ContainsText();
        }


        /// <summary>
        /// Invoked whenever an unhandled <see cref="E:System.Windows.UIElement.GotFocus"></see> event reaches this element in its route.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.RoutedEventArgs"></see> that contains the event data.</param>
        protected override void OnGotFocus(RoutedEventArgs e)
        {
            // when we first get focus, force the focus to the end...
            CaretPosition = Document.ContentEnd;
            base.OnGotFocus(e);
        }

        /// <summary>
        /// Invoked when an unhandled <see cref="E:System.Windows.UIElement.PreviewMouseLeftButtonUp"></see>�routed event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.Input.MouseButtonEventArgs"></see> that contains the event data. The event data reports that the left mouse button was released.</param>
        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (Properties.Settings.Default.CopyOnMouseSelect && Selection.Text.Length > 0)
            {
                Clipboard.SetText(Selection.Text);
                // CaretPosition = Document.ContentEnd;
            }
            base.OnPreviewMouseLeftButtonUp(e);
        }


        /// <summary>
        /// Invoked when an unhandled <see cref="E:System.Windows.UIElement.MouseRightButtonUp"></see>�routed event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.Input.MouseButtonEventArgs"></see> that contains the event data. The event data reports that the right mouse button was released.</param>
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            if (!CaretInCommand)
            {
                CaretPosition = Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            }
            ApplicationCommands.Paste.Execute(null, this);
            //this.Paste();
            e.Handled = true;
        }

        private bool CaretInCommand
        {
            get
            {
                return commandStart.GetOffsetToPosition(CaretPosition) >= 0;
            }
        }

        public string CurrentCommand
        {
            get
            {
                Run cmd = currentParagraph.Inlines.LastInline as Run;

                if (cmd != null)
                {
                    return cmd.Text; //cr.Text
                }
                else
                    return String.Empty;
            }
            set
            {
                Run cmd = currentParagraph.Inlines.LastInline as Run;
                if (cmd != null)
                {
                    cmd.Text = value;
                }
                // EndOfPrompt.DeleteTextInRun(EndOfPrompt.GetTextRunLength(LogicalDirection.Forward));
                //((Run)commandStart.Paragraph.Inlines.LastInline)
                //commandStart.DeleteTextInRun(commandStart.GetTextRunLength(LogicalDirection.Forward));
                //commandStart.InsertTextInRun(value);

                CaretPosition = Document.ContentEnd;
            }
        }



        private DateTime tabTime = DateTime.Now;
        private string lastWord, tabbing = null;
        private int tabbingCount = 0;
        private List<string> completions = null;
        private int historyIndex = 0;
        /// <summary>
        /// Invoked when an unhandled <see cref="E:System.Windows.Input.Keyboard.PreviewKeyDown"></see>�attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.Windows.Input.KeyEventArgs"></see> that contains the event data.</param>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            Trace.TraceInformation("Entering OnPreviewKeyDown:");
            Trace.Indent();
            Trace.WriteLine("Event:  {0}" + e.RoutedEvent);
            Trace.WriteLine("Key:    {0}" + e.Key);
            Trace.WriteLine("Source: {0}" + e.Source);
            if (null == commandStart)
            {
                base.OnPreviewKeyDown(e);
                Trace.Unindent();
                Trace.TraceInformation("Exiting OnPreviewKeyDown:");
                return;
            }
            if (e.Source == intellisense)
            {
                base.OnPreviewKeyDown(e);
                Trace.Unindent();
                Trace.TraceInformation("Exiting OnPreviewKeyDown:");
                return;
            }

            if (currentParagraph.Inlines.Count < promptInlines)
            {
                SetPrompt();
                promptInlines = currentParagraph.Inlines.Count;
            }

            bool inPrompt = CaretInCommand;
            // happens when starting up with a slow profile script
            switch (e.Key)
            {
                case Key.Tab:
                    {
                        if (inPrompt)
                        {
                            tabbingCount += ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) ? -1 : 1;
                            if (tabbing == null)
                            {
                                tabbing = CurrentCommand;
                                if (!string.IsNullOrEmpty(tabbing))
                                {
                                    lastWord = GetLastWord(tabbing);
                                    Cursor = Cursors.Wait;
                                    completions = TabComplete(tabbing, lastWord);
                                    Cursor = Cursors.IBeam;
                                } // make sure it's never an empty string.
                                else tabbing = null;
                            }
                            if (tabbing != null)
                            {
                                if (tabbingCount < 0 || tabbingCount >= completions.Count)
                                {
                                    tabbingCount = 0;
                                }
                                // show the menu if:
                                // TabCompleteMenuThreshold > 0 and there are more items than the threshold
                                // OR they tabbed twice really fast
                                if (    (Properties.Settings.Default.TabCompleteMenuThreshold > 0 
                                        && completions.Count > Properties.Settings.Default.TabCompleteMenuThreshold)
                                    || ((DateTime.Now - tabTime) < TimeSpan.FromSeconds(1)))
                                {
                                    string prefix = tabbing.Substring(0, tabbing.Length - lastWord.Length);
                                    popupClosing = new EventHandler(TabComplete_Closed);
                                    popup.Closed += popupClosing;
                                    ShowPopup(completions, false, false);
                                }
                                else if(tabbingCount < completions.Count)
                                {
                                    CurrentCommand = tabbing.Substring(0, tabbing.Length - lastWord.Length) + completions[tabbingCount];
                                }
                            }
                        }
                        tabTime = DateTime.Now;
                        e.Handled = true;
                    } break;
                case Key.F7:
                    {
                        popupClosing = new EventHandler(History_Closed);
                        popup.Closed += popupClosing;
                        ShowPopup(myHistory, true, Properties.Settings.Default.HistoryMenuFilterDupes);

                    } break;
                case Key.Up:
                    {
                        if (inPrompt && ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != ModifierKeys.Control))
                        {
                            historyIndex++;
                            if (null != GetHistory)
                            {
                                CurrentCommand = GetHistory(ref historyIndex);
                            }
                            else
                            {
                                if (historyIndex == -1)
                                {
                                    historyIndex = StringHistory.Count;
                                }
                                if (historyIndex > 0 && historyIndex <= StringHistory.Count)
                                {
                                    CurrentCommand = StringHistory[StringHistory.Count - historyIndex];
                                }
                                else
                                {
                                    historyIndex = 0;
                                    CurrentCommand = string.Empty;
                                }
                            }
                            e.Handled = true;
                        }
                    } break;
                case Key.Down:
                    {
                        if (inPrompt)
                        {
                            historyIndex--;
                            if (null != GetHistory)
                            {
                                CurrentCommand = GetHistory(ref historyIndex);
                            }
                            else
                            {
                                if (historyIndex == -1)
                                {
                                    historyIndex = StringHistory.Count;
                                }
                                if (historyIndex > 0 && historyIndex <= StringHistory.Count)
                                {
                                    CurrentCommand = StringHistory[StringHistory.Count - historyIndex];
                                }
                                else
                                {
                                    historyIndex = 0;
                                    CurrentCommand = string.Empty;
                                }
                            }
                            e.Handled = true;
                            e.Handled = true;
                        }
                    } break;

                case Key.Escape:
                    {
                        historyIndex = 0;
                        CurrentCommand = "";
                    } break;
                case Key.Return:
                    {
                        IsUndoEnabled = false;
                        IsUndoEnabled = true;
                        
                        // keep them in the same (relative) place in the history buffer.
                        if( historyIndex != 0 ) historyIndex++; 

                        if (CommandEntered != null)
                        {   // the "EndOfOutput" marker gets stuck just before the last character of the prompt...
                            string cmd = CurrentCommand;
                            //TextRange tr = new TextRange(currentParagraph.ContentStart.GetPositionAtOffset(promptLength), currentParagraph.ContentEnd);
                            //string cmd = EndOfPrompt.GetTextInRun(LogicalDirection.Forward);
                            currentParagraph.ContentEnd.InsertLineBreak();
                            myHistory.Add(cmd);
                            CommandEntered(cmd);
                        }
                        e.Handled = true;
                    } break;
                case Key.Left:
                    {
                        // cancel the "left" if we're at the left edge of the prompt
                        if (commandStart.GetOffsetToPosition(CaretPosition) >= 0 &&
                            commandStart.GetOffsetToPosition(CaretPosition.GetNextInsertionPosition(LogicalDirection.Backward)) < 0)
                        {
                            e.Handled = true;
                        }
                    } break;

                case Key.Home:
                    {
                        // if we're in the command string ... handle it ourselves 
                        if (inPrompt)
                        {
                            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                            {
                                Selection.Select(CaretPosition, commandStart.GetInsertionPosition(LogicalDirection.Forward));
                            }
                            else
                            {
                                // TODO: if Control, goto top ... e.KeyboardDevice.Modifiers
                                //CaretPosition = commandStart = Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);

                                CaretPosition = commandStart.GetInsertionPosition(LogicalDirection.Forward);//currentParagraph.ContentStart.GetPositionAtOffset(promptLength);
                            }
                            e.Handled = true;
                        }
                    } break;
                case Key.End:
                    {
                        // shift + ctrl
                        if (((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                            && ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
                        {
                            Selection.Select(Selection.Start, CaretPosition.DocumentEnd);
                        }
                        else if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                        {
                            CaretPosition = CaretPosition.DocumentEnd;
                        }
                        else if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                        {
                            Selection.Select(Selection.Start, (CaretPosition.GetLineStartPosition(1) ?? CaretPosition.DocumentEnd).GetInsertionPosition(LogicalDirection.Backward));
                        }
                        else
                        {
                            CaretPosition = (CaretPosition.GetLineStartPosition(1) ?? CaretPosition.DocumentEnd).GetNextInsertionPosition(LogicalDirection.Backward);
                        }

                        e.Handled = true;
                    } break;
                case Key.Back:
                    goto case Key.Delete;
                case Key.Delete:
                    {
                        if (!Selection.IsEmpty)
                        {
                            if (Selection.Start.GetOffsetToPosition(commandStart) >= 0)
                            {
                                Selection.Select(commandStart.GetInsertionPosition(LogicalDirection.Forward), Selection.End);
                            }
                            if (Selection.End.GetOffsetToPosition(commandStart) >= 0)
                            {
                                Selection.Select(Selection.Start, commandStart.GetInsertionPosition(LogicalDirection.Forward));
                            }
                            Selection.Text = string.Empty;
                            e.Handled = true;
                        }
                        else
                        {
                            int offset = 0;
                            if (currentParagraph.Inlines.Count > promptInlines)
                            {
                                offset = currentParagraph.Inlines.LastInline.ElementStart.GetOffsetToPosition(CaretPosition);
                            }
                            else
                            {
                                offset = commandStart.GetInsertionPosition(LogicalDirection.Forward).GetOffsetToPosition(CaretPosition);
                            }
                            if (offset < 0 || (offset == 0 && e.Key == Key.Back) || CurrentCommand.Length <= 0)
                            {
                                e.Handled = true;
                            }
                        }
                    } break;
                // we're only handling this to avoid the default handler when you try to copy
                // since that would de-select the text
                case Key.X: goto case Key.C;
                case Key.C:
                    {
                        if ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                        {
                            goto default;
                        }
                    } break;

                // here's a few keys we want the base control to handle:
                case Key.Right: break;
                case Key.RightAlt: break;
                case Key.LeftAlt: break;
                case Key.RightCtrl: break;
                case Key.LeftCtrl: break;
                case Key.RightShift: break;
                case Key.LeftShift: break;
                case Key.RWin: break;
                case Key.LWin: break;
                case Key.CapsLock: break;
                case Key.Insert: break;
                case Key.NumLock: break;
                case Key.PageUp: break;
                case Key.PageDown: break;

                // if a key isn't in the list above, then make sure we're in the prompt before we let it through
                default:
                    {
                        tabbing = null;
                        tabbingCount = 0;

                        //System.Diagnostics.Debug.WriteLine(CaretPosition.GetOffsetToPosition(EndOfOutput));
                        if (commandStart == null || CaretPosition.GetOffsetToPosition(commandStart) > 0)
                        {
                            CaretPosition = Document.ContentEnd.GetInsertionPosition(LogicalDirection.Forward);
                        }
                        // if they type anything, they're not using the history buffer.
                        historyIndex = 0;
                        // if they type anything, they're no longer using the autocopy
                        autoCopy = false;
                    } break;
                #endregion
            }
            base.OnPreviewKeyDown(e);
            Trace.Unindent();
            Trace.TraceInformation("Exiting OnPreviewKeyDown:");
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            // if they're trying to input text, they will overwrite the selection
            // lets make sure they don't overwrite the history buffer
            if (!Selection.IsEmpty)
            {
                if (Selection.Start.GetOffsetToPosition(commandStart) >= 0)
                {
                    Selection.Select(commandStart.GetInsertionPosition(LogicalDirection.Forward), Selection.End);
                }
                if (Selection.End.GetOffsetToPosition(commandStart) >= 0)
                {
                    Selection.Select(Selection.Start, commandStart.GetInsertionPosition(LogicalDirection.Forward));
                }
            }
            base.OnPreviewTextInput(e);
        }


        static Regex chunker = new Regex(@"[^ ""']+|([""'])[^\1]*?\1[^ ""']*|([""'])[^\1]*$", RegexOptions.Compiled);

        private string GetLastWord(string cmdline)
        {
            string lastWord = null;
            MatchCollection words = chunker.Matches(cmdline);
            if (words.Count >= 1)
            {
                Match lw = words[words.Count - 1];
                lastWord = lw.Value;
                if (lastWord[0] == '"')
                {
                    lastWord = lastWord.Replace("\"", string.Empty);
                }
                else if (lastWord[0] == '\'')
                {
                    lastWord = lastWord.Replace("'", string.Empty);
                }
            }
            return lastWord;
        }

        private void ShowPopup(List<string> items, bool number, bool FilterDupes)
        {
            intellisense.Items.Clear();

            if (number)
            {
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (!FilterDupes || !intellisense.Items.Contains(items[i]))
                    {
                        ListBoxItem item = new ListBoxItem();
                        TextSearch.SetText(item, items[i]); // A name must start with a letter or the underscore character (_), and must contain only letters, digits, or underscores
                        item.Content = string.Format("{0,2} {1}", i, items[i]);
                        intellisense.Items.Insert(0, item);
                    }
                }
            }
            else
            {

                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (!FilterDupes || !intellisense.Items.Contains(items[i]))
                    {
                        intellisense.Items.Insert(0, items[i]);
                    }
                }
            }
            intellisense.Visibility = Visibility.Visible;
            // if it's numbered, default to the last item
            intellisense.SelectedIndex = number ? items.Count - 1 : 0;
            intellisense.ScrollIntoView(intellisense.SelectedItem);

            popup.PlacementRectangle = new Rect(CursorPosition.X, CursorPosition.Y, this.ActualWidth - CursorPosition.X, this.ActualHeight - CursorPosition.Y);
            popup.Placement = PlacementMode.RelativePoint;

            popup.IsOpen = true;   // show the popup
            intellisense.Focus();  // focus the keyboard on the popup
        }

        #region Handle Clicks on the Intellisense popup.
        bool popupClicked = false;
        void intellisense_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // if they clicked, then when the selection changes we close.
            if (popupClicked) popup.IsOpen = false;
            popupClicked = false;
        }

        void popup_MouseDown(object sender, MouseButtonEventArgs e)
        {
            popupClicked = true;
        }
        #endregion

        /// <summary>
        /// Handles the Closed event of the popup control.
        /// Always reset the intellisense typed number...
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        void popup_Closed(object sender, EventArgs e)
        {
            intelliNum = -1;
        }

        /// <summary>
        /// Handles the Closed event of the History popup menu.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="ea">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        void History_Closed(object sender, EventArgs ea)
        {
            if (intellisense.SelectedValue != null)
            {
                CurrentCommand = TextSearch.GetText((ListBoxItem)intellisense.SelectedValue);
            }
            this.Focus();
            popup.Closed -= popupClosing;
        }

        /// <summary>
        /// Handles the Closed event of the TabComplete popup menu.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="ea">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        void TabComplete_Closed(object sender, EventArgs ea)
        {
            Trace.TraceInformation("Entering TabComplete_Closed");
            Trace.Indent();
            // WHY oh WHY are there three enums with different values for all the keys, when the Windows API clearly should be the gold standard?
            // System.ConsoleKey System.Windows.Forms.Keys System.Windows.Input.Key
            //System.Windows.Input.KeyInterop.VirtualKeyFromKey(

            if (intellisense.SelectedValue != null)
            {
                string cmd = CurrentCommand;
                Trace.TraceInformation("CurrentCommand: {0}", cmd);
                CurrentCommand = cmd.Substring(0, cmd.Length - GetLastWord(cmd).Length) + intellisense.SelectedValue.ToString() + post;
            }
            this.Focus();
            popup.Closed -= popupClosing;
            Trace.Unindent();
            Trace.TraceInformation("Exiting TabComplete_Closed");
        }

        static Regex tabseparator = new Regex(@"[.;,\\ |/[\]()""']", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        static Regex number = new Regex(@"[0-9]", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        string post = string.Empty;
        /// <summary>
        /// Handles the TextInput event of the popup control 
        /// 1) to save the key if it's one we consider to toggle the tab-complete
        /// 2) to handle typing numbers for the history menu
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Input.TextCompositionEventArgs"/> instance containing the event data.</param>
        void popup_TextInput(object sender, TextCompositionEventArgs e)
        {
            if( tabseparator.IsMatch(e.Text) ) {
                post = e.Text;
                popup.IsOpen = false;
            }

            if (number.IsMatch(e.Text))
            {
                if (intelliNum >= 0)
                {
                    intelliNum *= 10;
                }
                else intelliNum = 0;

                intelliNum += int.Parse(e.Text);
                if (intelliNum > 0 && intelliNum < intellisense.Items.Count - 1)
                {
                    intellisense.SelectedIndex = intelliNum;
                }
            }
        }

        int intelliNum = -1;
        void popup_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Trace.TraceInformation("Entering popup_PreviewKeyDown:");
            Trace.Indent();
            Trace.WriteLine("Event:  {0}" + e.RoutedEvent);
            Trace.WriteLine("Key:    {0}" + e.Key);
            Trace.WriteLine("Source: {0}" + e.Source);

            post = string.Empty;

            switch (e.Key)
            {
                case Key.Space:
                    {
                        post = " ";
                        e.Handled = true;
                        popup.IsOpen = false;
                    } break;
                case Key.Back: goto case Key.Escape;
                case Key.Delete: goto case Key.Escape;
                case Key.Escape:
                    {
                        intellisense.SelectedIndex = -1;
                        e.Handled = false;
                        popup.IsOpen = false;
                    } break;
                case Key.Tab: goto case Key.Enter;
                case Key.Enter:
                    {
                        e.Handled = true;
                        popup.IsOpen = false;
                    } break;
            }
            Trace.Unindent();
            Trace.TraceInformation("Exiting popup_PreviewKeyDown");
        }


        
        //------------------------------------------------------
        //
        //  Private Methods
        //
        //------------------------------------------------------

        #region Private Methods

        ///// <summary>
        ///// Helper to apply formatting properties to matching words in the document.
        ///// </summary>
        //private void FormatWords()
        //{
        //    // Applying formatting properties, triggers another TextChangedEvent. Remove event handler temporarily.
        //    this.TextChanged -= this.TextChangedEventHandler;

        //    // Add formatting for matching words.
        //    foreach (Word word in _words)
        //    {
        //        TextRange range = new TextRange(word.Start, word.End);
        //        range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Blue));
        //        range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
        //    }
        //    _words.Clear();

        //    // Add TextChanged handler back.
        //    this.TextChanged += this.TextChangedEventHandler;
        //}

        ///// <summary>
        ///// Scans passed Run's text, for any matching words from dictionary.
        ///// </summary>
        //private void AddMatchingWordsInRun(Run run)
        //{
        //    string runText = run.Text;

        //    int wordStartIndex = 0;
        //    int wordEndIndex = 0;
        //    for (int i = 0; i < runText.Length; i++)
        //    {
        //        if (Char.IsWhiteSpace(runText[i]))
        //        {
        //            if (i > 0 && !Char.IsWhiteSpace(runText[i - 1]))
        //            {
        //                wordEndIndex = i - 1;
        //                string wordInRun = runText.Substring(wordStartIndex, wordEndIndex - wordStartIndex + 1);

        //                if (keywords1.ContainsKey(wordInRun))
        //                {
        //                    TextPointer wordStart = run.ContentStart.GetPositionAtOffset(wordStartIndex, LogicalDirection.Forward);
        //                    TextPointer wordEnd = run.ContentStart.GetPositionAtOffset(wordEndIndex + 1, LogicalDirection.Backward);
        //                    _words.Add(new Word(wordStart, wordEnd));
        //                }
        //            }
        //            wordStartIndex = i + 1;
        //        }
        //    }

        //    // Check if the last word in the Run is a matching word.
        //    string lastWordInRun = runText.Substring(wordStartIndex, runText.Length - wordStartIndex);
        //    if (keywords1.ContainsKey(lastWordInRun))
        //    {
        //        TextPointer wordStart = run.ContentStart.GetPositionAtOffset(wordStartIndex, LogicalDirection.Forward);
        //        TextPointer wordEnd = run.ContentStart.GetPositionAtOffset(runText.Length, LogicalDirection.Backward);
        //        _words.Add(new Word(wordStart, wordEnd));
        //    }
        //}

        #endregion

        //------------------------------------------------------
        //
        //  Private Types
        //
        //------------------------------------------------------

        #region Private Types

        /// <summary>
        /// This class encapsulates a matching word by two TextPointer positions, 
        /// start and end, with forward and backward gravities respectively.
        /// </summary>
        private class Word
        {
            public Word(TextPointer wordStart, TextPointer wordEnd)
            {
                _wordStart = wordStart.GetPositionAtOffset(0, LogicalDirection.Forward);
                _wordEnd = wordEnd.GetPositionAtOffset(0, LogicalDirection.Backward);
            }

            public TextPointer Start
            {
                get
                {
                    return _wordStart;
                }
            }

            public TextPointer End
            {
                get
                {
                    return _wordEnd;
                }
            }

            private readonly TextPointer _wordStart;
            private readonly TextPointer _wordEnd;
        }

        #endregion

        //------------------------------------------------------
        //
        //  Private Members
        //
        //------------------------------------------------------

        #region Private Members

        private List<string> myHistory;
        public List<string> StringHistory
        {
            get { return myHistory; }
            set { myHistory = value; }
        }


        // Static list of editing formatting commands. In the ctor we disable all these commands.
        private static readonly RoutedUICommand[] _formattingCommands = new RoutedUICommand[]
            {
                EditingCommands.ToggleBold,
                EditingCommands.ToggleItalic,
                EditingCommands.ToggleUnderline,
                EditingCommands.ToggleSubscript,
                EditingCommands.ToggleSuperscript,
                EditingCommands.IncreaseFontSize,
                EditingCommands.DecreaseFontSize,
                EditingCommands.ToggleBullets,
                EditingCommands.ToggleNumbering,
                EditingCommands.AlignCenter,
                EditingCommands.AlignJustify,
                EditingCommands.AlignRight,
                EditingCommands.AlignLeft,              
            };

        #endregion Private Members

        int promptInlines = 0;
        TextPointer commandStart = null;


        public delegate void EndOutputDelegate();
        public delegate void PromptDelegate(string prompt);
        public delegate void ColoredPromptDelegate(Nullable<ConsoleColor> foreground, Nullable<ConsoleColor> background, string prompt);
        /// <summary>
        /// Prompts with the default colors
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        public void Prompt(string prompt)
        {
            Prompt(Foreground, Brushes.Transparent, prompt);
        }

        /// <summary>
        /// Prompts with specified colors.
        /// </summary>
        /// <param name="background">The background.</param>
        /// <param name="foreground">The foreground.</param>
        /// <param name="prompt">The prompt text.</param>
        public void Prompt(Nullable<ConsoleColor> foreground, Nullable<ConsoleColor> background, string prompt)
        {
            Prompt(
                ( foreground == null ) ? Foreground : BrushFromConsoleColor(foreground), 
                ( foreground == null ) ? Brushes.Transparent : BrushFromConsoleColor(background), 
                prompt);
        }

        /// <summary>
        /// Prompts with specified colors.
        /// </summary>
        /// <param name="background">The background.</param>
        /// <param name="foreground">The foreground.</param>
        /// <param name="prompt">The prompt text.</param>
        public void Prompt(Brush foreground, Brush background, string prompt)
        {
            TrimCurrentParagraph();

            //currentParagraph.ContentStart.GetOffsetToPosition(currentParagraph.ContentEnd) + PromptPadding.Length;
            Run prmt = new Run(prompt);
            if( !background.Equals( this.Background ) ) prmt.Background = background;
            prmt.Foreground = foreground;
            currentParagraph.Inlines.Add(prmt);

            SetPrompt();
            // toggle undo to prevent "undo"ing past this point.
            IsUndoEnabled = false;
            IsUndoEnabled = true;
            promptInlines = currentParagraph.Inlines.Count;
        }

        private void SetPrompt()
        {
            Run command = new Run("", currentParagraph.ContentEnd.GetInsertionPosition(LogicalDirection.Forward)); // , Document.ContentEnd
            command.Background = Brushes.Transparent;
            command.Foreground = Foreground;

            ScrollToEnd();
            CaretPosition = commandStart = Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        }

        Paragraph currentParagraph = null;

        /// <summary>
        /// Right before a prompt we want to insert a new paragraph...
        /// But we want to trim any whitespace off the end of the output first 
        /// because the paragraph mark makes plenty of whitespace
        /// </summary>
        public void EndOutput()
        {
            promptInlines = 0; // there are no promt inlines we need to save

            if (currentParagraph != null)
            {
                TrimCurrentParagraph();
                if (currentParagraph.Margin.Bottom == 0 && currentParagraph.Margin.Top == 0)
                {
                    currentParagraph.ContentEnd.InsertLineBreak();
                }
            }
            Document.ContentEnd.InsertParagraphBreak();
            currentParagraph = (Paragraph)Document.Blocks.LastBlock;
        }

        /// <summary>
        /// Trims whitespace from the current paragraph.
        /// </summary>
        public void TrimCurrentParagraph()
        {
            BeginChange();
            // if the paragraph has content
            if (currentParagraph.Inlines.Count > promptInlines)
            {
                // trim from the end until we run out of inlines or hit some non-whitespace
                Inline ln = currentParagraph.Inlines.LastInline;
                while (ln != null)
                {
                    Run run = ln as Run;
                    if (run != null)
                    {
                        run.Text = run.Text.TrimEnd();
                        // if there's text in this run, stop trimming!!!
                        if (run.Text.Length > 0) break;
                    }
                    // if( run == null || run.Text.Length == 0 )
                    Inline tmp = ln;
                    ln = ln.PreviousInline;
                    currentParagraph.Inlines.Remove(tmp);
                }
            }
            EndChange();
        }



        public delegate void WriteOutputDelegate(Nullable<ConsoleColor> foreground, Nullable<ConsoleColor> background, string text, bool lineBreak);
        public void WriteOutput(Nullable<ConsoleColor> foreground, Nullable<ConsoleColor> background, string text, bool lineBreakAtEnd)
        {
            if (currentParagraph == null)
            {
                currentParagraph = new Paragraph();
                Document.Blocks.Add(currentParagraph);
            }

            TextPointer currentPos = CaretPosition;

            BeginChange();
            string[] ansis = text.Split(new string[] { "\x1B[" }, StringSplitOptions.None);

            if (ansis.Length == 1)
            {
                Run insert = new Run(text, currentParagraph.ContentEnd);
                insert.Background = (background == null) ? Brushes.Transparent : BrushFromConsoleColor(background);
                insert.Foreground = (foreground == null) ? this.Foreground : BrushFromConsoleColor(foreground);
            }
            else 
            {
                Brush bg = (background == null) ? Brushes.Transparent : BrushFromConsoleColor(background);
                Brush fg = (foreground == null) ? this.Foreground : BrushFromConsoleColor(foreground);

                Boolean first = true;
                foreach (string ansi in ansis)
                {
                    if (first && ansi.Length > 0)
                    {
                        Run insert = new Run(ansi, currentParagraph.ContentEnd);
                        insert.Background = bg;
                        insert.Foreground = fg;
                    } 
                    else if (ansi.Length > 0)
                    {
                        // bool bg = false;
                        int m1 = ansi.IndexOf('m');
                        int m2 = ansi.IndexOf(']');
                        int split = -1;
                        if( m1 > 0 && (m2 < 0 || m1 < m2) ) {
                            split = m1;
                        } else if( m2 > 0 ) {
                            split = m2;
                        }
                        bool dark = true, back = false;
                        Run insert = new Run(ansi.Substring(split + 1), currentParagraph.ContentEnd);
                        insert.Foreground = fg;
                        insert.Background = bg;
                        if (split > 0)
                        {
                            foreach (string code in ansi.Substring(0, split).Split(';'))
                            {
                                switch (code.ToUpper())
                                {
                                    case "0":     goto case "RESET";// RESET
                                    case "CLEAR": goto case "RESET";
                                    case "RESET":
                                        insert.Background = bg = Brushes.Transparent;
                                        insert.Foreground = Foreground;
                                        insert.FontStyle  = FontStyles.Normal;
                                        insert.FontWeight = FontWeights.Normal;
                                        insert.TextDecorations.Clear();
                                        break;
                                    case "1":
                                        dark = false;
                                        break;
                                    case "2":
                                        dark = true;
                                        break;
                                    case "4": goto case "UNDERLINE";
                                    case "UNDERLINE":
                                        insert.TextDecorations.Add(TextDecorations.Underline);
                                        break;
                                    case "5": // blink 
                                        goto case "ITALIC";
                                    case "ITALIC":
                                        insert.FontStyle = FontStyles.Italic;
                                        break;
                                    //case "6": // blink faster
                                    case "7": // inverse
                                        goto case "BOLD";
                                    case "BOLD":
                                        insert.FontWeight = FontWeights.Bold;
                                        break;
                                    case "8": // hidden (uhm, no)
                                        goto case "THIN";
                                    case "THIN":
                                        insert.FontWeight = FontWeights.Thin;
                                        // insert.Foreground = ConsoleBrushes.Transparent;
                                        break;
                                    
                                    #region ANSI COLOR SEQUENCES 30-37 and 40-47


                                    case "30":
                                        insert.Foreground = (dark) ? ConsoleBrushes.Black : ConsoleBrushes.DarkGray;
                                        break;
                                    case "40":
                                        insert.Background = (dark) ? ConsoleBrushes.Black : ConsoleBrushes.DarkGray;
                                        break;

                                    case "31":
                                        insert.Foreground = (dark) ? ConsoleBrushes.DarkRed : ConsoleBrushes.Red;
                                        break;
                                    case "41":
                                        insert.Background = (dark) ? ConsoleBrushes.DarkRed : ConsoleBrushes.Red;
                                        break;

                                    case "32":
                                        insert.Foreground = (dark) ? ConsoleBrushes.DarkGreen : ConsoleBrushes.Green;
                                        break;
                                    case "42":
                                        insert.Background = (dark) ? ConsoleBrushes.DarkGreen : ConsoleBrushes.Green;
                                        break;

                                    case "33":
                                        insert.Foreground = (dark) ? ConsoleBrushes.DarkYellow : ConsoleBrushes.Yellow;
                                        break;
                                    case "43":
                                        insert.Background = (dark) ? ConsoleBrushes.DarkYellow : ConsoleBrushes.Yellow;
                                        break;

                                    case "34":
                                        insert.Foreground = (dark) ? ConsoleBrushes.DarkBlue : ConsoleBrushes.Blue;
                                        break;
                                    case "44":
                                        insert.Background = (dark) ? ConsoleBrushes.DarkBlue : ConsoleBrushes.Blue;
                                        break;

                                    case "35":
                                        insert.Foreground = (dark) ? ConsoleBrushes.DarkMagenta : ConsoleBrushes.Magenta;
                                        break;
                                    case "45":
                                        insert.Background = (dark) ? ConsoleBrushes.DarkMagenta : ConsoleBrushes.Magenta;
                                        break;

                                    case "36":
                                        insert.Foreground = (dark) ? ConsoleBrushes.DarkCyan : ConsoleBrushes.Cyan;
                                        break;
                                    case "46":
                                        insert.Background = (dark) ? ConsoleBrushes.DarkCyan : ConsoleBrushes.Cyan;
                                        break;

                                    case "37":
                                        insert.Foreground = (dark) ? ConsoleBrushes.White : ConsoleBrushes.Gray;
                                        break;
                                    case "47":
                                        insert.Background = (dark) ? ConsoleBrushes.White : ConsoleBrushes.Gray;
                                        break;

                                    
                                    #endregion ANSI COLOR SEQUENCES 30-37 and 40-47

                                    #region ConsoleColor Enumeration values
                                    case "TRANSPARENT":
                                        {
                                            if (back)
                                            {
                                                insert.Background = ConsoleBrushes.Transparent;
                                            }
                                            else
                                            {
                                                insert.Foreground = ConsoleBrushes.Transparent;
                                            }
                                            back = true;
                                        } break;
                                    #endregion ConsoleColor Enumeration values

                                    default:
                                        if (code[0] == '#')
                                        {
                                            #region parse hex color codes
                                            try
                                            {
                                                byte a, r, g, b;
                                                // if there's an alpha value...
                                                if (code.Length == 9)
                                                {
                                                    a = Byte.Parse(code.Substring(1, 2));
                                                    r = Byte.Parse(code.Substring(3, 2));
                                                    g = Byte.Parse(code.Substring(5, 2));
                                                    b = Byte.Parse(code.Substring(7, 2));
                                                }
                                                else if (code.Length == 7)
                                                {
                                                    r = Byte.Parse(code.Substring(1, 2));
                                                    g = Byte.Parse(code.Substring(3, 2));
                                                    b = Byte.Parse(code.Substring(5, 2));
                                                    a = Byte.MaxValue;
                                                }
                                                else break;

                                                if (back)
                                                {
                                                    insert.Background = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                                                }
                                                else
                                                {
                                                    insert.Foreground = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                                                }

                                            }
                                            catch { } // just ignore the sequence?
                                            finally { back = true; }
                                            #endregion parse hex color codes
                                        } else {
                                            #region parse ConsoleColor enum values
                                            try
                                            {
                                                if (back)
                                                {
                                                    insert.Background = BrushFromConsoleColor((ConsoleColor)Enum.Parse(typeof(ConsoleColor), code));
                                                }
                                                else
                                                {
                                                    insert.Foreground = BrushFromConsoleColor((ConsoleColor)Enum.Parse(typeof(ConsoleColor), code));
                                                }
                                            }
                                            catch { } // just ignore the sequence?
                                            finally { back = true; }
                                            #endregion parse ConsoleColor enum values
                                        } break;


                                }
                            }
                            bg = insert.Background;
                            fg = insert.Foreground;
                        }
                    }
                    first = false;
                }
            }


            if (lineBreakAtEnd)
            {
                currentParagraph.ContentEnd.InsertLineBreak();
            }

            EndChange();

            SetPrompt();
        }

        #region ConsoleBrushes

        public ConsoleColor ConsoleBackground
        {
            get
            {
                return ConsoleColorFromBrush(Background);
            }
            set
            {
                Background = BrushFromConsoleColor(value);
            }
        }

        public ConsoleColor ConsoleForeground
        {
            get
            {
                return ConsoleColorFromBrush(Foreground);
            }
            set
            {
                Foreground = BrushFromConsoleColor(value);
            }
        }



        public struct ConsoleBrushes
        {
            public static SolidColorBrush Black = new SolidColorBrush(Properties.Settings.Default.ConsoleBlack);
            public static SolidColorBrush Blue = new SolidColorBrush(Properties.Settings.Default.ConsoleBlue);
            public static SolidColorBrush Cyan = new SolidColorBrush(Properties.Settings.Default.ConsoleCyan);
            public static SolidColorBrush DarkBlue = new SolidColorBrush(Properties.Settings.Default.ConsoleDarkBlue);
            public static SolidColorBrush DarkCyan = new SolidColorBrush(Properties.Settings.Default.ConsoleDarkCyan);
            public static SolidColorBrush DarkGray = new SolidColorBrush(Properties.Settings.Default.ConsoleDarkGray);
            public static SolidColorBrush DarkGreen = new SolidColorBrush(Properties.Settings.Default.ConsoleDarkGreen);
            public static SolidColorBrush DarkMagenta = new SolidColorBrush(Properties.Settings.Default.ConsoleDarkMagenta);
            public static SolidColorBrush DarkRed = new SolidColorBrush(Properties.Settings.Default.ConsoleDarkRed);
            public static SolidColorBrush DarkYellow = new SolidColorBrush(Properties.Settings.Default.ConsoleDarkYellow);
            public static SolidColorBrush Gray = new SolidColorBrush(Properties.Settings.Default.ConsoleGray);
            public static SolidColorBrush Green = new SolidColorBrush(Properties.Settings.Default.ConsoleGreen);
            public static SolidColorBrush Magenta = new SolidColorBrush(Properties.Settings.Default.ConsoleMagenta);
            public static SolidColorBrush Red = new SolidColorBrush(Properties.Settings.Default.ConsoleRed);
            public static SolidColorBrush White = new SolidColorBrush(Properties.Settings.Default.ConsoleWhite);
            public static SolidColorBrush Yellow = new SolidColorBrush(Properties.Settings.Default.ConsoleYellow);
            public static SolidColorBrush Transparent = Brushes.Transparent;
        }

        public static Brush BrushFromConsoleColor(Nullable<ConsoleColor> color)
        {
            switch (color)
            {
                case ConsoleColor.Black:
                    return ConsoleBrushes.Black;
                case ConsoleColor.Blue:
                    return ConsoleBrushes.Blue;
                case ConsoleColor.Cyan:
                    return ConsoleBrushes.Cyan;
                case ConsoleColor.DarkBlue:
                    return ConsoleBrushes.DarkBlue;
                case ConsoleColor.DarkCyan:
                    return ConsoleBrushes.DarkCyan;
                case ConsoleColor.DarkGray:
                    return ConsoleBrushes.DarkGray;
                case ConsoleColor.DarkGreen:
                    return ConsoleBrushes.DarkGreen;
                case ConsoleColor.DarkMagenta:
                    return ConsoleBrushes.DarkMagenta;
                case ConsoleColor.DarkRed:
                    return ConsoleBrushes.DarkRed;
                case ConsoleColor.DarkYellow:
                    return ConsoleBrushes.DarkYellow;
                case ConsoleColor.Gray:
                    return ConsoleBrushes.Gray;
                case ConsoleColor.Green:
                    return ConsoleBrushes.Green;
                case ConsoleColor.Magenta:
                    return ConsoleBrushes.Magenta;
                case ConsoleColor.Red:
                    return ConsoleBrushes.Red;
                case ConsoleColor.White:
                    return ConsoleBrushes.White;
                case ConsoleColor.Yellow:
                    return ConsoleBrushes.Yellow;
                default: // for NULL values
                    return ConsoleBrushes.Transparent;
            }
        }

        public static ConsoleColor ConsoleColorFromBrush(Brush color)
        {
            if (color == ConsoleBrushes.Black)
                return ConsoleColor.Black;
            else if (color == ConsoleBrushes.Blue)
                return ConsoleColor.Blue;
            else if (color == ConsoleBrushes.Cyan)
                return ConsoleColor.Cyan;
            else if (color == ConsoleBrushes.DarkBlue)
                return ConsoleColor.DarkBlue;
            else if (color == ConsoleBrushes.DarkCyan)
                return ConsoleColor.DarkCyan;
            else if (color == ConsoleBrushes.DarkGray)
                return ConsoleColor.DarkGray;
            else if (color == ConsoleBrushes.DarkGreen)
                return ConsoleColor.DarkGreen;
            else if (color == ConsoleBrushes.DarkMagenta)
                return ConsoleColor.DarkMagenta;
            else if (color == ConsoleBrushes.DarkRed)
                return ConsoleColor.DarkRed;
            else if (color == ConsoleBrushes.DarkYellow)
                return ConsoleColor.DarkYellow;
            else if (color == ConsoleBrushes.Gray)
                return ConsoleColor.Gray;
            else if (color == ConsoleBrushes.Green)
                return ConsoleColor.Green;
            else if (color == ConsoleBrushes.Magenta)
                return ConsoleColor.Magenta;
            else if (color == ConsoleBrushes.Red)
                return ConsoleColor.Red;
            else if (color == ConsoleBrushes.White)
                return ConsoleColor.White;
            else if (color == ConsoleBrushes.Yellow)
                return ConsoleColor.Yellow;
            else
                return ConsoleColor.White;
        }
        #endregion

        /// <summary>
        /// Implements the CLS command the way most command-lines do:
        /// Scroll the window until the prompt is at the top ...
        /// (as opposed to clearing the screen and leaving the prompt at the bottom)
        /// </summary>        
        public void ClearScreen()
        {
            new TextRange(Document.ContentStart, Document.ContentEnd).Text = String.Empty;
            //SelectAll();
            //Selection.Text = String.Empty;
        }


        private bool autoCopy = false;
        /// <summary>
        /// Copies the current selection of the text editing control to the <see cref="T:System.Windows.Clipboard"></see>.
        /// <remarks>
        /// Overrides the base behavior of the RichTextbox, providing a feature which allows automatic 
        /// copying of full "paragraphs" (that is: a prompt, command and result).  The idea is to allow
        /// a simple Ctrl+C to copy the previous command (and additional Ctrl-Cs to copy more commands).
        /// </remarks>
        /// </summary>
        new public void Copy()
        {
            if (autoCopy || Selection.IsEmpty)
            {
                if (!autoCopy)
                {
                    autoCopy = true;
                    EditingCommands.SelectUpByParagraph.Execute(null, this);
                    CaretPosition = Selection.Start;
                }
                EditingCommands.SelectUpByParagraph.Execute(null, this);
            }
            //Paragraph p = this.CaretPosition.Paragraph;
            //// would select the prompt.
            //// Selection.Select(p.Inlines.FirstInline.ContentStart,p.Inlines.FirstInline.NextInline.NextInline.ContentEnd)
            //// 
            //Selection.Select(p.Inlines.FirstInline.NextInline.NextInline.NextInline.ContentStart, p.Inlines.LastInline.ContentEnd);

            //return Selection.Text;
            base.Copy();
        }

        #region ConsoleSizeCalculations
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
        }

        /// <summary>
        /// Gets or sets the name of the specified font.
        /// </summary>
        /// <value></value>
        /// <returns>A font family. The default value is the system dialog font.</returns>
        public new FontFamily FontFamily
        {
            get
            {
                return base.FontFamily;
            }
            set
            {
                base.FontFamily = value;
                UpdateCharacterWidth();
                Document.LineHeight = FontSize * FontFamily.LineSpacing;
            }
        }

        /// <summary>
        /// Gets or sets the font size.
        /// </summary>
        /// <value></value>
        /// <returns>A font size. The default value is the system dialog font size. The font size must be a positive number and in the range of the <see cref="P:System.Windows.SystemFonts.MessageFontSize"></see>.</returns>
        public new double FontSize
        {
            get
            {
                return base.FontSize;
            }
            set
            {
                base.FontSize = value;
                Document.LineHeight = FontSize * FontFamily.LineSpacing;
            }
        }

        private double characterWidth = 0.5498046875; // Default to the size for Consolas

        public double CharacterWidthRatio
        {
            get { return characterWidth; }
        }

        /// <summary>
        /// Gets or sets the size of the buffer.
        /// </summary>
        /// <value>The size of the buffer.</value>
        public System.Management.Automation.Host.Size BufferSize
        {
            get
            {
                return new System.Management.Automation.Host.Size(
                    (int)((((ExtentWidth > 0) ? ExtentWidth : RenderSize.Width) - (Padding.Left + Padding.Right))
                        / (FontSize * characterWidth)) - 1,
                    (int)((((ExtentHeight > 0) ? ExtentHeight : RenderSize.Height) - (Padding.Top + Padding.Bottom))
                        / (Double.IsNaN(Document.LineHeight) ? Document.FontSize : Document.LineHeight)));
            }
            set
            {
                this.Width = value.Width * FontSize * characterWidth;
                // our buffer is infinite-ish
                //this.Height = value.Y * Document.LineHeight;
            }
        }

        /// <summary>
        /// Gets or sets the size of the window.
        /// </summary>
        /// <value>The size of the window.</value>
        public System.Management.Automation.Host.Size WindowSize
        {
            get
            {
                return new System.Management.Automation.Host.Size(
                  (int)((((ViewportWidth > 0) ? ViewportWidth : RenderSize.Width) - (Padding.Left + Padding.Right))
                        / (FontSize * characterWidth)) - 1,
                  (int)(((ViewportHeight > 0) ? ViewportHeight : RenderSize.Height)
                        / (Double.IsNaN(Document.LineHeight) ? Document.FontSize : Document.LineHeight)));

            }
            set
            {
                this.Width = value.Width * FontSize * characterWidth;
                this.Height = value.Height * Document.LineHeight;
            }
        }

        /// <summary>
        /// Gets or sets the size of the max window.
        /// </summary>
        /// <value>The size of the max window.</value>
        public System.Management.Automation.Host.Size MaxWindowSize
        {
            get
            {
                return new System.Management.Automation.Host.Size(
                    (int)(System.Windows.SystemParameters.PrimaryScreenWidth - (Padding.Left + Padding.Right)
                            / (FontSize * characterWidth)) - 1,
                    (int)(System.Windows.SystemParameters.PrimaryScreenHeight - (Padding.Top + Padding.Bottom)
                            / (Double.IsNaN(Document.LineHeight) ? Document.FontSize : Document.LineHeight)));

            }
            //set { myMaxWindowSize = value; }
        }

        /// <summary>
        /// Gets or sets the size of the max physical window.
        /// </summary>
        /// <value>The size of the max physical window.</value>
        public System.Management.Automation.Host.Size MaxPhysicalWindowSize
        {
            get { return MaxWindowSize; }
            //set { myMaxPhysicalWindowSize = value; }
        }

        private System.Management.Automation.Host.Coordinates myCursorPosition;

        /// <summary>
        /// Gets or sets the cursor position.
        /// </summary>
        /// <value>The cursor position.</value>
        public System.Management.Automation.Host.Coordinates CursorPosition
        {
            get
            {
                Rect caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward).GetCharacterRect(LogicalDirection.Backward);
                myCursorPosition.X = (int)caret.Left;
                myCursorPosition.Y = (int)caret.Top;
                return myCursorPosition;
            }
            set
            {
                CaretPosition = GetPositionFromPoint(new Point(value.X * FontSize * characterWidth, value.Y * Document.LineHeight), true);
            }
        }


        /// <summary>
        /// Updates the value of the CharacterWidthRatio
        /// <remarks>
        /// Called each time the font-family changes
        /// </remarks>
        /// </summary>
        private void UpdateCharacterWidth()
        {
            // Calculate the font width (as a percentage of it's height)            
            foreach (Typeface tf in FontFamily.GetTypefaces())
            {
                if (tf.Weight == FontWeights.Normal && tf.Style == FontStyles.Normal)
                {
                    GlyphTypeface glyph;// = new GlyphTypeface();
                    if (tf.TryGetGlyphTypeface(out glyph))
                    {
                        // if this is really a fixed width font, then the widths should be equal:
                        // glyph.AdvanceWidths[glyph.CharacterToGlyphMap[(int)'M']]
                        // glyph.AdvanceWidths[glyph.CharacterToGlyphMap[(int)'i']]
                        characterWidth = glyph.AdvanceWidths[glyph.CharacterToGlyphMap[(int)'M']];
                        break;
                    }
                }
            }
        }
        #endregion ConsoleSizeCalculations

        //public static readonly RoutedEvent TitleChangedEvent = EventManager.RegisterRoutedEvent("TitleChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(RichTextConsole));

        //public event RoutedEventHandler TitleChanged
        //{
        //    add
        //    {
        //        AddHandler(TitleChangedEvent, value);
        //    }
        //    remove
        //    {
        //        RemoveHandler(TitleChangedEvent, value);
        //    }
        //}

        public static DependencyProperty TitleProperty = DependencyProperty.RegisterAttached("Title", typeof(string), typeof(RichTextConsole));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }


        /// <summary>
        /// Gets the viewport position (in lines/chars)
        /// </summary>
        /// <returns></returns>
        public Coordinates WindowPosition
        {
            get
            {
                int x, y = 0, lines = -1;
                TextPointer origin = GetPositionFromPoint(new Point(0, 0), true).GetInsertionPosition(LogicalDirection.Forward);
                TextPointer c = origin.GetLineStartPosition(0).GetNextInsertionPosition(LogicalDirection.Forward);
                x = c.GetOffsetToPosition(origin);
                origin = origin.GetLineStartPosition(1);

                while (lines < 0)
                {
                    c = c.GetLineStartPosition(-10, out lines); y -= lines;
                }
                return new Coordinates(x, y);
            }
            set
            {
                //// (value.X * FontSize * characterWidth, value.Y * Document.LineHeight)
                //TextPointer lineStart = CaretPosition.DocumentStart.GetInsertionPosition(LogicalDirection.Forward).GetLineStartPosition(value.Y, out lines).GetInsertionPosition(LogicalDirection.Forward);
                //while (lines < value.Y)
                //{
                //    for (; lines < value.Y; lines++)
                //    {
                //        CaretPosition.DocumentEnd.InsertLineBreak();
                //    }
                //    lineStart = CaretPosition.DocumentStart.GetInsertionPosition(LogicalDirection.Forward).GetLineStartPosition(value.Y, out lines).GetInsertionPosition(LogicalDirection.Forward);
                //}

                //TextPointer nextLine = lineStart.GetLineStartPosition(1);
                //TextPointer site = lineStart.GetPositionAtOffset(value.X);
                //if (site.GetOffsetToPosition(nextLine) <= 0)
                //{
                //    site = lineStart;
                //}

                ScrollToVerticalOffset(value.Y * (Double.IsNaN(Document.LineHeight) ? Document.FontSize : Document.LineHeight));
                ScrollToHorizontalOffset(value.X * FontSize * characterWidth);
            }
        }

        public BufferCell GetCell(TextPointer position, LogicalDirection direction)
        {
            TextRange range = null;
            try
            {
                range = new TextRange(position, position.GetPositionAtOffset(1, direction));

                return new BufferCell(
                        (range.IsEmpty || range.Text[0] == '\n') ? ' ' : range.Text[0],
                        ConsoleColorFromBrush((Brush)range.GetPropertyValue(TextElement.ForegroundProperty)),
                        ConsoleColorFromBrush((Brush)range.GetPropertyValue(TextElement.BackgroundProperty)),
                        BufferCellType.Complete);
            }
            catch
            {
                return new BufferCell();
            }
        }

        public void SetCell(BufferCell cell, TextPointer position, LogicalDirection direction)
        {
            TextRange range = null;

            TextPointer positionPlus = position.GetNextInsertionPosition(LogicalDirection.Forward);
            if (positionPlus != null)
            {
                range = new TextRange(position, positionPlus);
            }

            if (null == range || range.IsEmpty)
            {
                position = position.GetInsertionPosition(LogicalDirection.Forward);
                if (position != null)
                {
                    Run r = position.GetAdjacentElement(LogicalDirection.Forward) as Run;

                    if (null != r)
                    {
                        if (r.Text.Length > 0)
                        {
                            char[] chr = r.Text.ToCharArray();
                            chr[0] = cell.Character;
                            r.Text = chr.ToString();
                        }
                        else
                        {
                            r.Text = cell.Character.ToString();
                        }
                    }
                    else
                    {
                        r = position.GetAdjacentElement(LogicalDirection.Backward) as Run;
                        if (null != r
                            && r.Background == BrushFromConsoleColor(cell.BackgroundColor)
                            && r.Foreground == BrushFromConsoleColor(cell.ForegroundColor)
                        )
                        {
                            if (r.Text.Length > 0)
                            {
                                r.Text = r.Text + cell.Character;
                            }
                            else
                            {
                                r.Text = cell.Character.ToString();
                            }
                        }
                        else
                        {
                            r = new Run(cell.Character.ToString(), position);
                        }
                    }
                    r.Background = BrushFromConsoleColor(cell.BackgroundColor);
                    r.Foreground = BrushFromConsoleColor(cell.ForegroundColor);
                    //position = r.ElementStart;
                }

            }
            else
            {

                range.Text = cell.Character.ToString();
                range.ApplyPropertyValue(TextElement.BackgroundProperty, BrushFromConsoleColor(cell.BackgroundColor));
                range.ApplyPropertyValue(TextElement.ForegroundProperty, BrushFromConsoleColor(cell.ForegroundColor));
            }
        }

        public BufferCell[,] GetRectangle(Rectangle rectangle)
        {
            TextPointer origin, lineStart, cell;
            int lines = 0, x = 0, y = 0, width = rectangle.Right - rectangle.Left, height = rectangle.Bottom - rectangle.Top;
            BufferCell[,] buffer = new BufferCell[rectangle.Bottom - rectangle.Top, rectangle.Right - rectangle.Left];
            BufferCell blank = new BufferCell(' ', ConsoleColorFromBrush(Foreground), ConsoleColorFromBrush(Background), BufferCellType.Complete);

            lineStart = CaretPosition.DocumentStart.GetInsertionPosition(LogicalDirection.Forward).GetLineStartPosition(rectangle.Top, out lines).GetInsertionPosition(LogicalDirection.Forward);
            if (lines < rectangle.Top)
            {
                //throw new ArgumentOutOfRangeException("rectangle", rectangle, "The .Top of the boundary rectangle is larger than the available buffer");
                for (; y < height; y++)
                {
                    for (; x < width; x++)
                    {
                        buffer[y, x] = blank;
                    }
                }
                return buffer;
            }

            cell = origin = lineStart.GetPositionAtOffset(rectangle.Left);
            lineStart = origin.GetLineStartPosition(1, out lines).GetInsertionPosition(LogicalDirection.Forward);

            while (y < height)
            {
                while (x < width && cell.GetOffsetToPosition(lineStart) > 0)
                {
                    buffer[y, x] = GetCell(cell, LogicalDirection.Forward);
                    cell = cell.GetPositionAtOffset(1, LogicalDirection.Forward);
                    x++;
                }
                // just in case we ran out of line before we ran out of text...
                while (x < width)
                {
                    buffer[y, x] = blank;
                    x++;
                }
                // try advancing one line ...
                cell = lineStart.GetPositionAtOffset(rectangle.Left - 1);
                lineStart = lineStart.GetLineStartPosition(1, out lines).GetInsertionPosition(LogicalDirection.Forward);
                y++;
                x = 0;

                // if we skipped lines, fill them in...
                if (lines > 1)
                {
                    for (int i = 1; i < lines; i++)
                    {
                        while (x < width)
                        {
                            buffer[y, x] = blank;
                            x++;
                        }
                        y++;
                        x = 0;
                    }
                }
                #region fill with blanks
                else if (lines == 0)
                {
                    // we've reached the end, just fill the rest with blanks.
                    while (y < height)
                    {
                        while (x < width)
                        {
                            buffer[y, x] = blank;
                            x++;
                        }
                        y++;
                        x = 0;
                    }
                }
                #endregion
            }
            return buffer;
        }

        public void SetBufferContents(Rectangle rectangle, BufferCell fill)
        {
            if (rectangle.Left == -1 && rectangle.Right == -1)
            {
                ClearScreen();
            }
            else
            {

                TextPointer lineStart, lineEnd, cell, rowEnd, originCell;
                TextRange row;
                int lines = 0, y = 0, diff;
                int height = rectangle.Bottom - rectangle.Top;
                int width = rectangle.Right - rectangle.Left;

                string fillString = new string(fill.Character, width);

                lineStart = CaretPosition.DocumentStart.GetInsertionPosition(LogicalDirection.Forward).GetLineStartPosition(rectangle.Top, out lines).GetInsertionPosition(LogicalDirection.Forward);
                while (lines < rectangle.Top)
                {
                    for (; lines < rectangle.Top; lines++)
                    {
                        CaretPosition.DocumentEnd.InsertLineBreak();
                    }
                    lineStart = CaretPosition.DocumentStart.GetInsertionPosition(LogicalDirection.Forward).GetLineStartPosition(rectangle.Top, out lines).GetInsertionPosition(LogicalDirection.Forward);
                }

                cell = originCell = lineStart.GetPositionAtOffset(rectangle.Left);
                lineStart = originCell.GetLineStartPosition(1, out lines).GetInsertionPosition(LogicalDirection.Forward);
                lineEnd = originCell.GetLineStartPosition(1, out lines).GetNextInsertionPosition(LogicalDirection.Backward);

                if (lines != 1)
                {
                    lineStart.Paragraph.ElementEnd.InsertLineBreak();
                }
                BeginChange();
                while (y < height)
                {
                    diff = 0;
                    rowEnd = cell.GetPositionAtOffset(width);
                    #region clear existing text
                    if (null != rowEnd && null != lineEnd)
                    {
                        diff = rowEnd.GetOffsetToPosition(lineEnd);
                    }
                    if (diff > 0 || lineEnd == null)
                    {
                        row = new TextRange(cell, rowEnd);
                    }
                    else
                    {
                        row = new TextRange(cell, lineEnd);
                    }
                    row.Text = String.Empty;
                    #endregion clear existing text

                    // insert new text
                    Run r = new Run(fillString, cell);
                    r.Background = BrushFromConsoleColor(fill.BackgroundColor);
                    r.Foreground = BrushFromConsoleColor(fill.ForegroundColor);

                    // try advancing one line ...
                    cell = lineStart.GetPositionAtOffset(rectangle.Left - 1);
                    lineEnd = lineStart.GetLineStartPosition(1, out lines).GetNextInsertionPosition(LogicalDirection.Backward);
                    lineStart = lineStart.GetLineStartPosition(1, out lines).GetInsertionPosition(LogicalDirection.Forward);

                    if (lines != 1)
                    {
                        lineStart.Paragraph.ElementEnd.InsertLineBreak();
                    }
                    y++;
                }
                EndChange();
            }
        }

        public void SetBufferContents(Coordinates origin, BufferCell[,] contents)
        {
            TextPointer lineStart, lineEnd, cell, rowEnd, originCell;
            TextRange row;
            Run r;
            Brush back, fore;
            BufferCell bc;

            int lines = 0, x = 0, y = 0, diff;
            int height = contents.GetLength(0);
            int width = contents.GetLength(1);

            lineStart = CaretPosition.DocumentStart.GetInsertionPosition(LogicalDirection.Forward).GetLineStartPosition(origin.Y, out lines);
            while (lines < origin.Y)
            {
                for (; lines < origin.Y; lines++)
                {
                    CaretPosition.DocumentEnd.InsertLineBreak();
                }
                lineStart = CaretPosition.DocumentStart.GetInsertionPosition(LogicalDirection.Forward).GetLineStartPosition(origin.Y, out lines);
            }

            if (origin.X > 0)
            {
                cell = originCell = lineStart.GetPositionAtOffset(origin.X).GetInsertionPosition(LogicalDirection.Forward);
            }
            else
            {
                cell = originCell = lineStart.GetInsertionPosition(LogicalDirection.Forward);
            }
            lineStart = originCell.GetLineStartPosition(1, out lines);
            lineEnd = lineStart.GetNextInsertionPosition(LogicalDirection.Backward);

            if (lines != 1)
            {
                lineStart.Paragraph.ElementEnd.InsertLineBreak();
            }
            BeginChange();
            while (y < height)
            {
                diff = 0;
                rowEnd = cell.GetPositionAtOffset(width);
                #region clear existing text
                if (null != rowEnd && null != lineEnd)
                {
                    diff = rowEnd.GetOffsetToPosition(lineEnd);
                }

                if (diff > 0 || lineEnd == null)
                {
                    row = new TextRange(cell, rowEnd);
                }
                else
                {
                    row = new TextRange(cell, lineStart);
                }
                row.Text = String.Empty;
                #endregion clear existing text

                // insert new text
                r = new Run(String.Empty, row.Start);

                r.Background = Background;
                r.Foreground = Foreground;
                for (x = 0; x < width; ++x)
                {
                    bc = contents[y, x];
                    back = BrushFromConsoleColor(bc.BackgroundColor);
                    fore = BrushFromConsoleColor(bc.ForegroundColor);

                    if (r.Background != back || r.Foreground != fore)
                    {
                        r = new Run(bc.Character.ToString(), r.ElementEnd);
                        r.Background = back;
                        r.Foreground = fore;
                    }
                    else
                    {
                        r.Text += bc.Character;
                    }
                }
                if (diff <= 0 && lineEnd != null)
                {
                    row.End.InsertLineBreak();
                    lineStart = row.Start.GetLineStartPosition(1);
                }

                // try advancing one line ...
                if (origin.X > 0)
                {
                    cell = lineStart.GetPositionAtOffset(origin.X).GetInsertionPosition(LogicalDirection.Forward);
                }
                else
                {
                    cell = lineStart.GetInsertionPosition(LogicalDirection.Forward);
                }
                lineStart = lineStart.GetLineStartPosition(1, out lines);
                if (lines != 1)
                {
                    lineStart.InsertLineBreak();
                    lineStart = lineStart.GetLineStartPosition(1, out lines);
                }
                lineEnd = lineStart.GetNextInsertionPosition(LogicalDirection.Backward);

                y++;
                x = 0;
            }
            EndChange();
        }
    }
}