// Copyright (c) 2008-2011 Joel Bennett http://HuddledMasses.org/
//
// Permission is hereby granted, free of charge, to any person obtaining a copy 
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights 
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the Software is 
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.
// *****************************************************************************
// NOTE: YOU MAY *ALSO* DISTRIBUTE THIS FILE UNDER ANY OF THE FOLLOWING...
// PERMISSIVE LICENSES:
// BSD:	 http://www.opensource.org/licenses/bsd-license.php
// MIT:   http://www.opensource.org/licenses/mit-license.html
// Ms-PL: http://www.opensource.org/licenses/ms-pl.html
// RECIPROCAL LICENSES:
// Ms-RL: http://www.opensource.org/licenses/ms-rl.html
// GPL 2: http://www.gnu.org/copyleft/gpl.html
// *****************************************************************************
// LASTLY: THIS IS NOT LICENSED UNDER GPL v3 (although the above are compatible)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Huddled.Interop;
using Huddled.Interop.Windows;
using MessageMapping = System.Collections.Generic.KeyValuePair<Huddled.Interop.NativeMethods.WindowMessage, Huddled.Interop.NativeMethods.MessageHandler>;

namespace Huddled.Wpf {
   public class SnapToBehavior : NativeBehavior {
      public class AdvancedWindowStateChangedArgs : EventArgs {
         public AdvancedWindowStateChangedArgs(AdvancedWindowState windowState) {
            WindowState = windowState;
         }
         public AdvancedWindowState WindowState { get; set; }
      }


      private Size _normalSize;
      protected override void OnWindowSourceInitialized() {
         _normalSize = new Size(AssociatedObject.ActualWidth, AssociatedObject.ActualHeight);
         AssociatedObject.Deactivated += (s, e) => {
            Trace.WriteLine("Window Deactivated. VisualState: Inactive");
            VisualStateManager.GoToState(AssociatedObject, "Inactive", true);
         };
         AssociatedObject.Activated += (s, e) => {
            Trace.WriteLine("Window Activated. VisualState: Active");
            VisualStateManager.GoToState(AssociatedObject, "Active", true);
         };

         base.OnWindowSourceInitialized();

      }

      bool _undocking = false;
      private IntPtr OnEnterSizeMove(IntPtr wParam, IntPtr lParam, ref bool handled) {
         _undocking = DockedState != DockingEdge.None;
         return IntPtr.Zero;
      }


