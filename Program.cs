using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MonitorBrightness
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length == 0) return;

            using var controller = new PhisicalMonitorBrightnessController();

            //Console.WriteLine(controller.Monitors.Count);

            if (args[0].StartsWith("-"))
            {
                if (args[0] == "-darker")
                {
                    controller.Set((uint)(Math.Max(controller.Get() - 20, 0)));
                }
                else if (args[0] == "-brighter")
                {
                    controller.Set((uint)(Math.Min(controller.Get() + 20, 100)));
                }
            }
            else if (uint.TryParse(args[0], out var value) && value <= 100)
            {
                controller.Set(value);
            }
        }
    }

    // https://stackoverflow.com/questions/4013622/adjust-screen-brightness-using-c-sharp
    public class PhisicalMonitorBrightnessController : IDisposable
    {
        #region DllImport
        [DllImport("dxva2.dll", EntryPoint = "GetNumberOfPhysicalMonitorsFromHMONITOR")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint pdwNumberOfPhysicalMonitors);

        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("dxva2.dll", EntryPoint = "GetMonitorBrightness")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorBrightness(IntPtr handle, ref uint minimumBrightness, ref uint currentBrightness, ref uint maxBrightness);

        [DllImport("dxva2.dll", EntryPoint = "SetMonitorBrightness")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetMonitorBrightness(IntPtr handle, uint newBrightness);

        // TODO: Add license or rewrite
        // Copied from https://github.com/oysteinkrog/MonitorControl/blob/master/MonitorControl/Interop/Dxva2.cs
        [DllImport("dxva2.dll", ExactSpelling = true, SetLastError = true, PreserveSig = false)]
        public static extern void GetMonitorContrast(IntPtr hMonitor, [Out] out uint pdwMinimumContrast, [Out] out uint pdwCurrentContrast, [Out] out uint pdwMaximumContrast);

        [DllImport("dxva2.dll", ExactSpelling = true, SetLastError = true, PreserveSig = false)]
        public static extern void GetMonitorRedGreenOrBlueDrive(IntPtr hMonitor, MC_DRIVE_TYPE dtDriveType, [Out] out uint pdwMinimumDrive, [Out] out uint pdwCurrentDrive, [Out] out uint pdwMaximumDrive);

        [DllImport("dxva2.dll", ExactSpelling = true, SetLastError = true, PreserveSig = false)]
        public static extern void SetMonitorContrast(IntPtr hMonitor, uint dwNewContrast);

        [DllImport("dxva2.dll", ExactSpelling = true, SetLastError = true, PreserveSig = false)]
        public static extern void SetMonitorRedGreenOrBlueDrive(IntPtr hMonitor, MC_DRIVE_TYPE dtDriveType, uint dwNewDrive);




        [DllImport("dxva2.dll", EntryPoint = "DestroyPhysicalMonitor")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

        [DllImport("dxva2.dll", EntryPoint = "DestroyPhysicalMonitors")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);
        delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);
        #endregion

        public IReadOnlyCollection<MonitorInfo> Monitors { get; set; }

        public PhisicalMonitorBrightnessController()
        {
            UpdateMonitors();
        }

        #region Get & Set
        public void Set(uint brightness)
        {
            Set(brightness, true);
        }

        private void Set(uint brightness, bool refreshMonitorsIfNeeded)
        {
            bool isSomeFail = false;
            foreach (var monitor in Monitors)
            {
                uint realNewValue = (monitor.MaxValue - monitor.MinValue) * brightness / 100 + monitor.MinValue;
                if (SetMonitorBrightness(monitor.Handle, realNewValue))
                {
                    monitor.CurrentValue = realNewValue;
                }
                else if (refreshMonitorsIfNeeded)
                {
                    isSomeFail = true;
                    break;
                }
            }

            if (refreshMonitorsIfNeeded && (isSomeFail || !Monitors.Any()))
            {
                UpdateMonitors();
                Set(brightness, false);
                return;
            }
        }

        public int Get()
        {
            if (!Monitors.Any())
            {
                return -1;
            }
            return (int)Monitors.Average(d => d.CurrentValue);
        }
        #endregion

        private void UpdateMonitors()
        {
            DisposeMonitors(this.Monitors);

            var monitors = new List<MonitorInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) =>
            {
                uint physicalMonitorsCount = 0;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref physicalMonitorsCount))
                {
                    // Cannot get monitor count
                    return true;
                }

                var physicalMonitors = new PHYSICAL_MONITOR[physicalMonitorsCount];
                if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, physicalMonitorsCount, physicalMonitors))
                {
                    // Cannot get phisical monitor handle
                    return true;
                }

                foreach (PHYSICAL_MONITOR physicalMonitor in physicalMonitors)
                {
                    uint minValue = 0, currentValue = 0, maxValue = 0;
                    if (!GetMonitorBrightness(physicalMonitor.hPhysicalMonitor, ref minValue, ref currentValue, ref maxValue))
                    {
                        DestroyPhysicalMonitor(physicalMonitor.hPhysicalMonitor);
                        continue;
                    }

                    var info = new MonitorInfo
                    {
                        Handle = physicalMonitor.hPhysicalMonitor,
                        MinValue = minValue,
                        CurrentValue = currentValue,
                        MaxValue = maxValue,
                    };
                    monitors.Add(info);

                    /*
                        Contrast 45
                        Color temp MC_COLOR_TEMPERATURE_6500K
                        Red drive 50
                        Green drive 50
                        Blue drive 50
                        Red gain 50
                        Green gain 50
                        Blue gain 50
                    */

                    // Slightly okay for making it slightly darker
                    // SetMonitorContrast(physicalMonitor.hPhysicalMonitor, 45);
                    // GetMonitorContrast(physicalMonitor.hPhysicalMonitor, out uint _, out uint contrast, out uint _);
                    //Console.WriteLine("Contrast " + contrast);

                    // Color temp does nothing

                    // Drive is what I want (it messes up font rendering if overdone)

                    // Gain does nothing

                    /*while (true)
                    {

                        Console.Write("Drive: ");
                        string? v = Console.ReadLine();
                        if (uint.TryParse(v, out uint result))
                        {
                            SetMonitorRedGreenOrBlueDrive(physicalMonitor.hPhysicalMonitor, MC_DRIVE_TYPE.MC_RED_DRIVE, result);
                            SetMonitorRedGreenOrBlueDrive(physicalMonitor.hPhysicalMonitor, MC_DRIVE_TYPE.MC_GREEN_DRIVE, result);
                            SetMonitorRedGreenOrBlueDrive(physicalMonitor.hPhysicalMonitor, MC_DRIVE_TYPE.MC_BLUE_DRIVE, result);

                            GetMonitorRedGreenOrBlueDrive(physicalMonitor.hPhysicalMonitor, MC_DRIVE_TYPE.MC_RED_DRIVE, out uint _, out uint drive, out uint _);
                            Console.WriteLine("Red drive " + drive);
                            GetMonitorRedGreenOrBlueDrive(physicalMonitor.hPhysicalMonitor, MC_DRIVE_TYPE.MC_GREEN_DRIVE, out uint _, out drive, out uint _);
                            Console.WriteLine("Green drive " + drive);
                            GetMonitorRedGreenOrBlueDrive(physicalMonitor.hPhysicalMonitor, MC_DRIVE_TYPE.MC_BLUE_DRIVE, out uint _, out drive, out uint _);
                            Console.WriteLine("Blue drive " + drive);
                        }
                        else
                        {
                            break;
                        }
                    }*/
                }

                return true;
            }, IntPtr.Zero);

            this.Monitors = monitors;
        }

        public void Dispose()
        {
            DisposeMonitors(Monitors);
            GC.SuppressFinalize(this);
        }

        private static void DisposeMonitors(IEnumerable<MonitorInfo> monitors)
        {
            if (monitors?.Any() == true)
            {
                PHYSICAL_MONITOR[] monitorArray = monitors.Select(m => new PHYSICAL_MONITOR { hPhysicalMonitor = m.Handle }).ToArray();
                DestroyPhysicalMonitors((uint)monitorArray.Length, monitorArray);
            }
        }

        #region Classes
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        public enum MC_DRIVE_TYPE
        {
            MC_RED_DRIVE,
            MC_GREEN_DRIVE,
            MC_BLUE_DRIVE
        }

        public class MonitorInfo
        {
            public uint MinValue { get; set; }
            public uint MaxValue { get; set; }
            public IntPtr Handle { get; set; }
            public uint CurrentValue { get; set; }
        }
        #endregion
    }
}