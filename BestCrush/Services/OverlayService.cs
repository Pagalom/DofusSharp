using BestCrush.Overlay;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

#if WINDOWS
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Windows.Graphics;
#endif

namespace BestCrush.Services;

public sealed class OverlayService(
    DofusWindowService dofusWindowService,
    DofusCaptureService dofusCaptureService,
    DofusPanelDetectionService dofusPanelDetectionService,
    DofusMarketPanelDetectionService dofusMarketPanelDetectionService,
    DofusMarketLotReaderService dofusMarketLotReaderService,
    DofusCrushRowDetectionService dofusCrushRowDetectionService,
    DofusImageRegionService dofusImageRegionService,
    DofusOcrService dofusOcrService,
    IServiceScopeFactory serviceScopeFactory,
    CurrentServerState currentServerState,
    FocusedEquipmentState focusedEquipmentState)
{
    private Window? _overlayWindow;
    private OverlayPage? _overlayPage;

#if WINDOWS
    private AppWindow? _appWindow;

    private IntPtr _overlayHwnd = IntPtr.Zero;

    private bool _isOverlayVisible;

    private int _currentX = 40;
    private int _currentY = 40;

    private int _currentWidth = 340;
    private int _currentHeight = 432;

    private int _dragStartX;
    private int _dragStartY;

    private int _resizeStartX;
    private int _resizeStartY;
    private int _resizeStartWidth;
    private int _resizeStartHeight;

    private OverlayResizeEdge _resizeEdge;

    private const int MinimumOverlayWidth = 260;
    private const int MinimumOverlayHeight = 170;

    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const uint LwaAlpha = 0x00000002;

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private const int WhKeyboardLl = 13;

    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;

    private const int VkF7 = 0x76;
    private const int VkF8 = 0x77;

    private IntPtr _keyboardHook = IntPtr.Zero;

    private LowLevelKeyboardProc? _keyboardProc;

    private bool _f7Pressed;
    private bool _f8Pressed;
#endif

    public void Initialize()
    {
        if (_overlayWindow is not null)
        {
            return;
        }

        OverlayPage page =
            new(
                this,
                dofusWindowService
            );
        _overlayPage = page;

        Window window = new(page)
        {
            Title = "BestCrush Overlay",
            Width = 340,
            Height = 432,
            X = 40,
            Y = 40
        };

        window.Created += (_, _) =>
        {
            ConfigureOverlayWindow(window);

            window.Dispatcher.DispatchDelayed(
                TimeSpan.FromMilliseconds(100),
                Hide
            );
        };

        window.Destroying += (_, _) =>
        {
            _overlayPage = null;
            if (ReferenceEquals(
                _overlayWindow,
                window))
            {
                _overlayWindow = null;

#if WINDOWS
                _appWindow = null;
                _overlayHwnd = IntPtr.Zero;
                _isOverlayVisible = false;
#endif
            }
        };

        _overlayWindow = window;

        Application.Current?.OpenWindow(window);
    }

    public void Toggle()
    {
        #if WINDOWS
                if (_overlayWindow is null ||
                    _overlayHwnd == IntPtr.Zero)
                {
                    return;
                }

                if (_isOverlayVisible)
                {
                    Hide();
                }
                else
                {
                    Show();
                }
        #endif
    }

    public void RequestRead()
    {
        _ = RequestReadAsync();
    }

    private async Task RequestReadAsync()
    {
        if (!currentServerState.HasSelectedServer)
        {
            _overlayPage?.ShowServerSelectionRequired();

            Show();

            return;
        }

        DofusWindowInfo? dofusWindow =
            dofusWindowService.GetActiveDofusWindow();

        // Règle absolue :
        // aucune fenêtre Dofus = aucune capture.
        if (dofusWindow is null)
        {
            _overlayPage?.ShowReadCancelled();

            Show();

            return;
        }


        _overlayPage?.ShowCaptureStarted(
            dofusWindow
        );

        Show();

        string? captureFilePath = null;

        string? captureDirectory = null;

        try
        {
            DofusCaptureResult capture =
                await dofusCaptureService.CaptureAsync(
                    dofusWindow
                );

                captureDirectory =
                    Path.GetDirectoryName(
                        capture.FilePath
                    );

            captureFilePath =
                capture.FilePath;

            _overlayPage?.ShowCaptureSuccess(
                capture
            );

            DofusPanelDetectionResult? panel =
                await dofusPanelDetectionService
                    .DetectCrushResultPanelAsync(
                        capture.FilePath
                    );

                    if (panel is null)
                    {
                        DofusMarketPanelDetectionResult? marketPanel =
                            await dofusMarketPanelDetectionService
                                .DetectMarketPanelAsync(
                                    capture.FilePath
                                );

                        if (marketPanel is not null)
                        {
                            string marketItemNameRegion =
                                await dofusImageRegionService
                                    .ExtractRegionAsync(
                                        marketPanel.DebugImagePath,
                                        new RelativeImageRegion(
                                            X: 0.23,
                                            Y: 0.055,
                                            Width: 0.70,
                                            Height: 0.095
                                        ),
                                        "hdv-item-name"
                                    );

                            string marketFirstPriceRegion =
                                await dofusImageRegionService
                                    .ExtractRegionAsync(
                                        marketPanel.DebugImagePath,
                                        new RelativeImageRegion(
                                            X: 0.52,
                                            Y: 0.282,
                                            Width: 0.30,
                                            Height: 0.045
                                        ),
                                        "hdv-first-price"
                                    );

                            string recognizedMarketItemName =
                                await dofusOcrService
                                    .RecognizeUpscaledTextAsync(
                                        marketItemNameRegion
                                    );
                            recognizedMarketItemName =
                                recognizedMarketItemName
                                    .Split(
                                        ['\r', '\n'],
                                        StringSplitOptions.RemoveEmptyEntries
                                    )
                                    .FirstOrDefault()?
                                    .Trim()
                                ?? string.Empty;

                            long? recognizedMarketPrice =
                                await dofusOcrService
                                    .RecognizePriceAsync(
                                        marketFirstPriceRegion
                                    );

                                    if (string.IsNullOrWhiteSpace(
                                            recognizedMarketItemName))
                                    {
                                        _overlayPage?.ShowMarketEquipmentRead(
                                            recognizedMarketItemName,
                                            null
                                        );

                                        return;
                                    }

                            using IServiceScope marketScope =
                                serviceScopeFactory.CreateScope();

                            DofusItemRecognitionService
                                marketItemRecognitionService =
                                    marketScope.ServiceProvider
                                        .GetRequiredService<
                                            DofusItemRecognitionService>();
                            
                            DofusRuneRecognitionService
                                runeRecognitionService =
                                    marketScope.ServiceProvider
                                        .GetRequiredService<
                                            DofusRuneRecognitionService>();
                            
                            DofusResourceRecognitionService
                                resourceRecognitionService =
                                    marketScope.ServiceProvider
                                        .GetRequiredService<
                                            DofusResourceRecognitionService>();

                            MarketPriceService hdvMarketPriceService =
                                marketScope.ServiceProvider
                                    .GetRequiredService<
                                        MarketPriceService>();

                            ItemRecognitionResult? marketRecognition =
                                await marketItemRecognitionService
                                    .RecognizeEquipmentAsync(
                                        recognizedMarketItemName
                                    );

                            if (marketRecognition is null)
                            {
                                // 1. On essaie d'abord les runes.
                                RuneRecognitionResult? runeRecognition =
                                    await runeRecognitionService
                                        .RecognizeRuneAsync(
                                            recognizedMarketItemName
                                        );

                                if (runeRecognition is not null)
                                {
                                    IReadOnlyList<DofusMarketLot> lots =
                                        await dofusMarketLotReaderService
                                            .ReadMaterialLotsAsync(
                                                marketPanel.DebugImagePath
                                            );

                                    if (lots.Count == 0)
                                    {
                                        _overlayPage?
                                            .ShowAuxiliaryMarketReadFailed(
                                                runeRecognition.Rune.Name
                                            );

                                        return;
                                    }

                                    string runeServerName =
                                        currentServerState.ServerName!;

                                    foreach (DofusMarketLot lot in lots)
                                    {
                                        await hdvMarketPriceService
                                            .AddObservationAsync(
                                                MarketObjectType.Rune,
                                                runeRecognition.Rune.DofusDbId,
                                                runeServerName,
                                                lot.Price,
                                                lot.Quantity,
                                                MarketPriceSource.InGameAutomatic
                                            );
                                    }

                                    _overlayPage?
                                        .ShowAuxiliaryMarketDataRecorded(
                                            runeRecognition.Rune.Name,
                                            lots.Count,
                                            focusedEquipmentState
                                                .Equipment?
                                                .Name
                                        );

                                    await RefreshFocusedProfitabilityAsync();

                                    return;
                                }

                                // 2. Si ce n'est pas une rune,
                                // on essaie alors les ressources.
                                ResourceRecognitionResult? resourceRecognition =
                                    await resourceRecognitionService
                                        .RecognizeResourceAsync(
                                            recognizedMarketItemName
                                        );

                                if (resourceRecognition is not null)
                                {
                                    IReadOnlyList<DofusMarketLot> lots =
                                        await dofusMarketLotReaderService
                                            .ReadMaterialLotsAsync(
                                                marketPanel.DebugImagePath
                                            );

                                    if (lots.Count == 0)
                                    {
                                        _overlayPage?
                                            .ShowAuxiliaryMarketReadFailed(
                                                resourceRecognition
                                                    .Resource
                                                    .Name
                                            );

                                        return;
                                    }

                                    string resourceServerName =
                                        currentServerState.ServerName!;

                                    foreach (DofusMarketLot lot in lots)
                                    {
                                        await hdvMarketPriceService
                                            .AddObservationAsync(
                                                MarketObjectType.Resource,
                                                resourceRecognition
                                                    .Resource
                                                    .DofusDbId,
                                                resourceServerName,
                                                lot.Price,
                                                lot.Quantity,
                                                MarketPriceSource.InGameAutomatic
                                            );
                                    }

                                    _overlayPage?
                                        .ShowAuxiliaryMarketDataRecorded(
                                            resourceRecognition
                                                .Resource
                                                .Name,
                                            lots.Count,
                                            focusedEquipmentState
                                                .Equipment?
                                                .Name
                                        );

                                    await RefreshFocusedProfitabilityAsync();

                                    return;
                                }

                                // 3. Ni équipement, ni rune, ni ressource.
                                if (recognizedMarketPrice is long detectedPrice)
                                {
                                    _overlayPage?
                                        .ShowMarketEquipmentRecognitionFailed(
                                            recognizedMarketItemName,
                                            detectedPrice
                                        );
                                }
                                else
                                {
                                    _overlayPage?
                                        .ShowMarketEquipmentRead(
                                            recognizedMarketItemName,
                                            null
                                        );
                                }

                                return;
                            }

                            Equipment marketEquipment =
                                marketRecognition.Equipment;
                            if (recognizedMarketPrice is null ||
                                recognizedMarketPrice <= 0)
                            {
                                _overlayPage?
                                    .ShowMarketEquipmentRead(
                                        marketEquipment.Name,
                                        null
                                    );

                                return;
                            }

                            focusedEquipmentState.SetEquipment(marketEquipment);

                            string hdvServerName =
                                currentServerState.ServerName!;

                            MarketPriceObservation capturedPrice =
                                await hdvMarketPriceService
                                    .AddObservationAsync(
                                        MarketObjectType.Equipment,
                                        marketEquipment.DofusDbId,
                                        hdvServerName,
                                        recognizedMarketPrice.Value,
                                        1,
                                        MarketPriceSource.InGameAutomatic
                                    );

                            MarketPriceObservation? effectivePrice =
                                await hdvMarketPriceService
                                    .GetLatestObservationAsync(
                                        MarketObjectType.Equipment,
                                        marketEquipment.DofusDbId,
                                        hdvServerName,
                                        1
                                    );

                            MarketPriceObservation appliedPrice =
                                effectivePrice
                                ?? capturedPrice;

                            bool manualPricePreserved =
                                appliedPrice.Source ==
                                    MarketPriceSource.Manual &&
                                appliedPrice.Price !=
                                    capturedPrice.Price;

                            _overlayPage?.ShowMarketEquipmentRecorded(
                                marketEquipment.Name,
                                marketRecognition.Confidence,
                                capturedPrice.Price,
                                appliedPrice.Price,
                                manualPricePreserved
                            );
                            await RefreshFocusedProfitabilityAsync();

                            return;
                        }

                        _overlayPage?.ShowPanelNotDetected();

                        return;
                    }

            _overlayPage?.ShowPanelDetected(
                panel
            );

            CrushRowDetectionResult? lastRow =
                await dofusCrushRowDetectionService
                    .DetectLastRowAsync(
                        panel.DebugImagePath
                    );

            if (lastRow is null)
            {
                _overlayPage?.ShowCrushRowNotDetected();

                return;
            }

            _overlayPage?.ShowLastCrushRowDetected(
                lastRow
            );
            string itemNameRegion =
                await dofusImageRegionService.ExtractRegionAsync(
                    lastRow.DebugImagePath,
                    new RelativeImageRegion(
                        X: 0.07,
                        Y: 0.08,
                        Width: 0.40,
                        Height: 0.84
                    ),
                    "item-name"
                );

            string coefficientRegion =
                await dofusImageRegionService.ExtractRegionAsync(
                    lastRow.DebugImagePath,
                    new RelativeImageRegion(
                        X: 0.46,
                        Y: 0.10,
                        Width: 0.15,
                        Height: 0.80
                    ),
                    "coefficient"
                );
                string recognizedItemName =
                    await dofusOcrService
                        .RecognizeUpscaledTextAsync(
                            itemNameRegion
                        );

                recognizedItemName =
                    recognizedItemName
                        .Split(
                            ['\r', '\n'],
                            StringSplitOptions.RemoveEmptyEntries
                        )
                        .FirstOrDefault()?
                        .Trim()
                    ?? string.Empty;

                double? recognizedCoefficient =
                    await dofusOcrService.RecognizeCoefficientAsync(
                        coefficientRegion
                    );

                _overlayPage?.ShowCrushOcrResult(
                    recognizedItemName,
                    recognizedCoefficient
                );
                if (recognizedCoefficient is null)
                {
                    return;
                }

                using IServiceScope scope =
                    serviceScopeFactory.CreateScope();

                DofusItemRecognitionService itemRecognitionService =
                    scope.ServiceProvider
                        .GetRequiredService<
                            DofusItemRecognitionService>();

                CoefficientService coefficientService =
                    scope.ServiceProvider
                        .GetRequiredService<
                            CoefficientService>();

                MarketPriceService marketPriceService =
                    scope.ServiceProvider
                        .GetRequiredService<
                            MarketPriceService>();

                ItemRecognitionResult? recognition =
                    await itemRecognitionService
                        .RecognizeEquipmentAsync(
                            recognizedItemName
                        );

                if (recognition is null)
                {
                    _overlayPage?.ShowEquipmentRecognitionFailed(
                        recognizedItemName
                    );

                    return;
                }

                Equipment equipment =
                    recognition.Equipment;

                focusedEquipmentState.SetEquipment(equipment);

                string? serverName =
                    currentServerState.ServerName;

                if (string.IsNullOrWhiteSpace(serverName))
                {
                    _overlayPage?.ShowServerNotSelected();

                    return;
                }

                CoefficientObservation capturedCoefficient =
                    await coefficientService
                        .AddObservationAsync(
                            equipment.DofusDbId,
                            serverName,
                            recognizedCoefficient.Value,
                            CoefficientSource.InGameAutomatic
                        );

                CoefficientObservation? effectiveCoefficient =
                    await coefficientService
                        .GetLatestObservationAsync(
                            equipment.DofusDbId,
                            serverName
                        );

                CoefficientObservation appliedCoefficient =
                    effectiveCoefficient
                    ?? capturedCoefficient;

                bool manualCoefficientPreserved =
                    appliedCoefficient.Source ==
                        CoefficientSource.Manual &&
                    Math.Abs(
                        appliedCoefficient.CoefficientPercent -
                        recognizedCoefficient.Value
                    ) >= 0.001;

                MarketPriceObservation? equipmentPrice =
                    await marketPriceService
                        .GetLatestObservationAsync(
                            MarketObjectType.Equipment,
                            equipment.DofusDbId,
                            serverName,
                            1
                        );

                _overlayPage?.ShowRecognizedEquipment(
                    equipment.Name,
                    recognition.Confidence,
                    recognizedCoefficient.Value,
                    appliedCoefficient.CoefficientPercent,
                    manualCoefficientPreserved,
                    equipmentPrice?.Price
                );
                await RefreshFocusedProfitabilityAsync();
        }
        catch (Exception ex)
        {
            _overlayPage?.ShowCaptureFailed(
                ex.Message
            );
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(
                captureDirectory))
            {
                try
                {
                    if (Directory.Exists(
                        captureDirectory))
                    {
                        Directory.Delete(
                            captureDirectory,
                            recursive: true
                        );
                    }
                }
                catch
                {
                    // Le nettoyage des fichiers temporaires
                    // ne doit jamais interrompre BestCrush.
                }
            }
        }
    }

    public async Task FocusEquipmentAsync(
        Equipment equipment)
    {
        focusedEquipmentState.SetEquipment(
            equipment
        );

        await RefreshFocusedProfitabilityAsync();

        await MainThread
            .InvokeOnMainThreadAsync(
                Show
            );
    }

    private async Task RefreshFocusedProfitabilityAsync()
    {
        Equipment? equipment =
            focusedEquipmentState.Equipment;

        string? serverName =
            currentServerState.ServerName;

        if (equipment is null ||
            string.IsNullOrWhiteSpace(serverName))
        {
            return;
        }

        using IServiceScope scope =
            serviceScopeFactory.CreateScope();

        EquipmentProfitabilityService
            profitabilityService =
                scope.ServiceProvider
                    .GetRequiredService<
                        EquipmentProfitabilityService>();

        EquipmentProfitabilityResult result =
            await profitabilityService
                .CalculateAsync(
                    equipment,
                    serverName
                );

        await MainThread
            .InvokeOnMainThreadAsync(
                () =>
                {
                    _overlayPage?
                        .ShowProfitability(
                            result
                        );
                }
            );
    }

    public void BeginResize(
        OverlayResizeEdge edge)
    {
    #if WINDOWS
        _resizeEdge = edge;

        _resizeStartX = _currentX;
        _resizeStartY = _currentY;

        _resizeStartWidth = _currentWidth;
        _resizeStartHeight = _currentHeight;
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

        int dx = (int)Math.Round(totalX);
        int dy = (int)Math.Round(totalY);

        int newX = _resizeStartX;
        int newY = _resizeStartY;

        int newWidth = _resizeStartWidth;
        int newHeight = _resizeStartHeight;

        if (_resizeEdge.HasFlag(
            OverlayResizeEdge.Right))
        {
            newWidth =
                Math.Max(
                    MinimumOverlayWidth,
                    _resizeStartWidth + dx
                );
        }

        if (_resizeEdge.HasFlag(
            OverlayResizeEdge.Bottom))
        {
            newHeight =
                Math.Max(
                    MinimumOverlayHeight,
                    _resizeStartHeight + dy
                );
        }

        if (_resizeEdge.HasFlag(
            OverlayResizeEdge.Left))
        {
            newWidth =
                Math.Max(
                    MinimumOverlayWidth,
                    _resizeStartWidth - dx
                );

            newX =
                _resizeStartX +
                (_resizeStartWidth - newWidth);
        }

        if (_resizeEdge.HasFlag(
            OverlayResizeEdge.Top))
        {
            newHeight =
                Math.Max(
                    MinimumOverlayHeight,
                    _resizeStartHeight - dy
                );

            newY =
                _resizeStartY +
                (_resizeStartHeight - newHeight);
        }

        _appWindow.Resize(
            new SizeInt32(
                newWidth,
                newHeight
            )
        );

        _appWindow.Move(
            new PointInt32(
                newX,
                newY
            )
        );

        _currentX = newX;
        _currentY = newY;

        _currentWidth = newWidth;
        _currentHeight = newHeight;
    #endif
    }

    public void Show()
    {
#if WINDOWS
        if (_overlayHwnd == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(
            _overlayHwnd,
            SwShowNoActivate
        );

        if (_appWindow?.Presenter
            is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }

        _isOverlayVisible = true;
#endif
    }

    public void Hide()
    {
#if WINDOWS
        if (_overlayHwnd == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(
            _overlayHwnd,
            SwHide
        );

        _isOverlayVisible = false;
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

    public void Shutdown()
    {
#if WINDOWS
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(
                _keyboardHook
            );

            _keyboardHook = IntPtr.Zero;
        }
#endif

        if (_overlayWindow is null)
        {
            return;
        }

        Window window = _overlayWindow;

        _overlayWindow = null;

        Application.Current?.CloseWindow(
            window
        );
    }

    private void ConfigureOverlayWindow(
        Window window)
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

        _overlayHwnd = hwnd;

        // On le masque immédiatement.
        ShowWindow(
            hwnd,
            SwHide
        );

        _isOverlayVisible = false;

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
            hwnd,
            225
        );

        InstallKeyboardHook();
#endif
    }