      /// <summary>
      /// We handle the WindowPositionChanged Message only for the purpose of updating the OnWindowStateChanged message
      /// </summary>
      /// <param name="wParam">The wParam.</param>
      /// <param name="lParam">The lParam.</param>
      /// <param name="handled">Whether or not this message has been handled ... (we don't change it)</param>
      /// <returns>IntPtr.Zero</returns>  
      private IntPtr OnPositionChange(IntPtr wParam, IntPtr lParam, ref bool handled) {
         var windowPosition = (NativeMethods.WindowPosition)Marshal.PtrToStructure(lParam, typeof(NativeMethods.WindowPosition));

         if ((windowPosition.Flags & NativeMethods.WindowPositionFlags.NoMove) == 0) {
            // If we use the WPF SystemParameters, these should be "Logical" pixels
            var source = PresentationSource.FromVisual(AssociatedObject);
            // MUST use the position from the lParam, NOT the current position of the AssociatedObject
            Rect validArea = windowPosition.GetLocalWorkAreaRect().DPITransformFromWindow(source);

            // Enforce bottom boundary
            bool bottom = windowPosition.Bottom == validArea.Bottom;
            bool right = windowPosition.Right == validArea.Right;
            bool top = windowPosition.Top == validArea.Top;
            bool left = windowPosition.Left == validArea.Left;


            if (DockedState != DockingEdge.None
               && !(DockedState == DockingEdge.Top && top)
               && !(DockedState == DockingEdge.Bottom && bottom)
               && !(DockedState == DockingEdge.Left && left)
               && !(DockedState == DockingEdge.Right && right)
               ) {
               OnWindowStateChanged(AdvancedWindowState.Normal);
            }
            else if (bottom && top) {
               if (left && right) {
                  OnWindowStateChanged(AdvancedWindowState.Maximized);
               }
               else if (left) {
                  OnWindowStateChanged(AdvancedWindowState.DockedLeft);
               }
               else if (right) {
                  OnWindowStateChanged(AdvancedWindowState.DockedRight);
               }
               else {
                  OnWindowStateChanged(AdvancedWindowState.DockedHeight);
               }

            }
            else if (bottom) {
               if (left && right) {
                  OnWindowStateChanged(AdvancedWindowState.DockedBottom);
               }
               else if (left) {
                  OnWindowStateChanged(AdvancedWindowState.SnapBottomLeft);
               }
               else if (right) {
                  OnWindowStateChanged(AdvancedWindowState.SnapBottomRight);
               }
               else {
                  OnWindowStateChanged(AdvancedWindowState.SnapBottom);
               }
            }
            else if (top) {
               if (left && right) {
                  OnWindowStateChanged(AdvancedWindowState.DockedTop);
               }
               else if (left) {
                  OnWindowStateChanged(AdvancedWindowState.SnapTopLeft);
               }
               else if (right) {
                  OnWindowStateChanged(AdvancedWindowState.SnapTopRight);
               }
               else {
                  OnWindowStateChanged(AdvancedWindowState.SnapTop);
               }
            }
            else if (left && right) {
               OnWindowStateChanged(AdvancedWindowState.DockedWidth);
            }
            else if (right) {
               OnWindowStateChanged(AdvancedWindowState.SnapRight);
            }
            else if (left) {
               OnWindowStateChanged(AdvancedWindowState.SnapLeft);
            }
            else {
               OnWindowStateChanged(AdvancedWindowState.Normal);
            }
         }
         return IntPtr.Zero;
      }

