using System.Text.Json;

#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace BestCrush.Services;

public enum OverlayLayoutKind
{
    Profitability,
    Market,
    Crush,
    ControlBar
}

public sealed record OverlayWindowLayout(
    int X,
    int Y,
    int Width,
    int Height
);

public sealed class OverlayLayoutSettingsService
{
    private const string SettingsFileName =
        "overlay-layout.json";

    private readonly object _sync =
        new();

    private readonly string _settingsPath;

    private Dictionary<
        OverlayLayoutKind,
        OverlayWindowLayout>
        _layouts;

    public OverlayLayoutSettingsService()
    {
        string directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData
                ),
                "BestCrush",
                "Settings"
            );

        Directory.CreateDirectory(
            directory
        );

        _settingsPath =
            Path.Combine(
                directory,
                SettingsFileName
            );

        _layouts =
            LoadLayouts();
    }

    public OverlayWindowLayout GetDefaultLayout(
        OverlayLayoutKind kind)
    {
        return kind switch
        {
            OverlayLayoutKind.Profitability =>
                new OverlayWindowLayout(
                    40,
                    70,
                    340,
                    432
                ),

            OverlayLayoutKind.Market =>
                new OverlayWindowLayout(
                    395,
                    70,
                    330,
                    300
                ),

            OverlayLayoutKind.Crush =>
                new OverlayWindowLayout(
                    750,
                    80,
                    390,
                    520
                ),

            OverlayLayoutKind.ControlBar =>
                new OverlayWindowLayout(
                    210,
                    5,
                    205,
                    50
                ),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(kind)
                )
        };
    }

    public OverlayWindowLayout GetLayout(
        OverlayLayoutKind kind)
    {
        lock (_sync)
        {
            return _layouts.TryGetValue(
                kind,
                out OverlayWindowLayout? layout
            )
                ? layout
                : GetDefaultLayout(
                    kind
                );
        }
    }

    public OverlayWindowLayout
        GetValidatedLayout(
            OverlayLayoutKind kind,
            int minimumWidth,
            int minimumHeight,
            bool allowResize)
    {
        OverlayWindowLayout layout =
            GetLayout(
                kind
            );

        if (!allowResize)
        {
            OverlayWindowLayout defaults =
                GetDefaultLayout(
                    kind
                );

            layout =
                layout with
                {
                    Width =
                        defaults.Width,

                    Height =
                        defaults.Height
                };
        }

        OverlayWindowLayout constrained =
            ConstrainToVisibleScreen(
                layout,
                minimumWidth,
                minimumHeight
            );

        if (constrained != layout)
        {
            SaveLayout(
                kind,
                constrained
            );
        }

        return constrained;
    }

    public void SaveLayout(
        OverlayLayoutKind kind,
        OverlayWindowLayout layout)
    {
        lock (_sync)
        {
            _layouts[kind] =
                layout;

            PersistLocked();
        }
    }

    public void ResetAll()
    {
        lock (_sync)
        {
            _layouts =
                CreateDefaultLayouts();

            PersistLocked();
        }
    }

    public OverlayWindowLayout
        ConstrainToVisibleScreen(
            OverlayWindowLayout layout,
            int minimumWidth,
            int minimumHeight)
    {
#if WINDOWS
        NativeRect proposed =
            new()
            {
                Left =
                    layout.X,

                Top =
                    layout.Y,

                Right =
                    layout.X +
                    Math.Max(
                        1,
                        layout.Width
                    ),

                Bottom =
                    layout.Y +
                    Math.Max(
                        1,
                        layout.Height
                    )
            };

        IntPtr monitor =
            MonitorFromRect(
                ref proposed,
                MonitorDefaultToNearest
            );

        if (monitor ==
            IntPtr.Zero)
        {
            return layout;
        }

        MonitorInfo monitorInfo =
            new()
            {
                Size =
                    Marshal.SizeOf<
                        MonitorInfo>()
            };

        if (!GetMonitorInfo(
            monitor,
            ref monitorInfo))
        {
            return layout;
        }

        NativeRect work =
            monitorInfo.Work;

        int workWidth =
            Math.Max(
                1,
                work.Right -
                work.Left
            );

        int workHeight =
            Math.Max(
                1,
                work.Bottom -
                work.Top
            );

        int effectiveMinimumWidth =
            Math.Min(
                Math.Max(
                    1,
                    minimumWidth
                ),
                workWidth
            );

        int effectiveMinimumHeight =
            Math.Min(
                Math.Max(
                    1,
                    minimumHeight
                ),
                workHeight
            );

        int width =
            Math.Clamp(
                layout.Width,
                effectiveMinimumWidth,
                workWidth
            );

        int height =
            Math.Clamp(
                layout.Height,
                effectiveMinimumHeight,
                workHeight
            );

        int maximumX =
            work.Right -
            width;

        int maximumY =
            work.Bottom -
            height;

        int x =
            Math.Clamp(
                layout.X,
                work.Left,
                maximumX
            );

        int y =
            Math.Clamp(
                layout.Y,
                work.Top,
                maximumY
            );

        return new OverlayWindowLayout(
            x,
            y,
            width,
            height
        );
#else
        return layout with
        {
            Width =
                Math.Max(
                    minimumWidth,
                    layout.Width
                ),

            Height =
                Math.Max(
                    minimumHeight,
                    layout.Height
                )
        };
#endif
    }

    private Dictionary<
        OverlayLayoutKind,
        OverlayWindowLayout>
        LoadLayouts()
    {
        Dictionary<
            OverlayLayoutKind,
            OverlayWindowLayout>
            result =
                CreateDefaultLayouts();

        try
        {
            if (!File.Exists(
                _settingsPath))
            {
                return result;
            }

            string json =
                File.ReadAllText(
                    _settingsPath
                );

            Dictionary<
                string,
                OverlayWindowLayout>?
                stored =
                    JsonSerializer
                        .Deserialize<
                            Dictionary<
                                string,
                                OverlayWindowLayout>>(
                            json
                        );

            if (stored is null)
            {
                return result;
            }

            foreach (
                KeyValuePair<
                    string,
                    OverlayWindowLayout>
                entry
                in stored)
            {
                if (Enum.TryParse(
                    entry.Key,
                    ignoreCase: true,
                    out OverlayLayoutKind
                        kind))
                {
                    result[kind] =
                        entry.Value;
                }
            }
        }
        catch
        {
            // Un fichier de layout corrompu ne doit
            // jamais empêcher BestCrush de démarrer.
        }

        return result;
    }

    private void PersistLocked()
    {
        try
        {
            string? directory =
                Path.GetDirectoryName(
                    _settingsPath
                );

            if (!string.IsNullOrWhiteSpace(
                directory))
            {
                Directory.CreateDirectory(
                    directory
                );
            }

            Dictionary<
                string,
                OverlayWindowLayout>
                serializable =
                    _layouts
                        .ToDictionary(
                            entry =>
                                entry.Key
                                    .ToString(),

                            entry =>
                                entry.Value,

                            StringComparer
                                .Ordinal
                        );

            string json =
                JsonSerializer
                    .Serialize(
                        serializable,
                        new JsonSerializerOptions
                        {
                            WriteIndented =
                                true
                        }
                    );

            string temporaryPath =
                _settingsPath +
                ".tmp";

            File.WriteAllText(
                temporaryPath,
                json
            );

            File.Move(
                temporaryPath,
                _settingsPath,
                overwrite: true
            );
        }
        catch
        {
            // Une préférence de fenêtre ne doit
            // jamais interrompre BestCrush.
        }
    }

    private Dictionary<
        OverlayLayoutKind,
        OverlayWindowLayout>
        CreateDefaultLayouts()
    {
        return new Dictionary<
            OverlayLayoutKind,
            OverlayWindowLayout>
        {
            [
                OverlayLayoutKind
                    .Profitability
            ] =
                GetDefaultLayout(
                    OverlayLayoutKind
                        .Profitability
                ),

            [
                OverlayLayoutKind
                    .Market
            ] =
                GetDefaultLayout(
                    OverlayLayoutKind
                        .Market
                ),

            [
                OverlayLayoutKind
                    .Crush
            ] =
                GetDefaultLayout(
                    OverlayLayoutKind
                        .Crush
                ),

            [
                OverlayLayoutKind
                    .ControlBar
            ] =
                GetDefaultLayout(
                    OverlayLayoutKind
                        .ControlBar
                )
        };
    }

#if WINDOWS
    private const uint
        MonitorDefaultToNearest =
            0x00000002;

    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr
        MonitorFromRect(
            ref NativeRect rectangle,
            uint flags
        );

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Auto)]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        GetMonitorInfo(
            IntPtr monitor,
            ref MonitorInfo monitorInfo
        );
#endif
}