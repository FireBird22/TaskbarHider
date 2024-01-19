using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace TaskbarHider
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    ///

    public partial class MainWindow : Window
    {
        private static bool loopRun = true;
        private static readonly string[] gameNames = ["cs2", "VALORANT-Win64-Shipping", "cod", "SkyrimSE"];
        private static Process? gameProcess = null;

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        public MainWindow()
        {
            InitializeComponent();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(HandleUnCaughtException);
            Application.Current.Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;

            // Register to run on windows startup
            RegistryKey? startUpKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (startUpKey != null)
            {
                String? PATH = System.Environment.ProcessPath;
                if (PATH != null)
                {
                    startUpKey.SetValue("TaskbarHider", PATH);
                }
            }

            // Get the icon from resource for the tray icon
            Stream iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/TaskbarHider;component/icon.ico")).Stream;
            Icon myIcon = new(iconStream);
            iconStream.Close();

            // Context menu for the tray icon
            ContextMenuStrip trayContextMenu = new();
            ToolStripMenuItem refreshItem = new("Refresh");
            ToolStripMenuItem resizeItem = new("Resize");
            ToolStripMenuItem exitItem = new("Exit");

            refreshItem.Click += (sender, args) =>
            {
                gameProcess = null;
            };

            resizeItem.Click += (sender, args) =>
            {
                if (gameProcess == null)
                {
                    return;
                }

                if (gameProcess.ProcessName == "VALORANT-Win64-Shipping")
                {
                    MoveWindow(gameProcess.MainWindowHandle, 1272, -31, 2576, 1478, true);
                }
                else
                {
                    MoveWindow(gameProcess.MainWindowHandle, 1272, 0, 2560, 1440, true);
                }
            };

            exitItem.Click += async (sender, args) =>
            {
                loopRun = false;
                await Task.Delay(200);
                Taskbar.Show();
                Application.Current.Shutdown();
            };

            trayContextMenu.Items.Add(refreshItem);
            trayContextMenu.Items.Add(resizeItem);
            trayContextMenu.Items.Add(exitItem);

            // Create the tray notify icon
            NotifyIcon systemTray = new()
            {
                Icon = myIcon,
                Visible = true,
                Text = "TaskbarHider",
                ContextMenuStrip = trayContextMenu
            };
            ProcessWatcher();
        }

        private static async void ProcessWatcher()
        {
            // Set default taskbar state to show
            Taskbar.Show();

            while (loopRun)
            {
                await Task.Delay(10);
                if (gameProcess != null) goto Check;

                // Loop over and check for the running processes
                foreach (string game in gameNames)
                {
                    Process[] processList = Process.GetProcessesByName(game);
                    Process? currProcess = processList.Length > 0 ? processList[0] : null;
                    if (currProcess != null)
                    {
                        // Get main window handle after process is fully launched
                        try
                        {
                            gameProcess = currProcess;
                            currProcess.EnableRaisingEvents = true;
                            currProcess.Exited += (sender, e) =>
                            {
                                gameProcess = null;
                            };
                            break;
                        }
                        catch { }
                    }
                }

            // Skip to hide/show logic if we already have a process
            Check:
                try
                {
                    bool gameIsForeground = GetForegroundWindow() == gameProcess!.MainWindowHandle;
                    if (!Taskbar.IS_HIDDEN && gameIsForeground)
                    {
                        Taskbar.Hide();
                    }

                    if (Taskbar.IS_HIDDEN && !gameIsForeground)
                    {
                        Taskbar.Show();
                    }
                }
                catch { }
            }
        }

        private void HandleUnCaughtException(object sender, UnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(e.ExceptionObject.ToString());
            //Environment.Exit(0);
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(e.Exception.Message + "\n" + e.Exception.StackTrace);
            //Environment.Exit(0);
        }
    }
}