      /// <summary>Handles the WindowPositionChanging Window Message.
      /// </summary>
      /// <param name="wParam">The wParam.</param>
      /// <param name="lParam">The lParam.</param>
      /// <param name="handled">Whether or not this message has been handled ... (we don't change it)</param>
      /// <returns>IntPtr.Zero</returns>      
      private IntPtr OnPreviewPositionChange(IntPtr wParam, IntPtr lParam, ref bool handled) {
         var windowPosition = (NativeMethods.WindowPosition)Marshal.PtrToStructure(lParam, typeof(NativeMethods.WindowPosition));
         //if (_removeMargin)
         //{
         //   windowPosition.RemoveBorder(AssociatedObject.Margin);
         //}

         bool top = false, bottom = false, right = false, left = false;

         if (!windowPosition.Flags.HasFlag(NativeMethods.WindowPositionFlags.NoMove)) {
            // If we use the WPF SystemParameters, these should be "Logical" pixels
            var source = PresentationSource.FromVisual(AssociatedObject);
            // MUST use the position from the lParam, NOT the current position of the AssociatedObject
            Rect validArea = windowPosition.GetLocalWorkAreaRect().DPITransformFromWindow(source);

            var innerBorder = new Rect(validArea.Left + SnapDistance.Left,
                                          validArea.Top + SnapDistance.Top,
                                          validArea.Width - (SnapDistance.Left + SnapDistance.Right),
                                          validArea.Height - (SnapDistance.Top + SnapDistance.Bottom));

            var outerBorder = new Rect(validArea.Left - SnapDistance.Left,
                                 validArea.Top - SnapDistance.Top,
                                 validArea.Width + (SnapDistance.Left + SnapDistance.Right),
                                 validArea.Height + (SnapDistance.Top + SnapDistance.Bottom));

            if (_undocking) {
               Debug.WriteLine(string.Format("UnDocking: {0}x{1} (WxH)", _normalSize.Width, _normalSize.Height));
               //if (windowPosition.Width == validArea.Width ) {
               //   windowPosition.Width = (int)_normalSize.Width;
               //}
               //if (windowPosition.Height == validArea.Height) {
               //   windowPosition.Height = (int)_normalSize.Height;
               //}
               DockedState = DockingEdge.None;
            }

            // Trace.WriteLine("Preview PositionChange: " + windowPosition.ToString());

            // Calculate if we're within snapping distance of any edge
            bottom = windowPosition.Bottom > innerBorder.Bottom && windowPosition.Bottom <= outerBorder.Bottom;
            right = windowPosition.Right > innerBorder.Right && windowPosition.Right <= outerBorder.Right;
            top = windowPosition.Top < innerBorder.Y && windowPosition.Top >= outerBorder.Top;
            left = windowPosition.Left < innerBorder.Left && windowPosition.Left >= outerBorder.Left;



            if (bottom && top) {
               windowPosition.Top = (int)(validArea.Top);
               windowPosition.Height = (int)validArea.Height;
               if (left && right) {
                  windowPosition.Left = (int)(validArea.Left);
                  windowPosition.Width = (int)validArea.Width;
                  OnPreviewWindowStateChanged(AdvancedWindowState.Maximized);
               }
               else if (left) {
                  windowPosition.Left = (int)(validArea.Left);
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedLeft);
               }
               else if (right) {
                  windowPosition.Left = (int)(validArea.Right - windowPosition.Width);
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedRight);
               }
               else {
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedHeight);
               }

            }
            else if (bottom) {
               windowPosition.Top = (int)(validArea.Bottom - windowPosition.Height);
               if (left && right) {
                  windowPosition.Left = (int)(validArea.Left);
                  windowPosition.Width = (int)validArea.Width;
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedBottom);
               }
               else if (DockAgainst.HasFlag(DockingEdge.Bottom) && DockedState == DockingEdge.None || DockedState == DockingEdge.Bottom) {
                  if (DockedState == DockingEdge.None) {
                     if (!_undocking) {
                        _normalSize = new Size(windowPosition.Width, windowPosition.Height);
                        Debug.WriteLine(string.Format("Docking To Bottom: {0}x{1} (WxH)", _normalSize.Width, _normalSize.Height));
                     }
                     DockedState = DockingEdge.Bottom;
                  }
                  windowPosition.Left = (int)(validArea.Left);
                  windowPosition.Width = (int)validArea.Width;
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedBottom);
               }
               else if (left) {
                  windowPosition.Left = (int)(validArea.Left);
                  OnPreviewWindowStateChanged(AdvancedWindowState.SnapBottomLeft);
               }
               else if (right) {
                  windowPosition.Left = (int)(validArea.Right - windowPosition.Width);
                  OnPreviewWindowStateChanged(AdvancedWindowState.SnapBottomRight);
               }
               else {
                  OnPreviewWindowStateChanged(AdvancedWindowState.SnapBottom);
               }
            }
            else if (top) {
               windowPosition.Top = (int)(validArea.Top);

               if (left && right) {
                  if (DockedState == DockingEdge.None) {
                     if (!_undocking) {
                        _normalSize = new Size(windowPosition.Width, windowPosition.Height);
                        Debug.WriteLine(string.Format("Docking To Top: {0}x{1} (WxH)", _normalSize.Width, _normalSize.Height));
                     }
                     DockedState = DockingEdge.Top;
                  }
                  windowPosition.Left = (int)validArea.Left;
                  windowPosition.Width = (int)validArea.Width;
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedTop);
               }
               else if (DockAgainst.HasFlag(DockingEdge.Top) && DockedState == DockingEdge.None || DockedState == DockingEdge.Top) {
                  if (DockedState == DockingEdge.None) {
                     if (!_undocking) {
                        _normalSize = new Size(windowPosition.Width, windowPosition.Height);
                        Debug.WriteLine(string.Format("Docking To Top: {0}x{1} (WxH)", _normalSize.Width, _normalSize.Height));
                     }
                     DockedState = DockingEdge.Top;
                  }
                  windowPosition.Left = (int)validArea.Left;
                  windowPosition.Width = (int)validArea.Width;
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedTop);
               }
               else if (left) {
                  windowPosition.Left = (int)(validArea.Left);
                  OnPreviewWindowStateChanged(AdvancedWindowState.SnapTopLeft);
               }
               else if (right) {
                  windowPosition.Left = (int)(validArea.Right - windowPosition.Width);
                  OnPreviewWindowStateChanged(AdvancedWindowState.SnapTopRight);
               }
               else {
                  OnPreviewWindowStateChanged(AdvancedWindowState.SnapTop);
               }
            }
            else if (left && right) {
               windowPosition.Left = (int)validArea.Left;
               windowPosition.Width = (int)validArea.Width;
               OnPreviewWindowStateChanged(AdvancedWindowState.DockedWidth);
            }
            else if (right) {
               windowPosition.Left = (int)(validArea.Right - windowPosition.Width);

               if (DockAgainst.HasFlag(DockingEdge.Right) && DockedState == DockingEdge.None || DockedState == DockingEdge.Right) {
                  if (DockedState == DockingEdge.None) {
                     if (!_undocking) {
                        _normalSize = new Size(windowPosition.Width, windowPosition.Height);
                        Debug.WriteLine(string.Format("Docking To Right: {0}x{1} (WxH)", _normalSize.Width, _normalSize.Height));
                     }
                     DockedState = DockingEdge.Right;
                  }
                  windowPosition.Top = (int)(validArea.Top);
                  windowPosition.Height = (int)validArea.Height;
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedRight);
               }
               else {
                  OnPreviewWindowStateChanged(AdvancedWindowState.SnapRight);
               }
            }
            else if (left) {
               windowPosition.Left = (int)(validArea.Left);

               if (DockAgainst.HasFlag(DockingEdge.Left) && DockedState == DockingEdge.None || DockedState == DockingEdge.Left) {
                  if (DockedState == DockingEdge.None) {
                     if (!_undocking) {
                        _normalSize = new Size(windowPosition.Width, windowPosition.Height);
                        Debug.WriteLine(string.Format("Docking To Left: {0}x{1} (WxH)", _normalSize.Width, _normalSize.Height));
                     }
                     DockedState = DockingEdge.Left;
                  }
                  windowPosition.Top = (int)(validArea.Top);
                  windowPosition.Height = (int)validArea.Height;
                  OnPreviewWindowStateChanged(AdvancedWindowState.DockedLeft);
               }
               else {
                  OnPreviewWindowStateChanged(AdvancedWindowState.SnapLeft);
               }
            }
            else {
               OnPreviewWindowStateChanged(AdvancedWindowState.Normal);
            }
         }
         if (left || top || right || bottom || _undocking) {
            if (_undocking && DockedState == DockingEdge.None) {
               Debug.WriteLine(string.Format("REALLY UnDocking: {0}x{1} (WxH)", _normalSize.Width, _normalSize.Height));
               windowPosition.Width = (int)_normalSize.Width;
               windowPosition.Height = (int)_normalSize.Height;
            }

            Debug.WriteLine(string.Format("Setting Window Size To: {0}x{1} (WxH)", windowPosition.Width, windowPosition.Height));
            Marshal.StructureToPtr(windowPosition, lParam, true);
         }

         return IntPtr.Zero;
      }

      private void OnWindowStateChanged(AdvancedWindowState windowState) {
         if (windowState != WindowState) {
            Trace.WriteLine("VisualState: " + windowState);
            WindowState = windowState;

            var stateArgs = new RoutedEventArgs(DockedStateChangedEvent, this);
            AssociatedObject.RaiseEvent(stateArgs);
         }
      }
      private void OnPreviewWindowStateChanged(AdvancedWindowState windowState) {
         if (windowState != WindowState) {
            Trace.WriteLine("Preview VisualState: " + windowState);
            VisualStateManager.GoToState(AssociatedObject, windowState.ToString(), true);
         }
      }

      public static readonly RoutedEvent DockedStateChangedEvent = EventManager.RegisterRoutedEvent("DockedStateChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SnapToBehavior));
      public static void AddDockedStateChangedHandler(DependencyObject d, RoutedEventHandler handler) {
         var uie = d as UIElement;
         if (uie != null) {
            uie.AddHandler(DockedStateChangedEvent, handler);
         }
      }
      public static void RemoveDockedStateChangedHandler(DependencyObject d, RoutedEventHandler handler) {
         var uie = d as UIElement;
         if (uie != null) {
            uie.RemoveHandler(DockedStateChangedEvent, handler);
         }
      }

      //public event RoutedEventHandler DockedStateChanged {
      //   add { AssociatedObject.AddHandler(DockedStateChangedEvent, value); }
      //   remove { AssociatedObject.RemoveHandler(DockedStateChangedEvent, value); }
      //}



      #region Additional Dependency Properties
      /// <summary>
      /// The DependencyProperty as the backing store for SnapDistance. 
      /// <remarks>Just so you can set it from XAML.</remarks>
      /// </summary>
      public static readonly DependencyProperty SnapDistanceProperty =
          DependencyProperty.Register("SnapDistance", typeof(Thickness), typeof(SnapToBehavior), new UIPropertyMetadata(new Thickness(20)));

      /// <summary>
      /// Gets or sets the snap distance.
      /// </summary>
      /// <value>The snap distance.</value>
      public Thickness SnapDistance {
         get { return (Thickness)GetValue(SnapDistanceProperty); }
         set { SetValue(SnapDistanceProperty, value); }
      }

      public DockingEdge DockAgainst {
         get { return (DockingEdge)GetValue(DockAgainstProperty); }
         set { SetValue(DockAgainstProperty, value); }
      }

      // Using a DependencyProperty as the backing store for DockAgainst.  This enables animation, styling, binding, etc...
      public static readonly DependencyProperty DockAgainstProperty =
          DependencyProperty.Register("DockAgainst", typeof(DockingEdge), typeof(SnapToBehavior), new UIPropertyMetadata(DockingEdge.None));


      public AdvancedWindowState WindowState {
         get { return (AdvancedWindowState)GetValue(WindowStateProperty); }
         set { SetValue(WindowStateProperty, value); }
      }

      // Using a DependencyProperty as the backing store for WindowState.  This enables animation, styling, binding, etc...
      public static readonly DependencyProperty WindowStateProperty =
          DependencyProperty.Register("WindowState", typeof(AdvancedWindowState), typeof(SnapToBehavior), new UIPropertyMetadata(AdvancedWindowState.Normal));


      public DockingEdge DockedState {
         get { return (DockingEdge)GetValue(DockedStateProperty); }
         set { SetValue(DockedStateProperty, value); }
      }

      // Using a DependencyProperty as the backing store for DockedState.  This enables animation, styling, binding, etc...
      public static readonly DependencyProperty DockedStateProperty =
          DependencyProperty.Register("DockedState", typeof(DockingEdge), typeof(SnapToBehavior), new UIPropertyMetadata(DockingEdge.None));

      #endregion Additional Dependency Properties

      protected override IEnumerable<MessageMapping> Handlers {
         get {
            yield return new MessageMapping(NativeMethods.WindowMessage.WindowPositionChanging, OnPreviewPositionChange);
            yield return new MessageMapping(NativeMethods.WindowMessage.EnterSizeMove, OnEnterSizeMove);
            yield return new MessageMapping(NativeMethods.WindowMessage.WindowPositionChanged, OnPositionChange);
         }
      }
   }

   [Flags]
   public enum DockingEdge {
      None = 0,
      Left = 1,
      Top = 2,
      Right = 4,
      Bottom = 8
   }

   public enum AdvancedWindowState {
      Normal,
      Minimized,
      Maximized,
      SnapTop,
      SnapRight,
      SnapBottom,
      SnapLeft,
      SnapTopRight,
      SnapBottomRight,
      SnapBottomLeft,
      SnapTopLeft,
      DockedTop,
      DockedRight,
      DockedBottom,
      DockedLeft,
      DockedHeight,
      DockedWidth
   }
}
