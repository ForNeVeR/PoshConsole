﻿/**************************************************************************\
    Copyright Microsoft Corporation. All Rights Reserved.
\**************************************************************************/

namespace System.Windows.Extensions
{
   using System;
   using System.Collections.Generic;
   using System.Diagnostics;
   using System.Diagnostics.CodeAnalysis;
   using System.Reflection;
   using System.Runtime.InteropServices;
   using System.Windows;
   using System.Windows.Controls;
   using System.Windows.Interop;
   using System.Windows.Media;
   using Standard;


   using HANDLE_MESSAGE = System.Collections.Generic.KeyValuePair<Standard.WM, WindowChrome.MessageHandler>;

   public class WindowChrome : DependencyObject
   {
      /// <summary>
      /// Delegate declaration that matches WndProc signatures.
      /// </summary>
      internal delegate IntPtr MessageHandler(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled);

      #region Fields

      private const SWP _SwpFlags = SWP.FRAMECHANGED | SWP.NOSIZE | SWP.NOMOVE | SWP.NOZORDER | SWP.NOOWNERZORDER | SWP.NOACTIVATE;
      //        private static readonly bool _OnVista = Environment.OSVersion.Version.Major >= 6;

      private readonly List<HANDLE_MESSAGE> _messageTable;

      /// <summary>The Window that's chrome is being modified.</summary>
      private Window _window;

      private Brush _background;
      /// <summary>Underlying HWND for the _window.</summary>
      private IntPtr _hwnd;

      /// <summary>Template for chromeless window.</summary>
      private ControlTemplate _template;
      /// <summary>Border for the Window template, obtained when the template is applied.</summary>
      private Border _templatePartBorder;

      // Keep track of this so we can detect when we need to apply changes.  Tracking these separately
      // as I've seen using just one cause things to get enough out of sync that occasionally the caption will redraw.
      private WindowState _lastRoundingState;
      private WindowState _lastMenuState;

      #endregion

      /// <summary>
      /// Default constructor usable by XAML.
      /// </summary>
      public WindowChrome()
      {
         _messageTable = new List<HANDLE_MESSAGE>
            {
                new HANDLE_MESSAGE(WM.SETTEXT,               _HandleSetTextOrIcon),
                new HANDLE_MESSAGE(WM.SETICON,               _HandleSetTextOrIcon),
                new HANDLE_MESSAGE(WM.NCACTIVATE,            _HandleNCActivate),
                new HANDLE_MESSAGE(WM.NCCALCSIZE,            _HandleNCCalcSize),
                new HANDLE_MESSAGE(WM.NCHITTEST,             _HandleNCHitTest),
                new HANDLE_MESSAGE(WM.NCRBUTTONUP,           _HandleNCRButtonUp),
                new HANDLE_MESSAGE(WM.SIZE,                  _HandleSize),
                new HANDLE_MESSAGE(WM.WINDOWPOSCHANGED,      _HandleWindowPosChanged),
                new HANDLE_MESSAGE(WM.DWMCOMPOSITIONCHANGED, _HandleDwmCompositionChanged), 
            };
      }

      #region Attached Properties and support methods.

      #region WindowChrome Attached Dependency Property
      public static readonly DependencyProperty WindowChromeProperty = DependencyProperty.RegisterAttached(
          "WindowChrome",
          typeof(WindowChrome),
          typeof(WindowChrome),
          new PropertyMetadata(null, _OnChromeChanged, _CoerceChrome));

      private static object _CoerceChrome(DependencyObject d, object value)
      {
         if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(d))
         {
            return value;
         }

         var w = (Window)d;
         var chrome = (WindowChrome)value;

         if (w == null)
         {
            throw new ArgumentNullException("d");
         }

         if (chrome == null)
         {
            throw new ArgumentNullException("value");
         }

         if (chrome._window != null)
         {
            throw new NotSupportedException();
         }

         if (!w.CheckAccess())
         {
            throw new NotSupportedException();
         }

         return chrome;
      }

      private static void _OnChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
      {
         if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(d))
         {
            return;
         }

         var w = (Window)d;
         var c = (WindowChrome)e.NewValue;

