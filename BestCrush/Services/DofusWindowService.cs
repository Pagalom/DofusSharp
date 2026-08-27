using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BestCrush.Services;

public sealed class DofusWindowService
{
    public DofusWindowInfo? GetActiveDofusWindow()
    {
#if WINDOWS
        List<DofusWindowInfo> windows = GetDofusWindows();

        if (windows.Count == 0)
        {
            return null;
        }

        IntPtr foregroundWindow = GetForegroundWindow();

        DofusWindowInfo? foregroundDofus =
            windows.FirstOrDefault(
                window => window.Handle == foregroundWindow
            );

        return foregroundDofus ?? windows[0];
#else
        return null;
#endif
    }

#if WINDOWS
    private static List<DofusWindowInfo> GetDofusWindows()
    {
        List<DofusWindowInfo> result = [];

        EnumWindows(
            (hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd))
                {
                    return true;
                }

                GetWindowThreadProcessId(
                    hwnd,
                    out uint processId
                );

                if (processId == 0)
                {
                    return true;
                }

                try
                {
                    using Process process =
                        Process.GetProcessById(
                            (int)processId
                        );

                    string processName =
                        process.ProcessName;

                    if (!processName.Contains(
                        "dofus",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    string title =
                        GetWindowTitle(hwnd);

                    if (!GetWindowRect(
                        hwnd,
                        out Rect rect))
                    {
                        return true;
                    }

                    result.Add(
                        new DofusWindowInfo(
                            hwnd,
                            title,
                            processName,
                            rect.Left,
                            rect.Top,
                            rect.Right - rect.Left,
                            rect.Bottom - rect.Top
                        )
                    );
                }
                catch
                {
                    // Le processus a pu disparaître
                    // entre l'énumération et sa lecture.
                }

                return true;
            },
            IntPtr.Zero
        );

        return result;
    }

    private static string GetWindowTitle(
        IntPtr hwnd)
    {
        int length =
            GetWindowTextLength(hwnd);

        if (length == 0)
        {
            return "";
        }

        StringBuilder builder =
            new(length + 1);

        GetWindowText(
            hwnd,
            builder,
            builder.Capacity
        );

        return builder.ToString();
    }

    private delegate bool EnumWindowsProc(
        IntPtr hwnd,
        IntPtr lParam
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(
        EnumWindowsProc enumProc,
        IntPtr lParam
    );

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(
        IntPtr hwnd
    );

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hwnd,
        out uint processId
    );

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode
    )]
    private static extern int GetWindowText(
        IntPtr hwnd,
        StringBuilder text,
        int maxCount
    );

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode
    )]
    private static extern int GetWindowTextLength(
        IntPtr hwnd
    );

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(
        IntPtr hwnd,
        out Rect rect
    );
#endif
}

public sealed record DofusWindowInfo(
    IntPtr Handle,
    string Title,
    string ProcessName,
    int X,
    int Y,
    int Width,
    int Height
);