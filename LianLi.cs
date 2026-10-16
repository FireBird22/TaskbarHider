using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.Management;

namespace TaskbarHider
{
    internal static class LianLi
    {
        // SL-Infinity controller (VID 0x0CF2, PID 0xA102) lighting protocol, matching
        // L-Connect's own transfer sequence (reverse engineered in lewisgibson/FanControl.LianLi).
        // All reports are HID feature reports whose first byte is the report id 0xE0.
        private const byte ReportId = 0xE0;
        private const byte ConfigCommand = 0x10;
        private const byte ArgbSyncRegister = 97;      // 1 = LEDs follow motherboard ARGB header
        private const byte BlankWireMode = 0x32;       // Rainbow, what L-Connect blanks ports with
        private const byte BrightnessOff = 8;          // L-Connect's "Off" brightness step
        private const int PortCount = 8;

        private static readonly Guid HidGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

        private const uint GENERIC_READ_WRITE = 0xC0000000;
        private const uint FILE_SHARE_READ_WRITE = 0x00000003;
        private const uint OPEN_EXISTING = 3;

        public static async Task TurnOffLedsAsync()
        {
            try
            {
                foreach (string path in FindControllers())
                {
                    try { await TurnOffControllerAsync(path).ConfigureAwait(false); }
                    catch { }
                }
            }
            catch { }
        }

        private static IEnumerable<string> FindControllers()
        {
            using ManagementObjectSearcher searcher = new(
                "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'HID\\\\VID_0CF2&PID_A102%'");

            foreach (ManagementBaseObject device in searcher.Get())
            {
                string? id = device["PNPDeviceID"] as string;
                if (id == null || id.Contains("COL", StringComparison.OrdinalIgnoreCase)) continue;

                yield return @"\\?\" + id.Replace('\\', '#') + "#{" + HidGuid + "}";
            }
        }

        private static async Task TurnOffControllerAsync(string path)
        {
            using SafeFileHandle handle = CreateFile(path, GENERIC_READ_WRITE, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) return;

            int featureLength = GetReportLength(handle);

            // Take the LEDs back from the motherboard ARGB header so the controller obeys us.
            await SendFeatureAsync(handle, featureLength, new byte[] { ReportId, ConfigCommand, ArgbSyncRegister, 0x00, 0, 0, 0 }).ConfigureAwait(false);

            // Blank every port: effect-only report with L-Connect's "Off" brightness.
            for (int port = PortCount - 1; port >= 0; port--)
            {
                byte[] blank = { ReportId, (byte)(ConfigCommand | (port & 0x0F)), BlankWireMode, 0x00, 0x00, BrightnessOff, 0 };
                await SendFeatureAsync(handle, featureLength, blank).ConfigureAwait(false);
            }

            // Latch the frame so the blanked look is displayed.
            await SendFeatureAsync(handle, featureLength, new byte[] { ReportId, 0x60, 0x00, 0x01, 0, 0, 0 }).ConfigureAwait(false);
        }

        private static async Task SendFeatureAsync(SafeFileHandle handle, int featureLength, byte[] report)
        {
            foreach (int length in CandidateLengths(featureLength))
            {
                byte[] buffer = new byte[length];
                Array.Copy(report, buffer, Math.Min(report.Length, length));
                if (HidD_SetFeature(handle, buffer, (uint)length)) break;
                await Task.Delay(150).ConfigureAwait(false);
            }

            await Task.Delay(150).ConfigureAwait(false);
        }

        private static IEnumerable<int> CandidateLengths(int featureLength)
        {
            if (featureLength >= 7) yield return featureLength;
            yield return 65;
            yield return 7;
        }

        private static int GetReportLength(SafeFileHandle handle)
        {
            if (!HidD_GetPreparsedData(handle, out IntPtr preparsed)) return 0;
            try
            {
                HIDP_CAPS caps = default;
                if (HidP_GetCaps(preparsed, ref caps) < 0) return 0;
                return caps.FeatureReportByteLength;
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("hid.dll")]
        private static extern bool HidD_SetFeature(SafeFileHandle hFile, byte[] lpReportBuffer, uint ReportBufferLength);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle hFile, out IntPtr PreparsedData);

        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, ref HIDP_CAPS Capabilities);
    }
}
