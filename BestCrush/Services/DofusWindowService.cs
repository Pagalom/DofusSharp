using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BestCrush.Services;

public sealed class DofusWindowService
{
    /// <summary>
    /// All currently visible Dofus windows. Order reflects Win32 Z-order,
    /// NOT the moment of process or window creation.
    /// </summary>
    public IReadOnlyList<DofusWindowInfo> ListVisibleDofusWindows()
    {
#if WINDOWS
        return GetDofusWindows();
#else
        return [];
#endif
    }

    /// <summary>
    /// Strictly the foreground Dofus window, or null if another app has
    /// focus. Never fall back to an arbitrary window for client selection.
    /// </summary>
    public DofusWindowInfo? GetForegroundDofusWindow()
    {
#if WINDOWS
        IntPtr foreground = GetForegroundWindow();
        return GetDofusWindows().FirstOrDefault(window => window.Handle == foreground);
#else
        return null;
#endif
    }

    /// <summary>
    /// Strict target for a pointer event: require the actual top-level
    /// Windows hit-test result, never the foreground or first Dofus client.
    /// Unconnected to middle-click until the remaining routing checks pass.
    /// </summary>
    public DofusWindowInfo? GetDofusWindowAtScreenPoint(int x, int y)
    {
#if WINDOWS
        IntPtr hit = WindowFromPoint(new ScreenPoint(x, y));
        if (hit == IntPtr.Zero)
            return null;

        IntPtr topLevel = GetAncestor(hit, 2);
        return ResolvePointerTarget(GetDofusWindows(), topLevel, x, y);
#else
        return null;
#endif
    }

    internal static DofusWindowInfo? ResolvePointerTarget(
        IReadOnlyList<DofusWindowInfo> windows,
        IntPtr topLevelHandle, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (topLevelHandle == IntPtr.Zero)
            return null;

        DofusWindowInfo[] matches = windows
            .Where(w => w.Handle == topLevelHandle &&
                w.ProcessId > 0 && w.Width > 0 && w.Height > 0 &&
                (long)x >= w.X && (long)x < (long)w.X + w.Width &&
                (long)y >= w.Y && (long)y < (long)w.Y + w.Height)
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

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
                        ) { ProcessId = (int)processId }
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

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ScreenPoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

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
)
{
    public int ProcessId { get; init; }
}