using BestCrush.Overlay;
#if WINDOWS
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Windows.Graphics;
#endif

namespace BestCrush.Services;

public sealed record OverlayControlBarBindings(
    Func<bool> IsProfitabilityVisible,
    Action ToggleProfitability,
    Func<bool> IsMarketVisible,
    Action ToggleMarket,
    Func<bool> IsCrushVisible,
    Action ToggleCrush,
    Action OpenSettings
);

public sealed class OverlayControlBarService
{
    private Window? _window;
    private OverlayControlBarPage? _page;
    private OverlayControlBarBindings? _bindings;

#if WINDOWS
    private AppWindow? _appWindow;
    private IntPtr _hwnd = IntPtr.Zero;

    private int _currentX = 40;
    private int _currentY = 10;
    private int _currentWidth = 205;
    private int _currentHeight = 50;

    private int _dragStartX;
    private int _dragStartY;

    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const uint LwaAlpha = 0x00000002;
    private const int SwShowNoActivate = 4;
#endif

    public bool IsProfitabilityVisible =>
        _bindings?.IsProfitabilityVisible() ?? false;

    public bool IsMarketVisible =>
        _bindings?.IsMarketVisible() ?? false;

    public bool IsCrushVisible =>
        _bindings?.IsCrushVisible() ?? false;

    public void Initialize(
        OverlayControlBarBindings bindings)
    {
        _bindings = bindings;

        if (_window is not null)
        {
            _page?.RefreshState();
            return;
        }

        OverlayControlBarPage page =
            new(this);

        Window window = new(page)
        {
            Title = "BestCrush",
            Width = 205,
            Height = 50,
            X = 40,
            Y = 10
        };

        _page = page;
        _window = window;

        window.Created += (_, _) =>
        {
#if WINDOWS
            ConfigureWindow(window);
#endif
        };

        window.Destroying += (_, _) =>
        {
            if (!ReferenceEquals(
                _window,
                window))
            {
                return;
            }

            _window = null;
            _page = null;

#if WINDOWS
            _hwnd = IntPtr.Zero;
            _appWindow = null;
#endif
        };

        Application.Current?.OpenWindow(window);
    }

    public void ToggleProfitability()
    {
        _bindings?.ToggleProfitability();
        _page?.RefreshState();
    }

    public void ToggleMarket()
    {
        _bindings?.ToggleMarket();
        _page?.RefreshState();
    }

    public void ToggleCrush()
    {
        _bindings?.ToggleCrush();
        _page?.RefreshState();
    }

    public void OpenSettings()
    {
        _bindings?.OpenSettings();
    }

    public void Shutdown()
    {
        if (_window is null)
        {
            return;
        }

        Window window = _window;
        _window = null;
        _page = null;
        _bindings = null;

#if WINDOWS
        _hwnd = IntPtr.Zero;
        _appWindow = null;
#endif

        Application.Current?.CloseWindow(window);
    }

    public bool ContainsScreenPoint(
        int x,
        int y)
    {
#if WINDOWS
        if (_window is null)
        {
            return false;
        }

        return
            x >= _currentX &&
            y >= _currentY &&
            x < _currentX + _currentWidth &&
            y < _currentY + _currentHeight;
#else
        return false;
#endif
    }

    public void BeginDrag()
    {
#if WINDOWS
        _dragStartX = _currentX;
        _dragStartY = _currentY;
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

#if WINDOWS
    private void ConfigureWindow(
        Window window)
    {
        if (window.Handler?.PlatformView
            is not Microsoft.UI.Xaml.Window nativeWindow)
        {
            return;
        }

        _hwnd =
            WinRT.Interop.WindowNative
                .GetWindowHandle(nativeWindow);

        Microsoft.UI.WindowId windowId =
            Microsoft.UI.Win32Interop
                .GetWindowIdFromWindow(_hwnd);

        _appWindow =
            AppWindow.GetFromWindowId(windowId);

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
                _currentWidth,
                _currentHeight
            )
        );

        _appWindow.Move(
            new PointInt32(
                _currentX,
                _currentY
            )
        );

        MakeTransparent(
            _hwnd,
            240
        );

        ShowWindow(
            _hwnd,
            SwShowNoActivate
        );
    }

    private static void MakeTransparent(
        IntPtr hwnd,
        byte alpha)
    {
        long exStyle =
            GetWindowLongPtr(
                hwnd,
                GwlExStyle
            ).ToInt64();

        SetWindowLongPtr(
            hwnd,
            GwlExStyle,
            new IntPtr(
                exStyle |
                WsExLayered
            )
        );

        SetLayeredWindowAttributes(
            hwnd,
            0,
            alpha,
            LwaAlpha
        );
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow
    );

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLongPtr"
    )]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex
    );

    [DllImport(
        "user32.dll",
        EntryPoint = "GetWindowLong"
    )]
    private static extern IntPtr GetWindowLongPtr32(
        IntPtr hWnd,
        int nIndex
    );

    private static IntPtr GetWindowLongPtr(
        IntPtr hWnd,
        int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(
                hWnd,
                nIndex
            )
            : GetWindowLongPtr32(
                hWnd,
                nIndex
            );
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLongPtr"
    )]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr hWnd,
        int nIndex,
        IntPtr dwNewLong
    );

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowLong"
    )]
    private static extern IntPtr SetWindowLongPtr32(
        IntPtr hWnd,
        int nIndex,
        IntPtr dwNewLong
    );

    private static IntPtr SetWindowLongPtr(
        IntPtr hWnd,
        int nIndex,
        IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(
                hWnd,
                nIndex,
                dwNewLong
            )
            : SetWindowLongPtr32(
                hWnd,
                nIndex,
                dwNewLong
            );
    }

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr hwnd,
        uint crKey,
        byte bAlpha,
        uint dwFlags
    );
#endif
}
