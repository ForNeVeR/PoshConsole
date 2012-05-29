using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
//using System.Windows.Shapes;

using System.Windows.Navigation;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Huddled.Wpf.Controls
{
   /// <summary>
   /// ========================================
   /// .NET Framework 3.0 Custom Control
   /// ========================================
   ///
   /// Follow steps 1a or 1b and then 2 to use this custom control in a XAML file.
   ///
   /// Step 1a) Using this custom control in a XAML file that exists in the current project.
   /// Add this XmlNamespace attribute to the root element of the markup file where it is 
   /// to be used:
   ///
   ///     xmlns:MyNamespace="clr-namespace:Thumbnailer"
   ///
   ///
   /// Step 1b) Using this custom control in a XAML file that exists in a different project.
   /// Add this XmlNamespace attribute to the root element of the markup file where it is 
   /// to be used:
   ///
   ///     xmlns:MyNamespace="clr-namespace:Thumbnailer;assembly=Thumbnailer"
   ///
   /// You will also need to add a project reference from the project where the XAML file lives
   /// to this project and Rebuild to avoid compilation errors:
   ///
   ///     Right click on the target project in the Solution Explorer and
   ///     "Add Reference"->"Projects"->[Browse to and select this project]
   ///
   ///
   /// Step 2)
   /// Go ahead and use your control in the XAML file. Note that Intellisense in the
   /// XML editor does not currently work on custom controls and its child elements.
   ///
   ///     <MyNamespace:ThumbnailButton/>
   ///
   /// </summary>

   public partial class ThumbnailImage : Image
   {
      public static readonly DependencyProperty WindowSourceProperty = DependencyProperty.Register(
          "WindowSource",                                              // name
          typeof(IntPtr), typeof(ThumbnailImage),                  // Type information
          new FrameworkPropertyMetadata(IntPtr.Zero,                     // Default Value
          FrameworkPropertyMetadataOptions.AffectsMeasure |
          FrameworkPropertyMetadataOptions.AffectsArrange |
          FrameworkPropertyMetadataOptions.AffectsRender,         // Property Options
          new PropertyChangedCallback(OnWindowSourceChanged))      // Change Callback
          );



      public static DependencyProperty ClientAreaOnlyProperty = DependencyProperty.Register(
          "ClientAreaOnly",                                              // name
          typeof(bool), typeof(ThumbnailImage),                  // Type information
          new FrameworkPropertyMetadata(false,                     // Default Value
          FrameworkPropertyMetadataOptions.AffectsRender,         // Property Options
          new PropertyChangedCallback(OnClientAreaOnlyChanged))      // Change Callback
          );

      private static void OnWindowSourceChanged(DependencyObject depObj, DependencyPropertyChangedEventArgs args)
      {
         if (depObj is ThumbnailImage)
         {
            if (args.NewValue is IntPtr && !IntPtr.Zero.Equals(args.NewValue))
            {
               IntPtr source = (IntPtr)args.NewValue;
               (depObj as ThumbnailImage).InitialiseThumbnail(source);
            }
         }
      }

      private static void OnClientAreaOnlyChanged(DependencyObject depObj, DependencyPropertyChangedEventArgs args)
      {
         if (depObj is ThumbnailImage)
         {
            if (args.NewValue is bool)
            {
               (depObj as ThumbnailImage).ClientAreaOnly = (bool)args.NewValue;
               //if( !IntPtr.Zero.Equals(WindowSource) ) 
               //    InitialiseThumbnail(source);
            }
         }
      }
      private HwndSource _target;
      private IntPtr _thumb;

      /// <summary>Initializes the <see cref="ThumbnailImage"/> class.
      /// </summary>
      static ThumbnailImage()
      {
         OpacityProperty.OverrideMetadata(
             typeof(ThumbnailImage),
             new FrameworkPropertyMetadata(
                 1.0,
                 FrameworkPropertyMetadataOptions.Inherits,
                 delegate(DependencyObject obj, DependencyPropertyChangedEventArgs args)
                 {
                    ((ThumbnailImage)obj).UpdateThumbnail();
                 }));
         //This OverrideMetadata call tells the system that this element wants to provide a style that is different than its base class.
         //This style is defined in themes\generic.xaml
         DefaultStyleKeyProperty.OverrideMetadata(typeof(ThumbnailImage), new FrameworkPropertyMetadata(typeof(ThumbnailImage)));

      }

      /// <summary>Initializes a new instance of the <see cref="ThumbnailImage"/> class.
      /// </summary>
      public ThumbnailImage(IntPtr source)
         : this()
      {
         WindowSource = source;
         InitialiseThumbnail(source);
      }

      /// <summary>Initializes a new instance of the <see cref="ThumbnailImage"/> class.
      /// </summary>
      public ThumbnailImage()
      {
         // InitializeComponent();
         this.LayoutUpdated += new EventHandler(Thumbnail_LayoutUpdated);
         this.Unloaded += new RoutedEventHandler(Thumbnail_Unloaded);
         ////// hooks for clicks
         //this.ClickMode = ClickMode.Press;
         //this.MouseDown += new MouseButtonEventHandler(Thumbnail_MouseDown);
         //this.MouseUp += new MouseButtonEventHandler(Thumbnail_MouseUp);
         //this.MouseLeave += new MouseEventHandler(Thumbnail_MouseLeave);
         //keyIsDown = mouseIsDown = false;
      }


      /// <summary>Gets or sets the Window source
      /// </summary>
      /// <value>The Window source.</value>
      public IntPtr WindowSource
      {
         get { return (IntPtr)this.GetValue(WindowSourceProperty); }
         set { this.SetValue(WindowSourceProperty, value); }
      }

      /// <summary>Gets or sets a value indicating whether to show just the client area instead of the whole Window.
      /// </summary>
      /// <value><c>true</c> to show just the client area; <c>false</c> to show the whole Window, chrome and all.</value>
      public bool ClientAreaOnly
      {
         get { return (bool)this.GetValue(ClientAreaOnlyProperty); }
         set { this.SetValue(ClientAreaOnlyProperty, value); }
      }

      /// <summary>Gets or sets the opacity factor
      /// applied to the entire image when it is rendered in the user interface (UI).  
      /// This is a dependency property.
      /// </summary>
      /// <value></value>
      /// <returns>The opacity factor. Default opacity is 1.0. Expected values are between 0.0 and 1.0.</returns>
      public new double Opacity
      {
         get { return (double)this.GetValue(OpacityProperty); }
         set { this.SetValue(OpacityProperty, value); }
      }

      /// <summary>Initialises the thumbnail image
      /// </summary>
      /// <param name="source">The source.</param>
      private void InitialiseThumbnail(IntPtr source)
      {
         if (IntPtr.Zero != _thumb)
         {   // release the old thumbnail
            ReleaseThumbnail();
         }

         if (IntPtr.Zero != source)
         {
            // find our parent hwnd
            // [System.Windows.Interop.HwndSource]::FromHwnd( [Diagnostics.Process]::GetCurrentProcess().MainWindowHandle )
            //target = HwndSource.FromHwnd(System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle);
            _target = (HwndSource)HwndSource.FromVisual((UIElement)this);
            
            // if we have one, we can attempt to register the thumbnail
            if (_target != null ) {
               int result = Win32.NativeMethods.DwmRegisterThumbnail(_target.Handle, source, out this._thumb);
               if( 0 == result)
               {
                  Win32.NativeMethods.ThumbnailProperties props = new Win32.NativeMethods.ThumbnailProperties();
                  props.Visible = false;
                  props.ClientAreaOnly = this.ClientAreaOnly;
                  props.Opacity = (byte)(255 * this.Opacity);
                  props.Flags = Win32.NativeMethods.ThumbnailFlags.Visible | Win32.NativeMethods.ThumbnailFlags.SourceClientAreaOnly
                      | Win32.NativeMethods.ThumbnailFlags.Opacity;
                  Win32.NativeMethods.DwmUpdateThumbnailProperties(_thumb, ref props);
               }
            }
         }
      }

      /// <summary>Releases the thumbnail
      /// </summary>
      private void ReleaseThumbnail()
      {
         if (IntPtr.Zero != _thumb)
         {
            Win32.NativeMethods.DwmUnregisterThumbnail(_thumb);
            this._thumb = IntPtr.Zero;
         }
         this._target = null;
      }

      /// <summary>Updates the thumbnail
      /// </summary>
      private void UpdateThumbnail()
      {
         if (IntPtr.Zero != _thumb)
         {
            Win32.NativeMethods.ThumbnailProperties props = new Win32.NativeMethods.ThumbnailProperties();
            props.ClientAreaOnly = this.ClientAreaOnly;
            props.Opacity = (byte)(255 * this.Opacity);
            props.Flags = Win32.NativeMethods.ThumbnailFlags.SourceClientAreaOnly | Win32.NativeMethods.ThumbnailFlags.Opacity;
            Win32.NativeMethods.DwmUpdateThumbnailProperties(_thumb, ref props);
         }
      }

      /// <summary>Handles the Unloaded event of the Thumbnail control.
      /// </summary>
      /// <param name="sender">The source of the event.</param>
      /// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
      private void Thumbnail_Unloaded(object sender, RoutedEventArgs e)
      {
         ReleaseThumbnail();
      }

      /// <summary>Handles the LayoutUpdated event of the Thumbnail image
      /// Actually, we really just ask Windows to paint us at our new size...
      /// </summary>
      /// <param name="sender">The source of the event.</param>
      /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
      private void Thumbnail_LayoutUpdated(object sender, EventArgs e)
      {
         if (IntPtr.Zero.Equals(_thumb))
         {
            InitialiseThumbnail(this.WindowSource);
         }
         else if (null != _target)
         {
            if (!_target.RootVisual.IsAncestorOf(this))
            {
               //we are no longer in the visual tree
               ReleaseThumbnail();
               return;
            }

            GeneralTransform transform = TransformToAncestor(_target.RootVisual);
            Point a = transform.Transform(new Point(0, 0));
            Point b = transform.Transform(new Point(this.ActualWidth, this.ActualHeight));

            Win32.NativeMethods.ThumbnailProperties props = new Win32.NativeMethods.ThumbnailProperties();
            props.Visible = true;
            props.Destination = new Win32.NativeMethods.RECT(
                2 + (int)Math.Ceiling(a.X), 2 + (int)Math.Ceiling(a.Y),
                -2 + (int)Math.Ceiling(b.X), -2 + (int)Math.Ceiling(b.Y));
            props.Flags = Win32.NativeMethods.ThumbnailFlags.Visible | Win32.NativeMethods.ThumbnailFlags.RectDestination;
            Win32.NativeMethods.DwmUpdateThumbnailProperties(_thumb, ref props);
         }
      }

      /// <summary>
      /// Measures the size in layout required for child elements and determines a size for the Image.
      /// </summary>
      /// <param name="availableSize">The available size that this element can give to child elements. 
      /// Infinity can be specified as a value to indicate that the element will size to whatever content is available.</param>
      /// <returns>
      /// The size that this element determines it needs during layout, based on its calculations of child element sizes.
      /// </returns>
      protected override Size MeasureOverride(Size availableSize)
      {
         if (IntPtr.Zero == _thumb)
         {
            InitialiseThumbnail(this.WindowSource);
         }
         System.Drawing.Size size;
         Win32.NativeMethods.DwmQueryThumbnailSourceSize(_thumb, out size);

         double scale = 1;

         // our preferred size is the thumbnail source size
         // if less space is available, we scale appropriately
         if (size.Width > availableSize.Width)
            scale = availableSize.Width / size.Width;
         if (size.Height > availableSize.Height)
            scale = Math.Min(scale, availableSize.Height / size.Height);

         return new Size(size.Width * scale, size.Height * scale); ;
      }

      /// <summary>Positions elements and determines a size for the ThumbnailImage
      /// </summary>
      /// <param name="finalSize">The final area within the parent that this element should use to arrange itself and its children.</param>
      /// <returns>The actual size used.</returns>
      protected override Size ArrangeOverride(Size finalSize)
      {
         System.Drawing.Size size;
         Win32.NativeMethods.DwmQueryThumbnailSourceSize(this._thumb, out size);

         // scale to fit whatever size we were allocated
         double scale = finalSize.Width / size.Width;
         scale = Math.Min(scale, finalSize.Height / size.Height);

         return new Size(size.Width * scale, size.Height * scale);
      }
   }
}

