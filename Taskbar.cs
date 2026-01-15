namespace TaskbarHider
{
    using System;
    using System.Runtime.InteropServices;

    // Yeeted from: https://stackoverflow.com/questions/19022789/hide-taskbar-in-winforms-application
    internal class Taskbar
    {
        internal static bool IS_HIDDEN = false;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int FindWindow(string className, string windowText);

        [DllImport("user32.dll")]
        private static extern int ShowWindow(int hwnd, int command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int FindWindowEx(int parentHandle, int childAfter, string className, int windowTitle);

        [DllImport("user32.dll")]
        private static extern int GetDesktopWindow();

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 1;

        private const int ABM_GETSTATE = 0x00000004;
        private const int ABM_SETSTATE = 0x0000000A;

        private const int ABS_AUTOHIDE = 0x0000001;
        private const int ABS_ALWAYSONTOP = 0x0000002;

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left, top, right, bottom;
        }

        [DllImport("shell32.dll")]
        private static extern int SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        public static void SetAutoHide(bool enable)
        {
            APPBARDATA abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>()
            };

            int state = SHAppBarMessage(ABM_GETSTATE, ref abd);

            if (enable)
                state |= ABS_AUTOHIDE;
            else
                state &= ~ABS_AUTOHIDE;

            abd.lParam = state;
            SHAppBarMessage(ABM_SETSTATE, ref abd);
        }

        protected static int Handle
        {
            get
            {
                return FindWindow("Shell_TrayWnd", "");
            }
        }

        protected static int HandleOfStartButton
        {
            get
            {
                int handleOfDesktop = GetDesktopWindow();
                int handleOfStartButton = FindWindowEx(handleOfDesktop, 0, "button", 0);
                return handleOfStartButton;
            }
        }

        public static void Show()
        {
            IS_HIDDEN = false;
            _ = ShowWindow(Handle, SW_SHOW);
            _ = ShowWindow(HandleOfStartButton, SW_SHOW);
        }

        public static void Hide()
        {
            IS_HIDDEN = true;
            _ = ShowWindow(Handle, SW_HIDE);
            _ = ShowWindow(HandleOfStartButton, SW_HIDE);
        }
    }
}