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
        private static readonly string[] gameNames = ["cs2", "VALORANT-Win64-Shipping"];

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        public MainWindow()
        {
            InitializeComponent();

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
            ToolStripMenuItem resizeItem = new("Resize");
            ToolStripMenuItem exitItem = new("Exit");

            resizeItem.Click += (sender, args) =>
            {
                foreach (string game in gameNames)
                {
                    Process[] processList = Process.GetProcessesByName(game);
                    Process? gameProcess = processList.Length > 0 ? processList[0] : null;
                    if (gameProcess != null)
                    {
                        MoveWindow(gameProcess.MainWindowHandle, 1272, -31, 2576, 1478, true);
                        break;
                    }
                }
            };

            exitItem.Click += async (sender, args) =>
            {
                loopRun = false;
                await Task.Delay(200);
                Taskbar.Show();
                Application.Current.Shutdown();
            };

            trayContextMenu.Items.Add(resizeItem);
            trayContextMenu.Items.Add(exitItem);

            // Create the tray notify icon
            NotifyIcon systemTray = new()
            {
                Icon = myIcon,
                Visible = true,
                Text = "TaskbarHider",
            };

            // Show the context menu on click
            systemTray.Click += (sender, args) =>
            {
                trayContextMenu.Show(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
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
                nint? gameHwnd = null;

                // Loop over and check for the running processes
                Parallel.ForEach(gameNames, game =>
                {
                    Process[] processList = Process.GetProcessesByName(game);
                    Process? gameProcess = processList.Length > 0 ? processList[0] : null;
                    if (gameProcess != null)
                    {
                        gameHwnd = gameProcess.MainWindowHandle;
                    }
                });

                bool gameIsForeground = GetForegroundWindow() == gameHwnd;
                if (!Taskbar.IS_HIDDEN && gameIsForeground)
                {
                    Taskbar.Hide();
                }

                if (Taskbar.IS_HIDDEN && !gameIsForeground)
                {
                    Taskbar.Show();
                }
            }
        }
    }
}