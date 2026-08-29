#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using System.Runtime.InteropServices;
#endif

using BestCrush.Overlay;
using Microsoft.Maui.ApplicationModel;

namespace BestCrush.Services;

public sealed record CrushSessionRuneLine(
    string Name,
    int Quantity,
    double? Value
);

public sealed record CrushSessionSnapshot(
    bool IsRunning,
    int ScannedCells,
    int IdleCaptures,
    int? LastCursorX,
    int? LastCursorY,
    IReadOnlyList<CrushSessionRuneLine> Runes,
    double? TotalValue
);

public sealed class CrushSessionService(
    DofusWindowService dofusWindowService,
    DofusCaptureService dofusCaptureService)
{
    private Window? _window;
    private CrushSessionOverlayPage? _page;

    private bool _isRunning;
    private CancellationTokenSource?
    _mouseMonitorCancellation;

    private Task? _mouseMonitorTask;

    private int _idleCaptureCount;

    private int? _lastCursorX;
    private int? _lastCursorY;

    public bool IsRunning =>
        _isRunning;

    #if WINDOWS
    private AppWindow? _appWindow;

    private int _currentX = 400;
    private int _currentY = 80;

    private int _dragStartX;
    private int _dragStartY;

    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const uint LwaAlpha = 0x00000002;

    private const int MousePollMilliseconds = 40;
    private const int MouseStableMilliseconds = 220;
    private const int MouseMovementThreshold = 4;
    #endif
    public void Toggle()
    {
        if (_isRunning)
        {
            Stop();
            return;
        }

        StartNew();
    }

    public void StartNew()
    {
        ResetInternal();

        _isRunning = true;

        EnsureWindow();

        _page?.Update(
            CreateSnapshot()
        );

        StartMouseMonitoring();
    }

    public void Stop()
    {
        _isRunning = false;

        StopMouseMonitoring();

        _page?.Update(
            CreateSnapshot()
        );
    } 

    public void CloseAndReset()
    {
        _isRunning = false;

        ResetInternal();

        if (_window is null)
        {
            return;
        }

        Window window =
            _window;

        _window = null;
        _page = null;

        Application.Current?
            .CloseWindow(
                window
            );
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        CrushSessionOverlayPage page =
            new(
                this
            );

        Window window =
            new(page)
            {
                Title =
                    "BestCrush — Résultat concassage",

                Width = 390,
                Height = 520,

                X = 400,
                Y = 80
            };
            window.Created +=
                (_, _) =>
                {
            #if WINDOWS
                    if (window.Handler?.PlatformView
                        is not Microsoft.UI.Xaml.Window nativeWindow)
                    {
                        return;
                    }

                    IntPtr hwnd =
                        WinRT.Interop.WindowNative
                            .GetWindowHandle(
                                nativeWindow
                            );

                    Microsoft.UI.WindowId windowId =
                        Microsoft.UI.Win32Interop
                            .GetWindowIdFromWindow(
                                hwnd
                            );

                    _appWindow =
                        AppWindow.GetFromWindowId(
                            windowId
                        );

                    if (_appWindow.Presenter
                        is OverlappedPresenter presenter)
                    {
                        presenter.IsAlwaysOnTop = true;

                        presenter.IsResizable = false;
                        presenter.IsMaximizable = false;
                        presenter.IsMinimizable = false;

                        presenter.SetBorderAndTitleBar(
                            false,
                            false
                        );
                    }

                    _appWindow.Resize(
                        new SizeInt32(
                            390,
                            520
                        )
                    );

                    _appWindow.Move(
                        new PointInt32(
                            _currentX,
                            _currentY
                        )
                    );

                    MakeTransparent(
                        hwnd,
                        225
                    );
            #endif
                };

        window.Destroying +=
            (_, _) =>
            {
                _isRunning = false;

                ResetInternal();

                _window = null;
                _page = null;
            };

        _page = page;
        _window = window;

        Application.Current?
            .OpenWindow(
                window
            );
    }

    private void ResetInternal()
    {
        StopMouseMonitoring();

        _idleCaptureCount = 0;

        _lastCursorX = null;
        _lastCursorY = null;
    }

    public void BeginDrag()
    {
    #if WINDOWS
        _dragStartX =
            _currentX;

        _dragStartY =
            _currentY;
    #endif
    }

    public void Drag(
        double totalX,
        double totalY)
    {
    #if WINDOWS
        if (_appWindow is null)
        {
            return;
        }

        int newX =
            _dragStartX +
            (int)Math.Round(totalX);

        int newY =
            _dragStartY +
            (int)Math.Round(totalY);

        _appWindow.Move(
            new PointInt32(
                newX,
                newY
            )
        );

        _currentX = newX;
        _currentY = newY;
    #endif
    }

    private CrushSessionSnapshot
        CreateSnapshot()
    {
        return new CrushSessionSnapshot(
            _isRunning,
            0,
            _idleCaptureCount,
            _lastCursorX,
            _lastCursorY,
            [],
            null
        );
    }

    private void StartMouseMonitoring()
    {
    #if WINDOWS
        StopMouseMonitoring();

        _mouseMonitorCancellation =
            new CancellationTokenSource();

        _mouseMonitorTask =
            MonitorMouseAsync(
                _mouseMonitorCancellation.Token
            );
    #endif
    }

    private async Task TryCaptureAtCursorAsync(
    #if WINDOWS
        WinPoint cursor,
    #else
        object cursor,
    #endif
        CancellationToken cancellationToken)
    {
    #if WINDOWS
        DofusWindowInfo? dofusWindow =
            dofusWindowService
                .GetActiveDofusWindow();

        if (dofusWindow is null)
        {
            return;
        }

        // Curseur hors fenêtre Dofus :
        // aucune capture.
        if (cursor.X < dofusWindow.X ||
            cursor.Y < dofusWindow.Y ||
            cursor.X >=
                dofusWindow.X +
                dofusWindow.Width ||
            cursor.Y >=
                dofusWindow.Y +
                dofusWindow.Height)
        {
            return;
        }

        // On ignore également notre propre
        // overlay Résultat concassage.
        if (_window is not null &&
            cursor.X >= _currentX &&
            cursor.X < _currentX + 390 &&
            cursor.Y >= _currentY &&
            cursor.Y < _currentY + 520)
        {
            return;
        }

        DofusCaptureResult capture =
            await dofusCaptureService
                .CaptureAsync(
                    dofusWindow,
                    cancellationToken
                );

        // Conversion position écran
        // → position dans l'image capturée.
        double relativeX =
            (cursor.X - dofusWindow.X) /
            (double)dofusWindow.Width;

        double relativeY =
            (cursor.Y - dofusWindow.Y) /
            (double)dofusWindow.Height;

        int captureX =
            (int)Math.Round(
                relativeX *
                capture.Width
            );

        int captureY =
            (int)Math.Round(
                relativeY *
                capture.Height
            );

        captureX =
            Math.Clamp(
                captureX,
                0,
                capture.Width - 1
            );

        captureY =
            Math.Clamp(
                captureY,
                0,
                capture.Height - 1
            );

        _idleCaptureCount++;

        _lastCursorX =
            captureX;

        _lastCursorY =
            captureY;

        await MainThread
            .InvokeOnMainThreadAsync(
                () =>
                {
                    _page?.Update(
                        CreateSnapshot()
                    );
                }
            );
    #endif
    }

    private void StopMouseMonitoring()
    {
    #if WINDOWS
        if (_mouseMonitorCancellation is null)
        {
            return;
        }

        try
        {
            _mouseMonitorCancellation.Cancel();
        }
        catch
        {
            // Rien.
        }

        _mouseMonitorCancellation.Dispose();
        _mouseMonitorCancellation = null;

        _mouseMonitorTask = null;
    #endif
    }

    private async Task MonitorMouseAsync(
        CancellationToken cancellationToken)
    {
    #if WINDOWS
        WinPoint? referencePosition =
            null;

        DateTime stationarySince =
            DateTime.UtcNow;

        bool capturedForCurrentStop =
            false;

        while (!cancellationToken
            .IsCancellationRequested)
        {
            try
            {
                if (!GetCursorPos(
                    out WinPoint cursor))
                {
                    await Task.Delay(
                        MousePollMilliseconds,
                        cancellationToken
                    );

                    continue;
                }

                if (referencePosition is null)
                {
                    referencePosition =
                        cursor;

                    stationarySince =
                        DateTime.UtcNow;

                    capturedForCurrentStop =
                        false;
                }
                else
                {
                    int dx =
                        cursor.X -
                        referencePosition.Value.X;

                    int dy =
                        cursor.Y -
                        referencePosition.Value.Y;

                    int distanceSquared =
                        dx * dx +
                        dy * dy;

                    if (distanceSquared >
                        MouseMovementThreshold *
                        MouseMovementThreshold)
                    {
                        // La souris a réellement bougé.
                        referencePosition =
                            cursor;

                        stationarySince =
                            DateTime.UtcNow;

                        capturedForCurrentStop =
                            false;
                    }
                    else if (
                        !capturedForCurrentStop &&
                        DateTime.UtcNow -
                            stationarySince >=
                        TimeSpan.FromMilliseconds(
                            MouseStableMilliseconds
                        ))
                    {
                        // On marque immédiatement cet arrêt
                        // comme traité pour ne jamais capturer
                        // en boucle si l'utilisateur reste immobile.
                        capturedForCurrentStop =
                            true;

                        await TryCaptureAtCursorAsync(
                            cursor,
                            cancellationToken
                        );
                    }
                }

                await Task.Delay(
                    MousePollMilliseconds,
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Une erreur ponctuelle ne doit pas
                // tuer toute la session F9.
                await Task.Delay(
                    100,
                    cancellationToken
                );
            }
        }
    #endif
    }

    #if WINDOWS
    private static void MakeTransparent(
        IntPtr hwnd,
        byte opacity)
    {
        nint style =
            GetWindowLongPtr(
                hwnd,
                GwlExStyle
            );

        SetWindowLongPtr(
            hwnd,
            GwlExStyle,
            style | (nint)WsExLayered
        );

        SetLayeredWindowAttributes(
            hwnd,
            0,
            opacity,
            LwaAlpha
        );
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool GetCursorPos(
        out WinPoint point
    );

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtrW"
    )]
    private static extern nint
        GetWindowLongPtr(
            IntPtr hwnd,
            int index
        );

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtrW"
    )]
    private static extern nint
        SetWindowLongPtr(
            IntPtr hwnd,
            int index,
            nint newStyle
        );

    [DllImport("user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        SetLayeredWindowAttributes(
            IntPtr hwnd,
            uint colorKey,
            byte alpha,
            uint flags
        );
    #endif
}