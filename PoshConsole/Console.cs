using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace Huddled.PoshConsole
{
    internal class NativeMethods {
        [DllImport("kernel32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, ShowState nCmdShow);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetStdHandle(StdHandle nStdHandle, IntPtr hHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hHandle);

        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, ref SECURITY_ATTRIBUTES lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DuplicateHandle(IntPtr hSourceProcessHandle,
           IntPtr hSourceHandle, IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle,
           uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);


        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(
            IntPtr hFile,                   // handle to file
            byte[] lpBuffer,                // data buffer
            int nNumberOfBytesToRead,       // number of bytes to read
            out int lpNumberOfBytesRead,    // number of bytes read
            IntPtr overlapped               // overlapped buffer
            );
        
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int WriteFile(IntPtr hFile, byte[] buffer,
          int numBytesToWrite, out int numBytesWritten, IntPtr lpOverlapped);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, GwlIndex nIndex);
        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, GwlIndex nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int crKey, byte bAlpha, int dwFlags);

        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x80000;
        public const int LWA_ALPHA = 0x2;
        public const int LWA_COLORKEY = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bInheritHandle;
        }

        public enum GwlIndex : int
        {
            Id = (-12),
            Style = (-16),
            ExStyle = (-20)
        }

        public enum ShowState : int
        {
            SW_HIDE = 0
            /// and lots of others
        }

        public enum StdHandle : int
        {
            /// <summary>
            /// The standard input device
            /// </summary>
            INPUT_HANDLE = -10, //(DWORD)-10 	The standard input device.
            /// <summary>
            /// The standard output device.
            /// </summary>
            OUTPUT_HANDLE = -11, //(DWORD)-11 	The standard output device.
            /// <summary>
            /// The standard error device.
            /// </summary>
            ERROR_HANDLE = -12 //(DWORD)-12 	The standard error device.
        }

        public const UInt32 DUPLICATE_SAME_ACCESS = 0x00000002;
    }
    public class Console : IDisposable
    {
        public delegate void OutputDelegate(string text);
        public event OutputDelegate WriteOutput;
        public event OutputDelegate WriteError;
        private EventWaitHandle EndOutput = new ManualResetEvent(false);
        private EventWaitHandle EndError = new ManualResetEvent(false);

        private Thread outputThread, errorThread;

        // A nice handle to our console window
        private IntPtr handle;
        // And our process
        private System.Diagnostics.Process process;
        // Track whether Dispose has been called.
        private bool disposed = false;


        private IntPtr stdOutRead, stdOutWrite, stdInRead, stdInWrite, stdErrRead, stdErrWrite;
        private IntPtr stdOutReadCopy, stdInWriteCopy, stdErrReadCopy;
        /// <summary>
        /// Initializes a new instance of the <see cref="Console"/> class.
        /// </summary>
        public Console()
        {
            // Make ourselves a nice console
            NativeMethods.AllocConsole();
            // hide the window ...
            handle = NativeMethods.GetConsoleWindow();
            NativeMethods.ShowWindow(handle, NativeMethods.ShowState.SW_HIDE);
            //NativeMethods.SetWindowLong(handle, NativeMethods.GwlIndex.ExStyle, (NativeMethods.GetWindowLong(handle, NativeMethods.GwlIndex.ExStyle) | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT));
            //NativeMethods.SetLayeredWindowAttributes(handle, 0, 0, NativeMethods.LWA_ALPHA);

            process = System.Diagnostics.Process.GetCurrentProcess();


            NativeMethods.SECURITY_ATTRIBUTES saAttr;

            // Set the bInheritHandle flag so pipe handles are inherited.
            saAttr.nLength = Marshal.SizeOf(typeof(NativeMethods.SECURITY_ATTRIBUTES));
            saAttr.bInheritHandle = true;
            saAttr.lpSecurityDescriptor = IntPtr.Zero;


            // The steps for redirecting STDOUT:
            // * Create anonymous pipe to be STDOUT for us.
            // * Set STDOUT of our process to be WRITE handle to the pipe.
            // * Create a (noninheritable) duplicate of the read handle and close the inheritable read handle.

            if (!NativeMethods.CreatePipe(out stdOutRead, out stdOutWrite, ref saAttr, 0))
            {
                System.Diagnostics.Trace.TraceError("Couldn't create the STDOUT pipe");
            }
            if (!NativeMethods.SetStdHandle(NativeMethods.StdHandle.OUTPUT_HANDLE, stdOutWrite))
            {
                System.Diagnostics.Trace.TraceError("Couldn't redirect STDOUT!");
            }
            // Create noninheritable read handle and close the inheritable read handle.
            if( !NativeMethods.DuplicateHandle(process.Handle, stdOutRead, process.Handle, out stdOutReadCopy, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
            {
                System.Diagnostics.Trace.TraceError("Couldn't Duplicate STDOUT Handle");
            }
            NativeMethods.CloseHandle(stdOutRead);

            // For the output handles we need a thread to read them
            outputThread = new Thread(OutputThread);
            outputThread.Start();

            // The steps for redirecting STDERR are the same:
            // * Create anonymous pipe to be STDERR for us.
            // * Set STDERR of our process to be WRITE handle to the pipe.
            // * Create a (noninheritable) duplicate of the read handle and close the inheritable read handle.

            if (!NativeMethods.CreatePipe(out stdErrRead, out stdErrWrite, ref saAttr, 0))
            {
                System.Diagnostics.Trace.TraceError("Couldn't create the STDERR pipe");
            }
            if (!NativeMethods.SetStdHandle(NativeMethods.StdHandle.ERROR_HANDLE, stdErrWrite))
            {
                System.Diagnostics.Trace.TraceError("Couldn't redirect STDERR!");
            }
            // Create noninheritable read handle and close the inheritable read handle.
            if (!NativeMethods.DuplicateHandle(process.Handle, stdErrRead, process.Handle, out stdErrReadCopy, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
            {
                System.Diagnostics.Trace.TraceError("Couldn't Duplicate STDERR Handle");
            }
            NativeMethods.CloseHandle(stdErrRead);

            // For the output handles we need a thread to read them
            errorThread = new Thread(ErrorThread);
            errorThread.Start();

            // The steps for redirecting STDIN:
            // * Create anonymous pipe to be STDIN for us.
            // * Set STDIN of our process to be READ handle to the pipe.
            // * Create a (noninheritable) duplicate of the WRITE handle and close the inheritable WRITE handle.

            if (!NativeMethods.CreatePipe(out stdInRead, out stdInWrite, ref saAttr, 0))
            {
                System.Diagnostics.Trace.TraceError("Couldn't create the StdIn pipe");
            }
            if (!NativeMethods.SetStdHandle(NativeMethods.StdHandle.INPUT_HANDLE, stdInRead))
            {
                System.Diagnostics.Trace.TraceError("Couldn't redirect StdIn!");
            }
            // Create noninheritable read handle and close the inheritable read handle.
            if (!NativeMethods.DuplicateHandle(process.Handle, stdInWrite, process.Handle, out stdInWriteCopy, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS))
            {
                System.Diagnostics.Trace.TraceError("Couldn't Duplicate StdIn Handle");
            }
            NativeMethods.CloseHandle(stdInWrite);



            //if (!NativeMethods.CreatePipe(out stdInRead, out stdInWrite, ref saAttr, 0))
            //{
            //    System.Diagnostics.Trace.TraceError("Couldn't create a pipe");
            //}
            //buffer.WriteOutput(this.myUI.RawUI.ForegroundColor, myUI.RawUI.BackgroundColor, System.Console.In.ReadToEnd(), true);
            //buffer.WriteOutput(this.myUI.RawUI.ForegroundColor, myUI.RawUI.BackgroundColor, System.Console.In.ReadToEnd(), true);

        }

        /// <summary>
        /// The OutputThread ThreadStart delegate
        /// </summary>
        private void OutputThread()
        {
            int BytesRead;
            byte[] BufBytes = new byte[4096];
            // consider wrapping this in a System.IO.FileStream
            try
            {
                while (NativeMethods.ReadFile(stdOutReadCopy, BufBytes, 4096, out BytesRead, IntPtr.Zero))
                {
                    if (WriteOutput != null)
                    {
                        WriteOutput(System.Text.UTF8Encoding.Default.GetString(BufBytes, 0, BytesRead));
                    }
                }
            }
            catch (ThreadAbortException){}
            finally
            {
                NativeMethods.CloseHandle(stdOutWrite);
                NativeMethods.CloseHandle(stdOutReadCopy);
            }
        }

        /// <summary>
        /// The ErrorThread ThreadStart delegate
        /// </summary>
        private void ErrorThread()
        {
            int BytesRead;
            byte[] BufBytes = new byte[4096];
            // consider wrapping this in a System.IO.FileStream
            try
            {
                while (NativeMethods.ReadFile(stdErrReadCopy, BufBytes, 4096, out BytesRead, IntPtr.Zero))
                {
                    if (WriteError != null)
                    {
                        WriteError(System.Text.UTF8Encoding.Default.GetString(BufBytes, 0, BytesRead));
                    }
                }
            }
            catch (ThreadAbortException) { }
            finally
            {
                NativeMethods.CloseHandle(stdErrWrite);
                NativeMethods.CloseHandle(stdErrReadCopy);
            }
        }


        /// <summary>
        /// Writes the input.
        /// </summary>
        /// <param name="input">The input.</param>
        public void WriteInput(string input)
        {
            byte[] bytes = System.Text.UTF8Encoding.Default.GetBytes(input);
            int written;
            
            int hresult = NativeMethods.WriteFile(stdInWriteCopy, bytes, bytes.Length, out written, IntPtr.Zero);
            if ( hresult != 1 )
            {
                throw new Exception("Error Writing to StdIn, HRESULT: " + hresult.ToString());
            }
        }

        /// <summary>
        /// Implement IDisposable
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method. Therefore, we call GC.SupressFinalize 
            // to tell the runtime we dont' need to be finalized (we would clean up twice)
            GC.SuppressFinalize(this);
        }


        /// <summary>
        /// Handles actual cleanup actions, under two different scenarios
        /// </summary>
        /// <param name="disposing">if set to <c>true</c> we've been called directly or 
        /// indirectly by user code and can clean up both managed and unmanaged resources.
        /// Otherwise it's been called from the destructor/finalizer and we can't
        /// reference other managed objects (they might already be disposed).
        ///</param>
        private void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (!this.disposed)
            {
                // // If disposing equals true, dispose all managed resources ALSO.
                if (disposing){
                    errorThread.Abort();
                    outputThread.Abort();
                    //WriteInput("\n");

                    byte[] bytes = System.Text.UTF8Encoding.Default.GetBytes("\n" + (char)26); int written;
                    NativeMethods.WriteFile(stdErrWrite, bytes, bytes.Length, out written, IntPtr.Zero);
                    NativeMethods.WriteFile(stdOutWrite, bytes, bytes.Length, out written, IntPtr.Zero);
                    //errorThread.Join();
                    //outputThread.Join();
                }

                // Clean up UnManaged resources
                // If disposing is false, only the following code is executed.
                NativeMethods.FreeConsole();
                //NativeMethods.CloseHandle(stdOutWrite);
                //NativeMethods.CloseHandle(stdOutReadCopy);
                //NativeMethods.CloseHandle(stdErrWrite);
                //NativeMethods.CloseHandle(stdErrReadCopy);
                NativeMethods.CloseHandle(stdInWriteCopy);
                NativeMethods.CloseHandle(stdInRead);
            }
            disposed = true;         
        }

        /// <summary>
        /// Releases unmanaged resources and performs other cleanup operations before the
        /// <see cref="Console"/> is reclaimed by garbage collection.
        /// Use C# destructor syntax for finalization code.
        /// This destructor will run only if the Dispose method does not get called.
        /// </summary>
        /// <remarks>NOTE: Do not provide destructors in types derived from this class.</remarks>
        ~Console()      
        {
            // Instead of cleaning up in BOTH Dispose() and here ...
            // We call Dispose(false) for the best readability and maintainability.
            Dispose(false);
        }
    }
}