#if WINDOWS
    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            return;
        }

        _keyboardProc =
            KeyboardHookCallback;

        IntPtr moduleHandle =
            GetModuleHandle(null);

        _keyboardHook =
            SetWindowsHookEx(
                WhKeyboardLl,
                _keyboardProc,
                moduleHandle,
                0
            );
    }

    private IntPtr KeyboardHookCallback(
        int nCode,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int virtualKeyCode =
                Marshal.ReadInt32(lParam);

            if (virtualKeyCode == VkF7)
            {
                if (wParam ==
                        (IntPtr)WmKeyDown &&
                    !_f7Pressed)
                {
                    _f7Pressed = true;

                    MainThread
                        .BeginInvokeOnMainThread(
                            Toggle
                        );
                }
                else if (
                    wParam ==
                    (IntPtr)WmKeyUp)
                {
                    _f7Pressed = false;
                }
            }
            if (virtualKeyCode == VkF8)
            {
                if (wParam == (IntPtr)WmKeyDown &&
                    !_f8Pressed)
                {
                    _f8Pressed = true;

                    Application.Current?.Dispatcher.DispatchDelayed(
                        TimeSpan.FromMilliseconds(150),
                        RequestRead
                    );
                }
                else if (wParam == (IntPtr)WmKeyUp)
                {
                    _f8Pressed = false;
                }
            }
        }

        return CallNextHookEx(
            _keyboardHook,
            nCode,
            wParam,
            lParam
        );
    }

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

    private delegate IntPtr
        LowLevelKeyboardProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam
        );

    [DllImport(
        "user32.dll",
        SetLastError = true
    )]
    private static extern IntPtr
        SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId
        );

    [DllImport("user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        UnhookWindowsHookEx(
            IntPtr hhk
        );

    [DllImport("user32.dll")]
    private static extern IntPtr
        CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam
        );

    [DllImport("user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        ShowWindow(
            IntPtr hWnd,
            int nCmdShow
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

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern IntPtr
        GetModuleHandle(
            string? lpModuleName
        );
#endif
}

[Flags]
public enum OverlayResizeEdge
{
    None = 0,

    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8
}