namespace Win32
{

   public partial class NativeMethods
   {

      // ************************************************
      // ***** VISTA ONLY *******************************
      // ************************************************

      //[DllImport("dwmapi.dll")]
      //public static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, IntPtr pReserved, out SafeThumbnailHandle phThumbnailId);
      [DllImport("dwmapi.dll")]
      public static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr hThumbnailId);

      [DllImport("dwmapi.dll")]
      public static extern int DwmUnregisterThumbnail(IntPtr hThumbnailId);
      [DllImport("dwmapi.dll")]
      public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out System.Drawing.Size size);

      //[DllImport("dwmapi.dll")]
      //public static extern int DwmUpdateThumbnailProperties(SafeThumbnailHandle hThumbnailId, ref DwmThumbnailProperties ptnProperties);
      [DllImport("dwmapi.dll")]
      public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, ref ThumbnailProperties thumbProps);

      [DllImport("dwmapi.dll")]
      public static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool pfEnabled);

      [Flags()]
      public enum ThumbnailFlags : uint
      {
         /// <summary>
         /// Indicates a value for fSourceClientAreaOnly has been specified.
         /// </summary>
         RectDestination = 0x01,
         /// <summary>
         /// Indicates a value for rcSource has been specified.
         /// </summary>
         RectSource = 0x02,
         /// <summary>
         /// Indicates a value for opacity has been specified.
         /// </summary>
         Opacity = 0x04,
         /// <summary>
         /// Indicates a value for fVisible has been specified.
         /// </summary>
         Visible = 0x08,
         /// <summary>
         /// Indicates a value for fSourceClientAreaOnly has been specified.
         /// </summary>
         SourceClientAreaOnly = 0x10
      }

      [Serializable, StructLayout(LayoutKind.Sequential)]
      public struct ThumbnailProperties
      {
         public ThumbnailFlags Flags;
         public RECT Destination;
         public RECT Source;

         public byte Opacity;

         [MarshalAs(UnmanagedType.Bool)]
         public bool Visible;

         [MarshalAs(UnmanagedType.Bool)]
         public bool ClientAreaOnly;

         public ThumbnailProperties(RECT destination, ThumbnailFlags flags)
         {
            Source = new RECT();
            Destination = destination;
            Flags = flags;

            Opacity = 255;
            Visible = true;
            ClientAreaOnly = false;
         }
      }

      [Serializable, StructLayout(LayoutKind.Sequential)]
      public struct RECT
      {
         public int Left;
         public int Top;
         public int Right;
         public int Bottom;

         public RECT(int left_, int top_, int right_, int bottom_)
         {
            Left = left_;
            Top = top_;
            Right = right_;
            Bottom = bottom_;
         }

         public int Height
         {
            get { return (Bottom - Top) + 1; }
            set { Bottom = (Top + value) - 1; }
         }
         public int Width
         {
            get { return (Right - Left) + 1; }
            set { Right = (Left + value) - 1; }
         }
         public Size Size { get { return new Size(Width, Height); } }

         public Point Location { get { return new Point(Left, Top); } }

         // Handy method for converting to a System.Drawing.Rectangle
         public System.Drawing.Rectangle ToRectangle()
         { return System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom); }

         public static RECT FromRectangle(System.Drawing.Rectangle rectangle)
         {
            return new RECT(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
         }

         public override int GetHashCode()
         {
            return Left ^ ((Top << 13) | (Top >> 0x13))
              ^ ((Width << 0x1a) | (Width >> 6))
              ^ ((Height << 7) | (Height >> 0x19));
         }

         #region Operator overloads

         //public static implicit operator System.Windows.Shapes.Rectangle(ApiRect rect)
         //{
         //    System.Windows.Shapes.Rectangle sRectangle = new System.Windows.Shapes.Rectangle();
         //    sRectangle.Height = rect.Height;
         //    sRectangle.Width = rect.Width;
         //}

         public static implicit operator System.Drawing.Rectangle(RECT rect)
         {
            return System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
         }

         public static implicit operator RECT(System.Drawing.Rectangle rect)
         {
            return new RECT(rect.Left, rect.Top, rect.Right, rect.Bottom);
         }

         #endregion
      }

   }
}

