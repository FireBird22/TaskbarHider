using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Task = System.Threading.Tasks.Task;

namespace TaskbarHider
{
    public partial class MainWindow : Window
    {
        // 1. Consolidated Config Structure
        private record ProcessConfig(bool AltPos, bool AutoHide);

        private static readonly string ProcessNamesFile = Path.Combine(AppContext.BaseDirectory, "process_names.txt");
        private static Dictionary<string, ProcessConfig> _processConfigMap = new(StringComparer.OrdinalIgnoreCase);

        private static Process? _currentGameInstance;
        private WinEventDelegate? _winEventDelegate;
        private IntPtr _hookId;

        #region Win32 API

        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        // Fast way to get process name without overhead of System.Diagnostics.Process
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll")]
        private static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, [Out] StringBuilder lpBaseName, uint nSize);

        #endregion

        public MainWindow()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += (s, e) => MessageBox.Show(e.ExceptionObject.ToString());

            // Ensure file exists
            if (!File.Exists(ProcessNamesFile)) File.Create(ProcessNamesFile).Dispose();

            ReadProcessNames();
            RegisterStartupTask();
            InitializeTrayIcon();

            // Run one-off tasks in background
            _ = ExitGSkillAsync();
            _ = HandleEthernetAsync();

            // Hook into the system immediately
            InitializeSystemHook();
        }

        private void InitializeSystemHook()
        {
            Taskbar.Show(); // Ensure default state

            // Keep reference to delegate to prevent GC collection
            _winEventDelegate = new WinEventDelegate(WinEventProc);

            // 2. Set the Hook: Listen only for Foreground Change events
            _hookId = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hookId != IntPtr.Zero) UnhookWinEvent(_hookId);
            base.OnClosed(e);
        }

        // 3. This triggers ONLY when user switches windows
        // REPLACEMENT: This goes inside MainWindow class
        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string processName;
            try
            {
                // Reverting to Standard .NET method. 
                // It is slightly slower than raw Win32, but handles permissions/anti-cheat much better.
                // Since this only runs ONCE per window switch (not 6 times a second), the performance impact is zero.
                using (var p = Process.GetProcessById((int)pid))
                {
                    processName = p.ProcessName;
                }
            }
            catch
            {
                // If we can't read the process (Access Denied), we can't manage it.
                return;
            }

            // Logic: Is this a tracked game?
            bool isGameActive = _currentGameInstance != null;
            bool isForegroundTarget = _processConfigMap.ContainsKey(processName);

            // SCENARIO A: No game tracked, but we just switched to a target game
            if (!isGameActive && isForegroundTarget)
            {
                TrackNewGame((int)pid, processName);
            }
            // SCENARIO B: We are tracking a game
            else if (isGameActive)
            {
                // Are we currently looking at the tracked game?
                bool gameIsForeground = (hwnd == _currentGameInstance!.MainWindowHandle);

                if (!Taskbar.IS_HIDDEN && gameIsForeground)
                    Taskbar.Hide();
                else if (Taskbar.IS_HIDDEN && !gameIsForeground)
                    Taskbar.Show();
            }
        }

        private void TrackNewGame(int pid, string processName)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                _currentGameInstance = process;

                var config = _processConfigMap[processName];
                if (config.AutoHide) Taskbar.SetAutoHide(true);

                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    // Clean up when game closes
                    Dispatcher.Invoke(() =>
                    {
                        _currentGameInstance = null;
                        Taskbar.Show();
                        Taskbar.SetAutoHide(false);
                        ShowToast("Process Exited", processName);
                    });
                };

                ShowToast("Process Started", processName);
                Taskbar.Hide(); // Hide immediately on detection
            }
            catch (ArgumentException) { /* Process closed before we could grab it */ }
        }

        private void ReadProcessNames()
        {
            var newMap = new Dictionary<string, ProcessConfig>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(ProcessNamesFile))
            {
                foreach (string line in File.ReadLines(ProcessNamesFile)) // ReadLines is lazier than ReadAllLines
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string name = line.Trim();
                    bool altPos = name.Contains("**");
                    bool autoHide = name.Contains("&&");

                    name = name.Replace("**", "").Replace("&&", "");
                    newMap[name] = new ProcessConfig(altPos, autoHide);
                }
            }

            _processConfigMap = newMap;
            ShowToast("Processes Editor", "Process list updated");
        }

        #region Tray Icon & Utilities

        private void InitializeTrayIcon()
        {
            using Stream iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/TaskbarHider;component/icon.ico")).Stream;
            NotifyIcon trayIcon = new()
            {
                Icon = new Icon(iconStream),
                Visible = true,
                Text = "TaskbarHider",
                ContextMenuStrip = BuildContextMenu()
            };
        }

        private ContextMenuStrip BuildContextMenu()
        {
            ContextMenuStrip menu = new();

            menu.Items.Add("Edit Processes", null, (_, _) =>
            {
                Process.Start(new ProcessStartInfo { FileName = ProcessNamesFile, UseShellExecute = true })?.WaitForExit();
                ReadProcessNames();
            });

            menu.Items.Add("Refresh", null, (_, _) =>
            {
                _currentGameInstance = null;
                Taskbar.Show();
                ShowToast("Refreshed", "Waiting for new process");
            });

            menu.Items.Add("Resize", null, (_, _) =>
            {
                if (_currentGameInstance == null) return;

                string name = _currentGameInstance.ProcessName;
                if (_processConfigMap.TryGetValue(name, out var config) && config.AltPos)
                    MoveWindow(_currentGameInstance.MainWindowHandle, 1272, -31, 2576, 1478, true);
                else
                    MoveWindow(_currentGameInstance.MainWindowHandle, 1280, 0, 2560, 1440, true);
            });

            menu.Items.Add("Exit", null, (_, _) =>
            {
                if (_hookId != IntPtr.Zero) UnhookWinEvent(_hookId);
                Taskbar.Show();
                Application.Current.Shutdown();
            });

            return menu;
        }

        private static void ShowToast(string title, string message)
        {
            new ToastContentBuilder().AddText(title).AddText(message).Show();
        }

        private static void RegisterStartupTask()
        {
            try
            {
                using TaskService ts = new();
                TaskDefinition task = ts.NewTask();
                task.Triggers.Add(new LogonTrigger());
                task.Actions.Add(new ExecAction(Environment.ProcessPath));
                task.Principal.RunLevel = TaskRunLevel.Highest;
                ts.RootFolder.RegisterTaskDefinition("TaskbarHider", task);
            }
            catch { }
        }

        private static async Task HandleEthernetAsync()
        {
            // Simple check to avoid UI blocking
            var result = MessageBox.Show("Reset adapter?", "Taskbar Hider", MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes) return;

            await Task.Delay(30000); // 30 seconds

            // Execute without shell execute for slightly lower overhead
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "Disable-NetAdapter -Name 'Ethernet' -Confirm:$false; Start-Sleep 2; Enable-NetAdapter -Name 'Ethernet' -Confirm:$false",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private static async Task ExitGSkillAsync()
        {
            await Task.Delay(40000);
            await Task.Run(() =>
            {
                foreach (Process p in Process.GetProcessesByName("hid"))
                {
                    try
                    {
                        if (p.MainModule?.FileVersionInfo.FileDescription?.Contains("Trident Z") == true)
                            p.Kill();
                    }
                    catch { }
                }
            });
        }
        #endregion
    }
}