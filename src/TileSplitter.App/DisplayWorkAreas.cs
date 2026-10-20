using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace TileSplitter.App;

internal static class DisplayWorkAreas
{
    public static IReadOnlyList<Rect> Get(Matrix fromDevice)
    {
        var areas = new List<Rect>();
        MonitorCallback callback = (nint monitor, nint device, ref NativeRect rectangle, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
                var bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
                areas.Add(new Rect(topLeft, bottomRight));
            }

            return true;
        };

        EnumDisplayMonitors(0, 0, callback, 0);
        if (areas.Count == 0)
        {
            areas.Add(SystemParameters.WorkArea);
        }

        return areas;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool MonitorCallback(nint monitor, nint device, ref NativeRect rectangle, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint device, nint clip, MonitorCallback callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
