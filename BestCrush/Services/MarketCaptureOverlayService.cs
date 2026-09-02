using BestCrush.Overlay;
using Microsoft.Maui.ApplicationModel;
#if WINDOWS
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Windows.Graphics;
#endif

namespace BestCrush.Services;

public sealed class MarketCaptureOverlayService(
    OverlayLayoutSettingsService
        overlayLayoutSettingsService)
{
    private Window? _window;
    private MarketCaptureOverlayPage? _page;

    // Les captures peuvent mettre à jour leur diagnostic
    // sans provoquer l'ouverture de cette fenêtre.
    private Action<MarketCaptureOverlayPage>?
        _pendingUpdate;

#if WINDOWS
    private AppWindow? _appWindow;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isVisible;

    private int _currentX = 395;
    private int _currentY = 70;
    private int _currentWidth = 330;
    private int _currentHeight = 300;

    private int _dragStartX;
    private int _dragStartY;

    private int _resizeStartX;
    private int _resizeStartY;
    private int _resizeStartWidth;
    private int _resizeStartHeight;

    private OverlayResizeEdge
        _resizeEdge;

    private const int MinimumOverlayWidth = 260;
    private const int MinimumOverlayHeight = 180;

    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const uint LwaAlpha = 0x00000002;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
#endif

    public bool IsVisible
    {
        get
        {
#if WINDOWS
            return _window is not null && _isVisible;
#else
            return _window is not null;
#endif
        }
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void Show()
    {
        EnsureWindow();

#if WINDOWS
        _isVisible = true;

        if (_hwnd != IntPtr.Zero)
        {
            ShowWindow(
                _hwnd,
                SwShowNoActivate
            );

            if (_appWindow?.Presenter
                is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
            }
        }
#endif
    }

    public void Hide()
    {
#if WINDOWS
        _isVisible = false;

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(
            _hwnd,
            SwHide
        );
#endif
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

#if WINDOWS
        _hwnd = IntPtr.Zero;
        _appWindow = null;
        _isVisible = false;
#endif

        Application.Current?.CloseWindow(window);
    }

    public bool ContainsScreenPoint(
        int x,
        int y)
    {
#if WINDOWS
        if (!_isVisible)
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

        OverlayWindowLayout constrained =
            overlayLayoutSettingsService
                .ConstrainToVisibleScreen(
                    new OverlayWindowLayout(
                        _dragStartX +
                            (int)Math.Round(
                                totalX
                            ),
                        _dragStartY +
                            (int)Math.Round(
                                totalY
                            ),
                        _currentWidth,
                        _currentHeight
                    ),
                    MinimumOverlayWidth,
                    MinimumOverlayHeight
                );

        ApplyLayout(
            constrained
        );
#endif
    }

    public void EndDrag()
    {
#if WINDOWS
        SaveCurrentLayout();
#endif
    }

    public void BeginResize(
        OverlayResizeEdge edge)
    {
#if WINDOWS
        _resizeEdge =
            edge;

        _resizeStartX =
            _currentX;

        _resizeStartY =
            _currentY;

        _resizeStartWidth =
            _currentWidth;

        _resizeStartHeight =
            _currentHeight;
#endif
    }

    public void Resize(
        double totalX,
        double totalY)
    {
#if WINDOWS
        if (_appWindow is null)
        {
            return;
        }

        int dx =
            (int)Math.Round(
                totalX
            );

        int dy =
            (int)Math.Round(
                totalY
            );

        int newX =
            _resizeStartX;

        int newY =
            _resizeStartY;

        int newWidth =
            _resizeStartWidth;

        int newHeight =
            _resizeStartHeight;

        if (_resizeEdge.HasFlag(
            OverlayResizeEdge.Right))
        {
            newWidth =
                _resizeStartWidth +
                dx;
        }

        if (_resizeEdge.HasFlag(
            OverlayResizeEdge.Bottom))
        {
            newHeight =
                _resizeStartHeight +
                dy;
        }

        if (_resizeEdge.HasFlag(
            OverlayResizeEdge.Left))
        {
            newWidth =
                _resizeStartWidth -
                dx;

            newX =
                _resizeStartX +
                dx;
        }

        if (_resizeEdge.HasFlag(
            OverlayResizeEdge.Top))
        {
            newHeight =
                _resizeStartHeight -
                dy;

            newY =
                _resizeStartY +
                dy;
        }

        OverlayWindowLayout constrained =
            overlayLayoutSettingsService
                .ConstrainToVisibleScreen(
                    new OverlayWindowLayout(
                        newX,
                        newY,
                        newWidth,
                        newHeight
                    ),
                    MinimumOverlayWidth,
                    MinimumOverlayHeight
                );

        ApplyLayout(
            constrained
        );
#endif
    }

    public void EndResize()
    {
#if WINDOWS
        SaveCurrentLayout();
#endif
    }

    public void RestoreDefaultLayout()
    {
#if WINDOWS
        OverlayWindowLayout layout =
            overlayLayoutSettingsService
                .GetDefaultLayout(
                    OverlayLayoutKind
                        .Market
                );

        layout =
            overlayLayoutSettingsService
                .ConstrainToVisibleScreen(
                    layout,
                    MinimumOverlayWidth,
                    MinimumOverlayHeight
                );

        ApplyLayout(
            layout
        );

        SaveCurrentLayout();
#endif
    }

    public void ShowServerSelectionRequired() =>
        Update(
            page =>
                page.ShowServerSelectionRequired()
        );

    public void ShowReadCancelled() =>
        Update(
            page =>
                page.ShowReadCancelled()
        );

    public void ShowCaptureStarted(
        DofusWindowInfo window) =>
        Update(
            page =>
                page.ShowCaptureStarted(window)
        );

    public void ShowCaptureSuccess(
        DofusCaptureResult capture) =>
        Update(
            page =>
                page.ShowCaptureSuccess(capture)
        );

    public void ShowCaptureFailed(
        string message) =>
        Update(
            page =>
                page.ShowCaptureFailed(message)
        );

    public void ShowMultipleTooltipsDetected(
        int count) =>
        Update(
            page =>
                page.ShowMultipleTooltipsDetected(count)
        );

    public void ShowMarketPanelDetected(
        DofusMarketPanelDetectionResult panel) =>
        Update(
            page =>
                page.ShowMarketPanelDetected(panel)
        );

    public void ShowMarketEquipmentRead(
        string itemName,
        long? price) =>
        Update(
            page =>
                page.ShowMarketEquipmentRead(
                    itemName,
                    price
                )
        );

    public void ShowMarketEquipmentRecorded(
        string itemName,
        double confidence,
        long capturedPrice,
        long effectivePrice,
        bool manualPricePreserved) =>
        Update(
            page =>
                page.ShowMarketEquipmentRecorded(
                    itemName,
                    confidence,
                    capturedPrice,
                    effectivePrice,
                    manualPricePreserved
                )
        );

    public void ShowMarketEquipmentRecognitionFailed(
        string recognizedName,
        long detectedPrice) =>
        Update(
            page =>
                page.ShowMarketEquipmentRecognitionFailed(
                    recognizedName,
                    detectedPrice
                )
        );

    public void ShowAuxiliaryMarketDataRecorded(
        string objectName,
        int lotCount,
        string? focusedEquipmentName) =>
        Update(
            page =>
                page.ShowAuxiliaryMarketDataRecorded(
                    objectName,
                    lotCount,
                    focusedEquipmentName
                )
        );

    public void ShowAuxiliaryMarketReadFailed(
        string objectName) =>
        Update(
            page =>
                page.ShowAuxiliaryMarketReadFailed(
                    objectName
                )
        );

    public void ShowPanelNotDetected() =>
        Update(
            page =>
                page.ShowPanelNotDetected()
        );

    public void ShowPanelDetected(
        DofusPanelDetectionResult panel) =>
        Update(
            page =>
                page.ShowPanelDetected(panel)
        );

    public void ShowCrushRowNotDetected() =>
        Update(
            page =>
                page.ShowCrushRowNotDetected()
        );

    public void ShowLastCrushRowDetected(
        CrushRowDetectionResult row) =>
        Update(
            page =>
                page.ShowLastCrushRowDetected(row)
        );

    public void ShowCrushFieldsExtracted() =>
        Update(
            page =>
                page.ShowCrushFieldsExtracted()
        );

    public void ShowCrushOcrResult(
        string itemName,
        double? coefficient) =>
        Update(
            page =>
                page.ShowCrushOcrResult(
                    itemName,
                    coefficient
                )
        );

    public void ShowTooltipEquipmentFocused(
        string itemName,
        double confidence) =>
        Update(
            page =>
                page.ShowTooltipEquipmentFocused(
                    itemName,
                    confidence
                )
        );

    public void ShowEquipmentRecognitionFailed(
        string recognizedText) =>
        Update(
            page =>
                page.ShowEquipmentRecognitionFailed(
                    recognizedText
                )
        );

    public void ShowServerNotSelected() =>
        Update(
            page =>
                page.ShowServerNotSelected()
        );

    public void ShowRecognizedEquipment(
        string itemName,
        double recognitionConfidence,
        double detectedCoefficient,
        double appliedCoefficient,
        bool manualCoefficientPreserved,
        long? equipmentPrice) =>
        Update(
            page =>
                page.ShowRecognizedEquipment(
                    itemName,
                    recognitionConfidence,
                    detectedCoefficient,
                    appliedCoefficient,
                    manualCoefficientPreserved,
                    equipmentPrice
                )
        );

    private void Update(
        Action<MarketCaptureOverlayPage> update)
    {
        void Apply()
        {
            // Toujours mémoriser le dernier diagnostic.
            _pendingUpdate = update;

            // Une capture ne doit jamais créer ou ouvrir
            // l'overlay "Mise à jour marché".
            if (_page is null)
            {
                return;
            }

            update(_page);
        }

        if (MainThread.IsMainThread)
        {
            Apply();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Apply);
        }
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        LoadStoredLayout();

        MarketCaptureOverlayPage page =
            new(this);

        Window window = new(page)
        {
            Title = "BestCrush — Mise à jour marché",
            Width = _currentWidth,
            Height = _currentHeight,
            X = _currentX,
            Y = _currentY
        };

        _page = page;
        _window = window;

        // Si des captures ont eu lieu pendant que cette
        // fenêtre était fermée, afficher le dernier état
        // lorsque l'utilisateur l'ouvre volontairement.
        _pendingUpdate?.Invoke(page);

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
            _isVisible = false;
#endif
        };

        Application.Current?.OpenWindow(window);
    }

    private void LoadStoredLayout()
    {
#if WINDOWS
        OverlayWindowLayout layout =
            overlayLayoutSettingsService
                .GetValidatedLayout(
                    OverlayLayoutKind
                        .Market,
                    MinimumOverlayWidth,
                    MinimumOverlayHeight,
                    allowResize: true
                );

        _currentX =
            layout.X;

        _currentY =
            layout.Y;

        _currentWidth =
            layout.Width;

        _currentHeight =
            layout.Height;
#endif
    }

    private void ApplyLayout(
        OverlayWindowLayout layout)
    {
#if WINDOWS
        _currentX =
            layout.X;

        _currentY =
            layout.Y;

        _currentWidth =
            layout.Width;

        _currentHeight =
            layout.Height;

        if (_appWindow is null)
        {
            return;
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
#endif
    }

    private void SaveCurrentLayout()
    {
#if WINDOWS
        overlayLayoutSettingsService
            .SaveLayout(
                OverlayLayoutKind
                    .Market,
                new OverlayWindowLayout(
                    _currentX,
                    _currentY,
                    _currentWidth,
                    _currentHeight
                )
            );
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
            235
        );

        ShowWindow(
            _hwnd,
            _isVisible
                ? SwShowNoActivate
                : SwHide
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
