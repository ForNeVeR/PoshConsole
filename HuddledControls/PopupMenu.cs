﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management.Automation.Host;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Huddled.Wpf.Controls.Properties;
using Huddled.Wpf.Controls.Utility;
using PoshConsole.PowerShell.Utilities;

namespace Huddled.Wpf.Controls
{
	public class PopupMenu : Popup
	{
		private IPoshConsoleControl _console;
		private int _intelliNum = -1;
		private ListBox _intellisense = new ListBox();
		private static Regex _number = new Regex(@"[0-9]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

		private static Regex _tabseparator = new Regex(@"[.;,=\\ |/[\]()""']",
			RegexOptions.CultureInvariant | RegexOptions.Compiled);

		private string _terminalString = string.Empty;

		/// <summary>
		/// A task for rilling the completion list.
		/// </summary>
		private Task _listLoadingTask;

		/// <summary>
		/// A cancellation token source for cancelling the loading task.
		/// </summary>
		private CancellationTokenSource _listLoadingCancellation = new CancellationTokenSource();

      public PopupMenu(IPoshConsoleControl console)
      {
         Closed += ClosedTabComplete;
         Closed += ClosedHistory;

         StaysOpen = false;
         _intellisense.SelectionMode = SelectionMode.Single;
         _intellisense.SelectionChanged += IntellisenseSelectionChanged;
         _intellisense.IsTextSearchEnabled = true;
         //_intellisense.PreviewTextInput   += new TextCompositionEventHandler(popup_TextInput);

         _console = console;

         Child = _intellisense;
         PlacementTarget = _console.CommandBox;

      }


      /// <summary>
      /// Responds when the value of the <see cref="P:System.Windows.Controls.Primitives.Popup.IsOpen"/> property changes from to true to false.
      /// </summary>
      /// <param name="e">The event data.</param>
      protected override void OnClosed(EventArgs e)
      {
         base.OnClosed(e);
         _intelliNum = -1;
         _terminalString = string.Empty;
         _intellisense.Items.Filter = null;
         _tabbing = null;
         _console.CommandBox.Focus();
      }

      /// <summary>
      /// Invoked when an unhandled <see cref="E:System.Windows.Input.Keyboard.PreviewKeyDown"/> attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
      /// </summary>
      /// <param name="e">The <see cref="T:System.Windows.Input.KeyEventArgs"/> that contains the event data.</param>
      protected override void OnPreviewKeyDown(KeyEventArgs e)
      {
         Trace.TraceInformation("Entering popup_PreviewKeyDown:");
         Trace.Indent();
         //Trace.WriteLine("Event:  {0}" + e.RoutedEvent);
         Trace.WriteLine("Key:    " + e.Key);
         Trace.WriteLine("Index:  " + _intellisense.SelectedIndex);
         Trace.WriteLine("Count:  " + _intellisense.Items.Count);
         //Trace.WriteLine("Source: {0}" + e.Source);

         _terminalString = string.Empty;

         switch (e.Key)
         {
            case Key.Up:
               {
                 MovePrevious();
                  e.Handled = true;
                 Trace.WriteLine("Key.Up: " + _intellisense.SelectedIndex);
               } break;
            case Key.Down:
               {
                  MoveNext();
                  e.Handled = true;
                  Trace.WriteLine("Key.Dn: " + _intellisense.SelectedIndex);
               } break;
            case Key.Space:
               {
                  _terminalString = " ";
                  e.Handled = true;
                  IsOpen = false;
               } break;
            case Key.Back:
               {
                  // BUGBUG: if you tab back past where TAB was pressed.
                  //         this doesn't actually expand the _intellsense!
                  //  WORSE: it doesn't update the text on the fly.
               
                  // Update the filter
                  _tabbing = (_tabbing.Length == 0) ? "" : _tabbing.Substring(0, _tabbing.Length - 1);
                  _lastWord = (_lastWord.Length == 0) ? "" : _lastWord.Substring(0, _lastWord.Length - 1);

                  _intellisense.Items.Filter = new Predicate<object>(TypeAheadFilter);
                  _intellisense.SelectedIndex = 0;
                  e.Handled = true;
               } break;
            case Key.Delete: goto case Key.Escape;
            case Key.Escape:
               {
                  _intellisense.SelectedIndex = -1;
                  e.Handled = false;
                  IsOpen = false;
               } break;
            case Key.Tab:
               {
                  if (e.IsModifierOn(ModifierKeys.Shift))
                  {
                     MovePrevious();
                  }
                  else
                  {
                     MoveNext();
                  }
                  e.Handled = true;
               } break;
            case Key.Enter:
               {
                  e.Handled = true;
                  IsOpen = false;
               } break;
         }
         Trace.Unindent();
         Trace.TraceInformation("Exiting popup_PreviewKeyDown");
         base.OnPreviewKeyDown(e);
      }

      internal void MoveNext()
      {
         if (_intellisense.SelectedIndex == _intellisense.Items.Count - 1)
         {
            _intellisense.SelectedIndex = 0;
         } else {
            _intellisense.SelectedIndex++;
         }
         _intellisense.ScrollIntoView(_intellisense.SelectedItem);
         ConsoleControl.TabExpansionTrace.TraceEvent(TraceEventType.Information, 3, "Selection Changed (MoveNext): {0}", _intellisense.SelectedItem);
      }

      internal void MovePrevious()
      {
         if(_intellisense.SelectedIndex == 0)
         {
            // (_intellisense.ItemContainerGenerator.ContainerFromIndex(_intellisense.Items.Count - 1) as ListBoxItem).IsSelected = true;
            _intellisense.SelectedIndex = _intellisense.Items.Count - 1;
         } else {
            _intellisense.SelectedIndex--;
         }
         _intellisense.ScrollIntoView(_intellisense.SelectedItem);
         ConsoleControl.TabExpansionTrace.TraceEvent(TraceEventType.Information, 3, "Selection Changed (MovePrevious): {0}", _intellisense.SelectedItem);
      }

      /// <summary>
      /// Handles the TextInput event of the popup control 
      /// 1) to save the key if it's one we consider to toggle the tab-complete
      /// 2) to handle typing numbers for the history menu
      /// </summary>
      protected override void OnPreviewTextInput(TextCompositionEventArgs e)
      {
         if (_tabseparator.IsMatch(e.Text))
         {
            _terminalString = e.Text;
            IsOpen = false;
         }
         else if (_number.IsMatch(e.Text))
         {
            if (_intelliNum >= 0)
            {
               _intelliNum *= 10;
            }
            else _intelliNum = 0;

            _intelliNum += int.Parse(e.Text);
            if (_intelliNum > 0 && _intelliNum < _intellisense.Items.Count - 1)
            {
               _intellisense.SelectedIndex = _intelliNum;
            }
         }
         else if (_tabbing != null)
         {
            // Update the filter
            _tabbing += e.Text;
            _lastWord += e.Text;
            _intellisense.Items.Filter = new Predicate<object>(TypeAheadFilter);

            _intellisense.SelectedIndex = 0;
            if (_intellisense.Items.Count <= 1) IsOpen = false;
            //intellisense.Items.Refresh();
            // intellisense.Items.Count  //tabbingCount
         }
         ConsoleControl.TabExpansionTrace.TraceEvent(TraceEventType.Information, 3, "Selection Changed (Typing): {0}", _intellisense.SelectedItem);
         base.OnPreviewTextInput(e);
         Focus();
         _intellisense.Focus();
      }

      #region Closing Handlers

      /// <summary>
      /// Handles the Closed event of the History popup menu.
      /// </summary>
      /// <param name="sender">The source of the event.</param>
      /// <param name="ea">The <see cref="System.EventArgs"/> instance containing the event data.</param>
      void ClosedHistory(object sender, EventArgs ea)
      {
         if (_tabbing == null && IsParentActive())
         {
            if (_intellisense.SelectedValue != null)
            {
               _console.CurrentCommand = TextSearch.GetText((ListBoxItem)_intellisense.SelectedValue);
            }
         }
      }

      /// <summary>
      /// Handles the Closed event of the TabComplete popup menu.
      /// </summary>
      /// <param name="sender">The source of the event.</param>
      /// <param name="ea">The <see cref="System.EventArgs"/> instance containing the event data.</param>
      void ClosedTabComplete(object sender, EventArgs ea)
      {
         if (_tabbing == null || !IsParentActive()) return;

         var cmd = _console.CurrentCommand;
         if (_intellisense.SelectedValue != null)
         {
            ConsoleControl.TabExpansionTrace.TraceEvent(TraceEventType.Information, 4, "Popup Closed: {0}", _intellisense.SelectedValue);
            _console.CurrentCommand = cmd.Substring(0, cmd.TrimEnd('\r','\n').Length - cmd.GetLastWord(false).Length) + _intellisense.SelectedValue + _terminalString;
         }
         else
         {
            _console.CurrentCommand = _tabbing;
         }
         _console.CommandBox.Focus();
      }

      private bool IsParentActive()
      {
         var active = false;
         var console = _console as DependencyObject;
         if(console != null)
         {
            var window = console.TryFindParent<Window>();
            if(window != null)
            {
               active = window.IsActive;
            }
         }
         return active;
      }

      #endregion

		/// <summary>
		/// Show the popup.
		/// </summary>
		/// <param name="placementRectangle">The placement rectangle.</param>
		/// <param name="completions">The emumeration of items to show.</param>
		/// <param name="enableNumbering">If set to <c>true</c> then enables numbering.</param>
		/// <param name="distinct">If set to <c>true</c> then filters duplicates in the list.</param>
		private void ShowPopup(Rect placementRectangle, IEnumerable<string> completions, bool enableNumbering,
			bool distinct)
		{
			if (_listLoadingTask != null)
			{
				_listLoadingCancellation.Cancel();
			}
			_intellisense.Items.Clear();

			_intellisense.Items.Filter = null;
			
			_listLoadingCancellation = new CancellationTokenSource();
			_listLoadingTask = Task.Factory.StartNew(() =>
				{
					var values = completions;
					if (distinct)
					{
						values = values.Distinct();
					}

					int index = 0;
					foreach (var value in values)
					{
						if (_listLoadingCancellation.IsCancellationRequested)
						{
							break;
						}

						var completion = value;
						int number = ++index;
						Dispatcher.Invoke((Action) (() =>
							{
								var item = new ListBoxItem();
								TextSearch.SetText(item, completion);

								// NOTE: A name must start with a letter or the underscore character (_), and must
								// contain only letters, digits, or underscores.

								item.Content = enableNumbering
									? String.Format("{0,2} {1}", number, completion)
									: completion;

								_intellisense.Items.Add(item);
							}));
					}
				}, _listLoadingCancellation.Token);

			_intellisense.Visibility = Visibility.Visible;
			_intellisense.SelectedIndex = 0;
			_intellisense.ScrollIntoView(_intellisense.SelectedItem);

			PlacementRectangle = placementRectangle;
			Placement = PlacementMode.RelativePoint;

			IsOpen = true; // show the popup
			Focus();
			_intellisense.Focus(); // focus the keyboard on the popup
		}

      /// <summary>
      /// Types the ahead filter.
      /// </summary>
      /// <param name="item">The item.</param>
      /// <returns></returns>
      private bool TypeAheadFilter(object item)
      {
         return (item.ToString()).ToLower().StartsWith(_lastWord.ToLower());
      }

		internal void ShowHistoryPopup(Rect placementRectangle, IEnumerable<string> completions)
		{
			ShowPopup(placementRectangle, completions, true, Settings.Default.HistoryMenuFilterDupes);
		}

		/// <summary>
		/// Shows the tab-expansion popup.
		/// </summary>
		/// <param name="placementRectangle">The position to show the popup in.</param>
		/// <param name="completions">The list of options</param>
		/// <param name="currentCommand">The current command</param>
		internal void ShowTabPopup(Rect placementRectangle, IEnumerable<string> completions, string currentCommand)
		{
			completions = completions.Distinct()
				.ToList();

			_tabbing = currentCommand.TrimEnd('\r', '\n');
			_lastWord = _tabbing.GetLastWord();

			_intellisense.Items.Filter = TypeAheadFilter;

			ShowPopup(placementRectangle, completions, false, false);
		}

      private string _lastWord, _tabbing;

      #region Handle Clicks on the Intellisense popup.
      private bool _popupClicked;
      /// <summary>
      /// Intellisenses the selection changed.
      /// </summary>
      /// <param name="sender">The sender.</param>
      /// <param name="e">The <see cref="System.Windows.Controls.SelectionChangedEventArgs"/> instance containing the event data.</param>
      private void IntellisenseSelectionChanged(object sender, SelectionChangedEventArgs e)
      {
         // if they clicked, then when the selection changes we close.
         if (_popupClicked)
         {
            IsOpen = false;
            _popupClicked = false;
         }
      }

      /// <summary>
      /// Invoked when an unhandled <see cref="E:System.Windows.Input.Mouse.MouseDown"/> attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
      /// </summary>
      /// <param name="e">The <see cref="T:System.Windows.Input.MouseButtonEventArgs"/> that contains the event data. This event data reports details about the mouse button that was pressed and the handled state.</param>
      protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
      {
         _popupClicked = true;
         base.OnPreviewMouseDown(e);
      }

      /// <summary>
      /// Invoked when an unhandled <see cref="E:System.Windows.Input.Mouse.PreviewMouseWheel"/> attached event reaches an element in its route that is derived from this class. Implement this method to add class handling for this event.
      /// </summary>
      /// <param name="e">The <see cref="T:System.Windows.Input.MouseWheelEventArgs"/> that contains the event data.</param>
      protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
      {
         ConsoleControl.TabExpansionTrace.TraceEvent(TraceEventType.Information,-1,"Mouse Wheel: {0}", e.Delta);
         if(e.Delta > 0 )
         {
            MovePrevious();
         } 
         else
         {
            MoveNext();
         }
         e.Handled = true;
         base.OnPreviewMouseWheel(e);
      }
      #endregion

   }
}