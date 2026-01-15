using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Windows.UI.Notifications;
using Application = System.Windows.Application;
using Task = System.Threading.Tasks.Task;

namespace TaskbarHider
{
    public partial class MainWindow : Window
    {
        private static readonly string ProcessNamesFile = @$"{AppContext.BaseDirectory}\process_names.txt";
        private static HashSet<string> ProcessNamesList = new(StringComparer.Ordinal);
        private static HashSet<string> ProcessNamesListAltPos = new(StringComparer.Ordinal);
        private static HashSet<string> ProcessNamesListAutoHide = new(StringComparer.Ordinal);

        private static bool watcherRunning = true;
        private static Process? currentGameInstance = null;

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();

        public MainWindow()
        {
            // Init stuff
            InitializeComponent();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(HandleUnCaughtException);
            Application.Current.Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            FileStream fs = new(ProcessNamesFile, FileMode.OpenOrCreate); fs.Close();
            ReadProcessNames();

            try
            {
                TaskService ts = new();
                TaskDefinition startupTask = ts.NewTask();
                startupTask.Triggers.Add(new LogonTrigger());
                startupTask.Actions.Add(new ExecAction(Environment.ProcessPath, null, null));
                startupTask.Principal.RunLevel = TaskRunLevel.Highest;
                ts.RootFolder.RegisterTaskDefinition("TaskbarHider", startupTask);
            }
            catch (Exception _ex) { };

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
                    FileName = ProcessNamesFile,
                    UseShellExecute = true,
                });
                if (editor == null) return;
                ShowToast("Processes Editor", "Waiting for editor to exit.");
                editor.WaitForExit();
                ReadProcessNames();
            };

            // Tray menu function for refresh
            refreshItem.Click += (sender, args) =>
            {
                currentGameInstance = null;
                ShowToast("Refreshed", "Looking for new process");
            };

            // Tray menu function for resize
            resizeItem.Click += (sender, args) =>
            {
                if (currentGameInstance == null) return;

                if (ProcessNamesListAltPos.Contains(currentGameInstance.ProcessName))
                {
                    MoveWindow(currentGameInstance.MainWindowHandle, 1272, -31, 2576, 1478, true);
                }
                else
                {
                    MoveWindow(currentGameInstance.MainWindowHandle, 1280, 0, 2560, 1440, true);
                }
            };

            // Tray menu function for exit
            exitItem.Click += async (sender, args) =>
            {
                watcherRunning = false;
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
            ExitGSkill();
            HandleEthernet();
        }

        private static async void ProcessWatcher()
        {
            // Set default taskbar state to show
            Taskbar.Show();

            while (watcherRunning)
            {
                await Task.Delay(200);
                if (currentGameInstance != null) goto Check;

                // Loop over and check for the running processes
                foreach (string game in ProcessNamesList)
                {
                    Process[] processList = Process.GetProcessesByName(game);
                    Process? currProcess = processList.Length > 0 ? processList[0] : null;
                    if (currProcess != null)
                    {
                        Debug.WriteLine(currProcess.ProcessName);
                        // Get main window handle after process is fully launched
                        try
                        {
                            if( ProcessNamesListAutoHide.Contains(game))
                                Taskbar.SetAutoHide(true);
                            currentGameInstance = currProcess;
                            currProcess.EnableRaisingEvents = true;
                            currProcess.Exited += (sender, e) =>
                            {
                                ShowToast("Process Exited", $"{game} with PID {currentGameInstance.Id}");
                                currentGameInstance = null;
                                Taskbar.Show();
                                Taskbar.SetAutoHide(false);
                            };
                            ShowToast("Process Started", $"{game} with PID {currentGameInstance.Id}");
                            break;
                        }
                        catch { }
                    }
                }

            // Skip to hide/show logic if we already have a process
            Check:
                if (currentGameInstance == null) continue;

                bool gameIsForeground = GetForegroundWindow() == currentGameInstance.MainWindowHandle;

                if (!Taskbar.IS_HIDDEN && gameIsForeground) Taskbar.Hide();
                if (Taskbar.IS_HIDDEN && !gameIsForeground) Taskbar.Show();
            }
        }

        private static void ReadProcessNames()
        {
            string[] names = File.ReadAllLines(ProcessNamesFile);
            ProcessNamesList = [];
            ProcessNamesListAltPos = [];
            ProcessNamesListAutoHide = [];
            foreach (string name in names)
            {
                string temp = name;
                bool isAltPost = temp.Contains("**");
                bool isAutoHide = temp.Contains("&&");
                temp = temp.Replace("**", string.Empty);
                temp = temp.Replace("&&", string.Empty);
                ProcessNamesList.Add(temp);

                if (isAltPost)
                    ProcessNamesListAltPos.Add(temp);

                if (isAutoHide)
                    ProcessNamesListAutoHide.Add(temp);
            }
            ShowToast("Processes Editor", "Successfully update process list");
        }

        private static void ShowToast(string title, string message)
        {
            ToastContent toast = new ToastContentBuilder()
                                .AddHeader(Guid.NewGuid().ToString(), title, "")
                                .SetToastDuration(ToastDuration.Short)
                                .AddText(message)
                                .GetToastContent();

            ToastNotification toastNotification = new(toast.GetXml());
            toastNotification.ExpirationTime = DateTimeOffset.Now.AddSeconds(2);
            ToastNotificationManagerCompat.CreateToastNotifier().Show(toastNotification);
        }

        private static void HandleUnCaughtException(object sender, UnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(e.ExceptionObject.ToString());
        }

        private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(e.Exception.Message + "\n" + e.Exception.StackTrace);
        }

        private static async void HandleEthernet()
        {
            MessageBoxResult result = System.Windows.MessageBox.Show("Reset adapter?", "Taskbar hider", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;
            await Task.Delay(1000 * 30);
            string script = $@"
                Disable-NetAdapter -Name 'Ethernet' -Confirm:$false
                Start-sleep 2
                Enable-NetAdapter -Name 'Ethernet' -Confirm:$false
            ";

            Process task = new();
            task.StartInfo.FileName = "powershell.exe";
            task.StartInfo.Arguments = script;
            task.StartInfo.CreateNoWindow = false;
            task.Start();
        }

        private static async void ExitGSkill()
        {
            await Task.Delay(1000 * 40);
            Process[] processes = Process.GetProcessesByName("hid");
            for (int i = 0; i < processes.Length; i++)
            {
                Process p = processes[i];
                if (p.MainModule.FileVersionInfo.FileDescription.Contains("Trident Z"))
                {
                    p.Kill();
                }
            }
        }
    }
}