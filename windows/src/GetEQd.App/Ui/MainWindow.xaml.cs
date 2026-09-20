using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GetEQd.Ui
{
    /// <summary>
    /// The application window. Asks DWM for a dark title bar so the frame matches the
    /// console instead of fighting it.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                int enabled = 1;
                if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
                }

                int rounded = DwmwcpRound;
                DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
            }
            catch
            {
                // Dark chrome is a nicety; a failure here must not stop the console.
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            View?.Shutdown();
            base.OnClosed(e);
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
