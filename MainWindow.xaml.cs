using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Windows.UI.Notifications;
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
        private static string[] gameNames = [];
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
            string name_files = @$"{AppContext.BaseDirectory}\process_names.txt";
            FileStream fs = new(name_files, FileMode.OpenOrCreate); fs.Close();
            gameNames = File.ReadAllLines(name_files);

            // Register to run on windows startup
            RegistryKey? startUpKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (startUpKey != null)
            {
                string? PATH = Environment.ProcessPath;
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
            ToolStripMenuItem editProcessesItem = new("Edit Processes");
            ToolStripMenuItem refreshItem = new("Refresh");
            ToolStripMenuItem resizeItem = new("Resize");
            ToolStripMenuItem exitItem = new("Exit");

            // Tray menu function for editProcess
            editProcessesItem.Click += (sender, args) =>
            {
                Process? editor = Process.Start(new ProcessStartInfo()
                {
                    FileName = name_files,
                    UseShellExecute = true,
                });
                if (editor == null) return;
                ShowToast("Processes Editor", "Waiting for editor to exit.");
                editor.WaitForExit();
                gameNames = File.ReadAllLines(name_files);
                ShowToast("Processes Editor", "Successfully update process list");
            };

            // Tray menu function for refresh
            refreshItem.Click += (sender, args) =>
            {
                gameProcess = null;
                ShowToast("Refreshed", "Looking for new process");
            };

            // Tray menu function for resize
            resizeItem.Click += (sender, args) =>
            {
                if (gameProcess == null) return;

                if (gameProcess.ProcessName == "VALORANT-Win64-Shipping")
                {
                    MoveWindow(gameProcess.MainWindowHandle, 1272, -31, 2576, 1478, true);
                }
                else
                {
                    MoveWindow(gameProcess.MainWindowHandle, 1280, 0, 2560, 1440, true);
                }
            };

            // Tray menu function for exit
            exitItem.Click += async (sender, args) =>
            {
                loopRun = false;
                await Task.Delay(200);
                Taskbar.Show();
                Application.Current.Shutdown();
            };

            // Add items to the context menu
            trayContextMenu.Items.Add(editProcessesItem);
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
                                ShowToast("Process Exited", $"{game} with PID {gameProcess.Id}");
                                gameProcess = null;
                            };
                            ShowToast("Process Started", $"{game} with PID {gameProcess.Id}");
                            break;
                        }
                        catch { }
                    }
                }

            // Skip to hide/show logic if we already have a process
            Check:
                if (gameProcess == null) continue;

                bool gameIsForeground = GetForegroundWindow() == gameProcess.MainWindowHandle;
                if (!Taskbar.IS_HIDDEN && gameIsForeground) Taskbar.Hide();
                if (Taskbar.IS_HIDDEN && !gameIsForeground) Taskbar.Show();
            }
        }

        private static async void ShowToast(string title, string message)
        {
            ToastContent toast = new ToastContentBuilder()
                                .AddHeader(Guid.NewGuid().ToString(), title, "")
                                .SetToastDuration(ToastDuration.Short)
                                .AddText(message)
                                .GetToastContent();

            ToastNotification toastNotification = new(toast.GetXml());
            toastNotification.ExpirationTime = DateTimeOffset.Now.AddSeconds(5);
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toastNotification);
        }

        private void HandleUnCaughtException(object sender, UnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(e.ExceptionObject.ToString());
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(e.Exception.Message + "\n" + e.Exception.StackTrace);
        }
    }
}