         c._SetWindow(w);
      }

      [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
      public static WindowChrome GetWindowChrome(Window window)
      {
         if (window == null)
         {
            throw new ArgumentNullException("window");
         }

         return (WindowChrome)window.GetValue(WindowChromeProperty);
      }

      [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
      public static void SetWindowChrome(Window window, WindowChrome chrome)
      {
         if (window == null)
         {
            throw new ArgumentNullException("window");
         }

         window.SetValue(WindowChromeProperty, chrome);
      }
      #endregion WindowChrome Attached Dependency Property

      #region HitTestable Attached Dependency Property
      public static readonly DependencyProperty HitTestableProperty = DependencyProperty.RegisterAttached(
          "HitTestable",
          typeof(bool),
          typeof(WindowChrome),
          new PropertyMetadata(false));

      [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
      public static bool GetHitTestable(UIElement element)
      {
         if (element == null)
         {
            throw new ArgumentNullException("element");
         }

         return (bool)element.GetValue(HitTestableProperty);
      }

      [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
      public static void SetHitTestable(UIElement element, bool hitTestVisible)
      {
         if (element == null)
         {
            throw new ArgumentNullException("element");
         }

         element.SetValue(HitTestableProperty, hitTestVisible);
      }
      #endregion HitTestable Attached Dependency Property
      #endregion

      #region Dependency Properties and support methods.


      /// <summary>
      /// Generic DP callback.
      /// Most dependency properties affect the Window in a way that requires it to be repainted for the new property to visibly take effect.
      /// </summary>
      /// <param name="d">The WindowChrome object</param>
      /// <param name="e">Old and New values are compared for equality to short-circuit the redraw.</param>
      private static void _OnPropertyChangedThatRequiresRepaint(DependencyObject d, DependencyPropertyChangedEventArgs e)
      {
         var c = d as WindowChrome;
         Debug.Assert(null != c);

         if (e.OldValue != e.NewValue)
         {
            c._UpdateFrameState(true);
         }
      }

      /// <summary>
      /// Generic DP coersion.  There are several properties of type Double that only can't be negative.
      /// </summary>
      /// <param name="d">The WindowChrome object</param>
      /// <param name="value">The double that shouldn't be negative.</param>
      private static object _CoerceNonNegativeDouble(DependencyObject d, object value)
      {
         if ((double)value < 0)
         {
            throw new ArgumentException("The property cannot be set to a negative value.");
         }

         return value;
      }


      #region UseGlassFrame Dependency Property
      public static readonly DependencyProperty UseGlassFrameProperty = DependencyProperty.Register(
          "UseGlassFrame",
          typeof(bool),
          typeof(WindowChrome),
          new PropertyMetadata(false, _OnPropertyChangedThatRequiresRepaint));

      /// <summary>
      /// Get or set whether to use the glass frame if it's available.
      /// </summary>
      public bool UseGlassFrame
      {
         get { return (bool)GetValue(UseGlassFrameProperty); }
         set { SetValue(UseGlassFrameProperty, value); }
      }
      #endregion UseGlassFrame Dependency Property

      #region IsGlassEnabled Dependency Property
      private static readonly DependencyPropertyKey IsGlassEnabledPropertyKey = DependencyProperty.RegisterReadOnly(
          "IsGlassEnabled",
          typeof(bool),
          typeof(WindowChrome),
          new PropertyMetadata(false));

      public static readonly DependencyProperty IsGlassEnabledProperty = IsGlassEnabledPropertyKey.DependencyProperty;

      /// <summary>
      /// Get whether glass is enabled by the system and whether it hasn't been turned off by setting UseGlassFrame="False"
      /// </summary>
      public bool IsGlassEnabled
      {
         get { return (bool)GetValue(IsGlassEnabledProperty); }
         private set { SetValue(IsGlassEnabledPropertyKey, value); }
      }
      #endregion IsGlassEnabled Dependency Property

      #region CaptionHeight Dependency Property
      public static readonly DependencyProperty CaptionHeightProperty = DependencyProperty.Register(
          "CaptionHeight",
          typeof(double),
          typeof(WindowChrome),
          new PropertyMetadata(SystemParameters.CaptionHeight, _OnPropertyChangedThatRequiresRepaint, _CoerceNonNegativeDouble));

      /// <summary>The extent of the top of the window to treat as the caption.</summary>
      public double CaptionHeight
      {
         get { return (double)GetValue(CaptionHeightProperty); }
         set { SetValue(CaptionHeightProperty, value); }
      }
      #endregion CaptionHeight Dependency Property


      // #region CaptionElement Dependency Property
      // private FrameworkElement _CaptionElement;


      // public static readonly DependencyProperty CaptionElementNameProperty = DependencyProperty.Register(
      //     "CaptionElementName",
      //     typeof(string), 
      //     typeof(WindowChrome),
      //     new UIPropertyMetadata(null, _OnCaptionElementNameChanged, null, true));

      // private static void _OnCaptionElementNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
      //{
      //   var chrome = (WindowChrome)d;
      //   if (chrome == null) throw new ArgumentNullException("d");
      //   var window = chrome._window;
      //   if (window != null)
      //   {
      //      if (string.IsNullOrEmpty((string) e.NewValue))
      //      {
      //         chrome._CaptionElement = null;
      //      }
      //      else
      //      {
      //         chrome._CaptionElement = window.FindName((string) e.NewValue) as FrameworkElement;
      //      }
      //   }
      //}



      //public string CaptionElementName
      // {
      //    get { return (string)GetValue(CaptionElementNameProperty); }
      //    set { SetValue(CaptionElementNameProperty, value); }
      // }

      //#endregion CaptionElement Dependency Property

      #region ResizeBorder Dependency Property

      // Not exposing a public setter for this property as it would have surprising results to the end-user.
      private static readonly DependencyPropertyKey ResizeBorderPropertyKey = DependencyProperty.RegisterReadOnly(
          "ResizeBorder",
          typeof(Thickness),
          typeof(WindowChrome),
          new PropertyMetadata(
              new Thickness(
                  SystemParameters.ResizeFrameVerticalBorderWidth,
                  SystemParameters.ResizeFrameHorizontalBorderHeight,
                  SystemParameters.ResizeFrameVerticalBorderWidth,
                  SystemParameters.ResizeFrameHorizontalBorderHeight)));

      public static readonly DependencyProperty ResizeBorderProperty = ResizeBorderPropertyKey.DependencyProperty;

      /// <summary>Get the bounds of the resize grips on the Window.</summary>
      public Thickness ResizeBorder
      {
         get { return (Thickness)GetValue(ResizeBorderProperty); }
         // private set { SetValue(ResizeBorderPropertyKey, value); }
      }
      #endregion ResizeBorder Dependency Property

      #region ClientBorderThickness Dependency Property

      // TODO: The WPF SystemParameters class is wrong on Vista.  Need to P/Invoke to get the right data.
      // There's the additional iPaddedBorderWidth field on NONCLIENTMETRICS structure that's not being accounted for.
      // Need to investigate how these values are affected based on the current theme.
      // The SystemParameters class' behavior is good enough I'm just sticking with it for now.
      public static readonly DependencyProperty ClientBorderThicknessProperty = DependencyProperty.Register(
          "ClientBorderThickness",
          typeof(Thickness),
          typeof(WindowChrome),
          new PropertyMetadata(
         // Default value is to emulate the standard non-client thickness.
         // These values are the same as ResizeBorder and CaptionHeight DPs.
              new Thickness(
                  SystemParameters.ResizeFrameVerticalBorderWidth,
                  SystemParameters.ResizeFrameHorizontalBorderHeight + SystemParameters.CaptionHeight,
                  SystemParameters.ResizeFrameVerticalBorderWidth,
                  SystemParameters.ResizeFrameHorizontalBorderHeight),
              _OnPropertyChangedThatRequiresRepaint));

      /// <summary>
      /// Thickness to extend the glass frame, when it's available.
      /// </summary>
      /// <remarks>
      /// A thickness with all sides equal to -1 will extend the glass frame fully into the Window.
      /// </remarks>
      public Thickness ClientBorderThickness
      {
         get { return (Thickness)GetValue(ClientBorderThicknessProperty); }
         set { SetValue(ClientBorderThicknessProperty, value); }
      }
      #endregion ClientBorderThickness Dependency Property

      #region CornerRadius Dependency Property

      public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
          "CornerRadius",
          typeof(CornerRadius),
          typeof(WindowChrome),
          new PropertyMetadata(default(CornerRadius), _OnPropertyChangedThatRequiresRepaint, _CoerceCornerRadius));


      private static object _CoerceCornerRadius(DependencyObject d, object value)
      {
         if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(d))
         {
            return value;
         }
         return value;
      }

      /// <summary>
      /// Get or set the radius of rounded corners on the window.
      /// </summary>
      /// <remarks>
      /// This affects all four corners of the window.
      /// Setting this to large values can potentially obscure parts of the Window's content.
      /// Setting this to zero, or setting RoundCorners="False", will affect square corners on the Window.
      /// </remarks>
      public CornerRadius CornerRadius
      {
         get { return (CornerRadius)GetValue(CornerRadiusProperty); }
         set { SetValue(CornerRadiusProperty, value); }
      }
      #endregion CornerRadius Dependency Property


      #endregion




      /// <summary>Display the system menu at a specified location.</summary>
      /// <param name="screenLocation">The location to display the system menu, in logical screen coordinates.</param>
      public void ShowSystemMenu(Point screenLocation)
      {
         Point physicalScreenLocation = DpiHelper.LogicalPixelsToDevice(screenLocation);
         _ShowSystemMenu(physicalScreenLocation);
      }

      private void _ShowSystemMenu(Point physicalScreenLocation)
      {
         const uint TPM_RETURNCMD = 0x0100;
         const uint TPM_LEFTBUTTON = 0x0;

         IntPtr hmenu = NativeMethods.GetSystemMenu(_hwnd, false);

         uint cmd = NativeMethods.TrackPopupMenuEx(hmenu, TPM_LEFTBUTTON | TPM_RETURNCMD, (int)physicalScreenLocation.X, (int)physicalScreenLocation.Y, _hwnd, IntPtr.Zero);
         if (0 != cmd)
         {
            NativeMethods.PostMessage(_hwnd, WM.SYSCOMMAND, new IntPtr(cmd), IntPtr.Zero);
         }
      }


      private void _SetWindow(Window window)
      {
         Debug.Assert(null == _window);
         Debug.Assert(null != window);

         _window = window;
         _background = _window.Background;
         //if(_CaptionElement == null && !string.IsNullOrEmpty(CaptionElementName))
         //{
         //   if (!_window.IsLoaded)
         //   {
         //      _window.SourceInitialized +=
         //         (sender, e) => _CaptionElement = _window.FindName(CaptionElementName) as FrameworkElement;
         //   } else
         //   {
         //      _CaptionElement = _window.FindName(CaptionElementName) as FrameworkElement;
         //   }
         //}

         var resourceLocater = new Uri("/" + Assembly.GetExecutingAssembly().GetName().Name + ";component/ChromelessWindowTemplate.xaml", System.UriKind.Relative);
         _template = (ControlTemplate)Application.LoadComponent(resourceLocater);
         Debug.Assert(null != _template);

         _window.Template = _template;

         // Use whether we can get an HWND to determine if the Window has been loaded.
         _hwnd = new WindowInteropHelper(_window).Handle;

         if (IntPtr.Zero != _hwnd)
         {
            _window.ApplyTemplate();
            _HookCustomChrome();
         }
         else
         {
            _window.SourceInitialized += (sender, e) =>
            {
               _hwnd = new WindowInteropHelper(_window).Handle;
               Debug.Assert(IntPtr.Zero != _hwnd);
               _HookCustomChrome();
            };
         }
      }

      /// <summary>
      /// Subclass the HWND to handle the NC messages and start listening for DWM changes.
      /// </summary>
      private void _HookCustomChrome()
      {
         Debug.Assert(IntPtr.Zero != _hwnd);
         Debug.Assert(null != _window);

         HwndSource.FromHwnd(_hwnd).AddHook(_WndProc);

         // Our template should have been applied.
         _templatePartBorder = (Border)_template.FindName("PART_BackgroundBorder", _window);
         Debug.Assert(null != _templatePartBorder);

         // Force this the first time.
         _UpdateSystemMenu(_window.WindowState);
         _UpdateFrameState(true);

         NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, _SwpFlags);
      }

      #region WindowProc and Message Handlers

      private IntPtr _WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         // Only expecting messages for our cached HWND.
         Debug.Assert(hwnd == _hwnd);

         var message = (WM)msg;
         foreach (var handlePair in _messageTable)
         {
            if (handlePair.Key == message)
            {
               return handlePair.Value(message, wParam, lParam, ref handled);
            }
         }
         return IntPtr.Zero;
      }

      private IntPtr _HandleSetTextOrIcon(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         bool modified = _ModifyStyle(WS.VISIBLE, 0);

         // Setting the caption text and icon cause Windows to redraw the caption.
         // Letting the default WndProc handle the message without the WS_VISIBLE
         // style applied bypasses the redraw.
         IntPtr lRet = NativeMethods.DefWindowProc(_hwnd, uMsg, wParam, lParam);

         // Put back the style we removed.
         if (modified)
         {
            _ModifyStyle(0, WS.VISIBLE);
         }
         handled = true;
         return lRet;
      }

      private IntPtr _HandleNCActivate(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         // Despite MSDN's documentation of lParam not being used,
         // calling DefWindowProc with lParam set to -1 causes Windows not to draw over the caption.

         // Directly call DefWindowProc with a custom parameter
         // which bypasses any other handling of the message.
         IntPtr lRet = NativeMethods.DefWindowProc(_hwnd, WM.NCACTIVATE, wParam, new IntPtr(-1));
         handled = true;
         return lRet;
      }

      private IntPtr _HandleNCCalcSize(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         int result = 0;
         // lParam is an [in, out] that can be either a RECT* (wParam == 0) or an NCCALCSIZE_PARAMS*.
         // Since the first field of NCCALCSIZE_PARAMS is a RECT and is the only field we care about
         // we can unconditionally treat it as a RECT.

         // When the window procedure receives the WM_NCCALCSIZE message, the first rectangle contains
         // the new coordinates of a window that has been moved or resized, that is, it is the proposed 
         // new window coordinates. The second contains the coordinates of the window before it was moved 
         // or resized. The third contains the coordinates of the window's client area before the window 
         // was moved or resized. If the window is a child window, the coordinates are relative to the 
         // client area of the parent window. If the window is a top-level window, the coordinates are 
         // relative to the screen origin.

         // When the window procedure returns, the first rectangle contains the coordinates of the new 
         // client rectangle resulting from the move or resize. The second rectangle contains the valid
         // destination rectangle, and the third rectangle contains the valid source rectangle. The last
         // two rectangles are used in conjunction with the return value of the WM_NCCALCSIZE message to
         // determine the area of the window to be preserved. 
         //if (wParam.ToInt32() == 1)
         //{
         //   NCCALCSIZE_PARAMS ncParam = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(lParam, typeof(NCCALCSIZE_PARAMS));

         //   RECT r = new RECT()
         //               {
         //                  top = ncParam.r3.top + (ncParam.r1.top - ncParam.r2.top),
         //                  left = ncParam.r3.left + (ncParam.r1.left - ncParam.r2.left),
         //                  bottom = ncParam.r3.bottom + (ncParam.r1.bottom - ncParam.r2.bottom),
         //                  right = ncParam.r3.right + (ncParam.r1.right - ncParam.r2.right),
         //               };
         //   ncParam.r3 = ncParam.r2;
         //   ncParam.r2 = ncParam.r1;
         //   //ncParam.r1 = r;
         //   result = (int)NCCALCSIZE_Results.ValidRects;
         //   Marshal.StructureToPtr(ncParam, lParam, true);
         //}
         //else
         //{
         //   result = 0;
         //}
         handled = true;
         return new IntPtr(result);
      }

      private IntPtr _HandleNCHitTest(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         IntPtr lRet = IntPtr.Zero;
         handled = false;

         // Give DWM a chance at this first.
         if (NativeMethods.IsCompositionEnabled && UseGlassFrame)
         {
            // If we're on Vista, give the DWM a chance to handle the message first.
            handled = NativeMethods.DwmDefWindowProc(_hwnd, uMsg, wParam, lParam, out lRet);
         }

         // Handle letting the system know if we consider the mouse to be in our effective non-client area.
         // If DWM already handled this by way of DwmDefWindowProc, then respect their call.
         if (IntPtr.Zero == lRet)
         {
            var mousePosScreen = new Point(Utility.GET_X_LPARAM(lParam), Utility.GET_Y_LPARAM(lParam));
            Rect windowPosition = _GetWindowRect();

            HT ht = _HitTestNca(
                DpiHelper.DeviceRectToLogical(windowPosition),
                DpiHelper.DevicePixelsToLogical(mousePosScreen));

            // Don't blindly respect HTCAPTION.
            // We want UIElements in the caption area to be actionable so run through a hittest first.
            if (ht == HT.CAPTION)
            {
               Point mousePosWindow = mousePosScreen;
               mousePosWindow.Offset(-windowPosition.X, -windowPosition.Y);
               mousePosWindow = DpiHelper.DevicePixelsToLogical(mousePosWindow);
               if (_HitTestUIElements(mousePosWindow))
               {
                  ht = HT.NOWHERE;
               }
            }
            handled = HT.NOWHERE != ht;
            lRet = new IntPtr((int)ht);
         }
         return lRet;
      }

      private IntPtr _HandleNCRButtonUp(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         // Emulate the system behavior of clicking the right mouse button over the caption area
         // to bring up the system menu.
         if (HT.CAPTION == (HT)wParam.ToInt32())
         {
            _ShowSystemMenu(new Point(Utility.GET_X_LPARAM(lParam), Utility.GET_Y_LPARAM(lParam)));
         }
         handled = false;
         return IntPtr.Zero;
      }

      private IntPtr _HandleSize(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         const int SIZE_MAXIMIZED = 2;

         // Force when maximized.
         // We can tell what's happening right now, but the Window doesn't yet know it's
         // maximized.  Not forcing this update will eventually cause the
         // default caption to be drawn.
         WindowState? state = null;
         if (wParam.ToInt32() == SIZE_MAXIMIZED)
         {
            state = WindowState.Maximized;
         }
         _UpdateSystemMenu(state);

         // Still let the default WndProc handle this.
         handled = false;
         return IntPtr.Zero;
      }

      private IntPtr _HandleWindowPosChanged(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         // http://blogs.msdn.com/oldnewthing/archive/2008/01/15/7113860.aspx
         // The WM_WINDOWPOSCHANGED message is sent at the end of the window
         // state change process. It sort of combines the other state change
         // notifications, WM_MOVE, WM_SIZE, and WM_SHOWWINDOW. But it doesn't
         // suffer from the same limitations as WM_SHOWWINDOW, so you can
         // reliably use it to react to the window being shown or hidden.

         _UpdateSystemMenu(null);

         //if (!IsGlassEnabled)
         //{
         //    //Debug.Assert(IntPtr.Zero != lParam);
         //    //var wp = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
         //    //_SetRoundingRegion(wp);
         //    //_templatePartBorder.CornerRadius = CornerRadius;
         //}

         // Still want to pass this to DefWndProc
         handled = false;
         return IntPtr.Zero;
      }

      private IntPtr _HandleDwmCompositionChanged(WM uMsg, IntPtr wParam, IntPtr lParam, ref bool handled)
      {
         _UpdateFrameState(false);

         handled = false;
         return IntPtr.Zero;
      }

      #endregion

      private bool _HitTestUIElements(Point mousePosWindow)
      {
         bool ret = false;
         VisualTreeHelper.HitTest(
             _window,
             target =>
             {
                UIElement uie = target as UIElement;
                if (null != uie && WindowChrome.GetHitTestable(uie))
                {
                   ret = true;
                   return HitTestFilterBehavior.Stop;
                }
                return HitTestFilterBehavior.Continue;
             },
             result => HitTestResultBehavior.Stop,
             new PointHitTestParameters(mousePosWindow));

         return ret;
      }

      /// <summary>Add and remove a native WindowStyle from the HWND.</summary>
      /// <param name="removeStyle">The styles to be removed.  These can be bitwise combined.</param>
      /// <param name="addStyle">The styles to be added.  These can be bitwise combined.</param>
      /// <returns>Whether the styles of the HWND were modified as a result of this call.</returns>
      private bool _ModifyStyle(WS removeStyle, WS addStyle)
      {
         Debug.Assert(IntPtr.Zero != _hwnd);
         var dwStyle = (WS)NativeMethods.GetWindowLongPtr(_hwnd, GWL.STYLE).ToInt32();
         var dwNewStyle = (dwStyle & ~removeStyle) | addStyle;
         if (dwStyle == dwNewStyle)
         {
            return false;
         }

         NativeMethods.SetWindowLongPtr(_hwnd, GWL.STYLE, new IntPtr((int)dwNewStyle));
         return true;
      }

      /// <summary>
      /// Get the WindowState as the native HWND knows it to be.  This isn't necessarily the same as what Window thinks.
      /// </summary>
      private WindowState _GetHwndState()
      {
         var wpl = NativeMethods.GetWindowPlacement(_hwnd);
         switch (wpl.showCmd)
         {
            case SW.SHOWMINIMIZED: return WindowState.Minimized;
            case SW.SHOWMAXIMIZED: return WindowState.Maximized;
         }
         return WindowState.Normal;
      }

      /// <summary>
      /// Get the bounding rectangle for the window in physical coordinates.
      /// </summary>
      /// <returns>The bounding rectangle for the window.</returns>
      private Rect _GetWindowRect()
      {
         // Get the window rectangle.
         RECT windowPosition;
         NativeMethods.GetWindowRect(_hwnd, out windowPosition);

         return new Rect(windowPosition.left, windowPosition.top, windowPosition.right - windowPosition.left, windowPosition.bottom - windowPosition.top);
      }

      /// <summary>
      /// Update the items in the system menu based on the current, or assumed, WindowState.
      /// </summary>
      /// <param name="assumeState">
      /// The state to assume that the Window is in.  This can be null to query the Window's state.
      /// </param>
      /// <remarks>
      /// We want to update the menu while we have some control over whether the caption will be repainted.
      /// </remarks>
      private void _UpdateSystemMenu(WindowState? assumeState)
      {
         const MF mfEnabled = MF.ENABLED | MF.BYCOMMAND;
         const MF mfDisabled = MF.GRAYED | MF.DISABLED | MF.BYCOMMAND;

         WindowState state = assumeState ?? _GetHwndState();

         if (null != assumeState || _lastMenuState != state)
         {
            _lastMenuState = state;

            bool modified = _ModifyStyle(WS.VISIBLE, 0);
            IntPtr hmenu = NativeMethods.GetSystemMenu(_hwnd, false);
            if (IntPtr.Zero != hmenu)
            {
               var dwStyle = (WS)NativeMethods.GetWindowLongPtr(_hwnd, GWL.STYLE).ToInt32();

               bool canMinimize = Utility.IsFlagSet((int)dwStyle, (int)WS.MINIMIZEBOX);
               bool canMaximize = Utility.IsFlagSet((int)dwStyle, (int)WS.MAXIMIZEBOX);
               bool canSize = Utility.IsFlagSet((int)dwStyle, (int)WS.THICKFRAME);

               switch (state)
               {
                  case WindowState.Maximized:
                     NativeMethods.EnableMenuItem(hmenu, SC.RESTORE, mfEnabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MOVE, mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.SIZE, mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MINIMIZE, canMinimize ? mfEnabled : mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MAXIMIZE, mfDisabled);
                     break;
                  case WindowState.Minimized:
                     NativeMethods.EnableMenuItem(hmenu, SC.RESTORE, mfEnabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MOVE, mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.SIZE, mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MINIMIZE, mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MAXIMIZE, canMaximize ? mfEnabled : mfDisabled);
                     break;
                  default:
                     NativeMethods.EnableMenuItem(hmenu, SC.RESTORE, mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MOVE, mfEnabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.SIZE, canSize ? mfEnabled : mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MINIMIZE, canMinimize ? mfEnabled : mfDisabled);
                     NativeMethods.EnableMenuItem(hmenu, SC.MAXIMIZE, canMaximize ? mfEnabled : mfDisabled);
                     break;
               }
            }

            if (modified)
            {
               _ModifyStyle(0, WS.VISIBLE);
            }
         }
      }

      private void _UpdateFrameState(bool force)
      {
         if (IntPtr.Zero == _hwnd)
         {
            return;
         }

         bool frameState = NativeMethods.IsCompositionEnabled;
         if (force || frameState != IsGlassEnabled)
         {
            IsGlassEnabled = frameState && UseGlassFrame;

            if (!IsGlassEnabled)
            {
               _SetRoundingRegion(null);
               _templatePartBorder.CornerRadius = CornerRadius;
               _templatePartBorder.Visibility = Visibility.Visible;
            }
            else
            {
               _ExtendGlassFrame();
               _ClearRoundingRegion();
               _templatePartBorder.Visibility = Visibility.Hidden;
            }

            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, _SwpFlags);
         }
      }

      private void _ClearRoundingRegion()
      {
         NativeMethods.SetWindowRgn(_hwnd, IntPtr.Zero, NativeMethods.IsWindowVisible(_hwnd));
      }

      private void _SetRoundingRegion(WINDOWPOS? wp)
      {
         const int MONITOR_DEFAULTTONEAREST = 0x00000002;

         // We're early - WPF hasn't necessarily updated the state of the window.
         // Need to query it ourselves.
         WINDOWPLACEMENT wpl = NativeMethods.GetWindowPlacement(_hwnd);

         if (wpl.showCmd == SW.SHOWMAXIMIZED)
         {
            int left;
            int top;

            if (wp.HasValue)
            {
               left = wp.Value.x;
               top = wp.Value.y;
            }
            else
            {
               Rect r = _GetWindowRect();
               left = (int)r.Left;
               top = (int)r.Top;
            }

            IntPtr hMon = NativeMethods.MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST);

            MONITORINFO mi = NativeMethods.GetMonitorInfo(hMon);
            RECT rcMax = mi.rcWork;
            // The location of maximized window takes into account the border that Windows was
            // going to remove, so we also need to consider it.
            rcMax.Offset(-left, -top);

            IntPtr hrgn = NativeMethods.CreateRectRgnIndirect(rcMax);
            NativeMethods.SetWindowRgn(_hwnd, hrgn, NativeMethods.IsWindowVisible(_hwnd));
         }
         else
         {
            int width;
            int height;

            // Use the size if it's specified.
            if (null != wp && !Utility.IsFlagSet(wp.Value.flags, (int)SWP.NOSIZE))
            {
               width = wp.Value.cx;
               height = wp.Value.cy;
            }
            else if (null != wp && (_lastRoundingState == _window.WindowState))
            {
               return;
            }
            else
            {
               Rect r = _GetWindowRect();
               width = (int)r.Width;
               height = (int)r.Height;
            }

            _lastRoundingState = _window.WindowState;

            IntPtr hrgn = IntPtr.Zero;

            //var maxCorner = Math.Max(
            //   Math.Max(CornerRadius.BottomLeft, CornerRadius.BottomRight),
            //   Math.Max(CornerRadius.TopLeft, CornerRadius.TopRight));

            //if (maxCorner > 0)
            // {
            //    Point radius = DpiHelper.LogicalPixelsToDevice(new Point(maxCorner, maxCorner));
            //    hrgn = NativeMethods.CreateRoundRectRgn(0, 0, width, height, (int)radius.X * 2, (int)radius.Y * 2);

            //    radius = DpiHelper.LogicalPixelsToDevice(new Point(CornerRadius.TopLeft, CornerRadius.TopLeft));
            //    IntPtr corner = NativeMethods.CreateRoundRectRgn(0, 0, width / 2, height / 2, (int)radius.X * 2, (int)radius.Y * 2);
            //    hrgn = NativeMethods.OrRgn(hrgn, corner);

            //    radius = DpiHelper.LogicalPixelsToDevice(new Point(CornerRadius.TopRight, CornerRadius.TopRight));
            //    corner = NativeMethods.CreateRoundRectRgn(width / 2, 0, width, height / 2, (int)radius.X * 2, (int)radius.Y * 2);
            //    hrgn = NativeMethods.OrRgn(hrgn, corner);

            //    radius = DpiHelper.LogicalPixelsToDevice(new Point(CornerRadius.BottomLeft, CornerRadius.BottomLeft));
            //    corner = NativeMethods.CreateRoundRectRgn(0, height / 2, width / 2, height, (int)radius.X * 2, (int)radius.Y * 2);
            //     hrgn = NativeMethods.OrRgn(hrgn, corner);

            //     radius = DpiHelper.LogicalPixelsToDevice(new Point(CornerRadius.BottomRight, CornerRadius.BottomRight));
            //     corner = NativeMethods.CreateRoundRectRgn(width / 2, height / 2, width, height, (int)radius.X * 2, (int)radius.Y * 2);
            //     hrgn = NativeMethods.OrRgn(hrgn, corner);

            //}
            // else
            // {
            hrgn = NativeMethods.CreateRectRgn(0, 0, width, height);
            //}
            _window.Background = _background;
            NativeMethods.SetWindowRgn(_hwnd, hrgn, NativeMethods.IsWindowVisible(_hwnd));
         }
      }

      private void _ExtendGlassFrame()
      {
         Debug.Assert(null != _window);

         // Expect that this might be called on OSes other than Vista.
         // Not an error.  Just not on Vista so we're not going to get glass.
         if (!NativeMethods.IsCompositionEnabled) return;

         // Can't do anything with this call until the Window has been shown.
         if (IntPtr.Zero == _hwnd) return;


         //HwndSource hwndSource = HwndSource.FromHwnd(_hwnd);

         //// The Window's Background property is handled by _UpdateFrameState.
         //// The Border/Background part of the template is made invisible when glass is on.
         if (NativeMethods.IsCompositionEnabled)
         {
            //    // Apply the transparent background to the HWND
            //    hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;

            // Thickness is going to be DIPs, need to convert to system coordinates.
            //Point deviceTopLeft = DpiHelper.LogicalPixelsToDevice(new Point(ClientBorderThickness.Left, ClientBorderThickness.Top));
            //Point deviceBottomRight = DpiHelper.LogicalPixelsToDevice(new Point(ClientBorderThickness.Right, ClientBorderThickness.Bottom));

            //var dwmMargin = new MARGINS
            //{
            //    // err on the side of pushing in glass an extra pixel.
            //    cxLeftWidth = (int)Math.Ceiling(deviceTopLeft.X),
            //    cxRightWidth = (int)Math.Ceiling(deviceBottomRight.X),
            //    cyTopHeight = (int)Math.Ceiling(deviceTopLeft.Y),
            //    cyBottomHeight = (int)Math.Ceiling(deviceBottomRight.Y),
            //};

            _window.Background = Brushes.Transparent;
            NativeMethods.ExtendFrameIntoClientArea(_hwnd, ClientBorderThickness); //
         }

      }

      /// <summary>
      /// Matrix of the HT values to return when responding to NC window messages.
      /// </summary>
      [SuppressMessage("Microsoft.Performance", "CA1814:PreferJaggedArraysOverMultidimensional", MessageId = "Member")]
      private static readonly HT[,] _HitTestBorders = new[,]
        {
            { HT.TOPLEFT,    HT.TOP,     HT.TOPRIGHT    },
            { HT.LEFT,       HT.NOWHERE, HT.RIGHT       },
            { HT.BOTTOMLEFT, HT.BOTTOM,  HT.BOTTOMRIGHT },
        };

      private HT _HitTestNca(Rect windowPosition, Point mousePosition)
      {
         // Determine if hit test is for resizing, default middle (1,1).
         int uRow = 1;
         int uCol = 1;
         bool onResizeBorder = false;


         //if (_CaptionElement != null)
         //{
         //   var captionCheck = _CaptionElement.PointFromScreen(mousePosition);
         //   if (captionCheck.X > 0 &&
         //      captionCheck.Y > 0 &&
         //      captionCheck.Y < _CaptionElement.ActualHeight &&
         //      captionCheck.X < _CaptionElement.ActualWidth)
         //   {
         //      return HT.CAPTION;
         //   } else Trace.Write( String.Format("({0},{1})", captionCheck.X, captionCheck.Y) );
         //}                                             

         // Determine if the point is at the top or bottom of the window.
         if (mousePosition.Y >= windowPosition.Top && mousePosition.Y < windowPosition.Top + ResizeBorder.Top + CaptionHeight)
         {
            onResizeBorder = (mousePosition.Y < (windowPosition.Top + ResizeBorder.Top));
            //if (!onResizeBorder && (_CaptionElement != null))
            //{
            //   Trace.WriteLine(String.Format("[{0},{1}]", mousePosition.X, mousePosition.Y));
            //   return HT.NOWHERE;
            //}
            uRow = 0; // top (caption or resize border)
         }
         else if (mousePosition.Y < windowPosition.Bottom && mousePosition.Y >= windowPosition.Bottom - (int)ResizeBorder.Bottom)
         {
            uRow = 2; // bottom
         }

         // Determine if the point is at the left or right of the window.
         if (mousePosition.X >= windowPosition.Left && mousePosition.X < windowPosition.Left + (int)ResizeBorder.Left)
         {
            uCol = 0; // left side
         }
         else if (mousePosition.X < windowPosition.Right && mousePosition.X >= windowPosition.Right - ResizeBorder.Right)
         {
            uCol = 2; // right side
         }

         HT ht = _HitTestBorders[uRow, uCol];
         if (ht == HT.TOP && !onResizeBorder)
         {
            ht = HT.CAPTION;
         }

         return ht;
      }
   }
}
