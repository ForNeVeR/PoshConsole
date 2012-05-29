using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;


namespace System.Management.Automation.Host
{
   /// <summary>
   /// <para>Provides an interface which extends the existing PowerShell PrivateData class with a
   /// <see cref="IPSWpfConsole" />, with access to the WPF Window and Dispatcher</para>
   /// </summary>
   public interface IPSWpfOptions
   {
      IPSWpfConsole WpfConsole { get; }
   }
   /// <summary>
   /// <para>Provides an interface which extends the existing PowerShell interfaces with a Xaml
   /// based user interface which allows loading of arbitrary bits of Xaml source.  This
   /// is peculiar to the <see cref="PoshConsole"/> implementation.</para>
   /// <para>The implemenation of these methods must be done on the UI Delegate thread, because
   /// typically Xaml can only be loaded on the UI thread, since no other thread is allowed to 
   /// create instances of the visual controls (the likely contents of the <paramref name="template"/>).
   /// </para>
   /// </summary>
   public interface IPSWpfConsole
   {
      Window RootWindow { get; }
      Dispatcher Dispatcher { get; }

      IList<Window> PopoutWindows { get; }

      FlowDocument Document { get; }

      Paragraph CurrentBlock { get; }

      void NewParagraph();
      //Runspace Runspace { get; }

      //void OutXaml(bool popup, System.Xml.XmlDocument template );
      //void OutXaml(bool popup, System.IO.FileInfo template );
      //void OutXaml(bool popup, System.Xml.XmlDocument template, params PSObject[] data);
      //void OutXaml(bool popup, System.IO.FileInfo template, params PSObject[] data);
      //void OutXaml(bool popup, params PSObject[] data);
      // Block GetOutputBlock(int id);
   }
}