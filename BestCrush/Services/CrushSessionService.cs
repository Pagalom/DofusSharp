#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using System.Runtime.InteropServices;
#endif

using System.Numerics;
using System.Threading.Channels;

using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Overlay;

using Microsoft.Extensions.DependencyInjection;
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
    double? TotalValue,
    string? ErrorMessage
);

public sealed class CrushSessionService(
    DofusWindowService dofusWindowService,
    DofusCaptureService dofusCaptureService,
    DofusPanelDetectionService
        dofusPanelDetectionService,
    DofusCrushRuneCellDetectionService
        runeCellDetectionService,
    DofusCrushCoefficientScanService
        coefficientScanService,
    IServiceScopeFactory serviceScopeFactory,
    CurrentServerState currentServerState,
    MarketDataChangeNotifier marketDataChangeNotifier,
    OverlayLayoutSettingsService
        overlayLayoutSettingsService)
{
    private Window? _window;
    private CrushSessionOverlayPage? _page;

    private bool _isRunning;

    private CancellationTokenSource?
        _mouseMonitorCancellation;

    private Task? _mouseMonitorTask;

    private readonly object
        _stateLock = new();

    private readonly System.Threading.SemaphoreSlim
        _marketRefreshLock = new(1, 1);

    private int _idleCaptureCount;

    private int? _lastCursorX;
    private int? _lastCursorY;

    private string? _errorMessage;

    private bool _coefficientsScanned;

    private readonly List<
        ScannedRuneCellIdentity>
        _scannedRuneCells = [];

    private readonly Dictionary<
        long,
        AccumulatedRune>
        _runes = [];

    // Les captures sont produites rapidement par
    // la surveillance souris puis traitées derrière.
    //
    // SingleReader est volontaire : l'OCR et
    // l'anti-doublon restent déterministes, sans
    // empêcher la capture suivante d'être prise.
    private readonly Channel<
        CapturedCursorWorkItem>
        _processingQueue =
            Channel.CreateUnbounded<
                CapturedCursorWorkItem>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations =
                        false
                }
            );

    private readonly CancellationTokenSource
        _processingCancellation =
            new();

    private Task? _processingTask;

    private long _sessionId;

    public bool IsRunning =>
        _isRunning;

    public event EventHandler?
        CoefficientsUpdated;

    public bool IsVisible
    {
        get
        {
#if WINDOWS
            return
                _window is not null &&
                _isVisible;
#else
            return
                _window is not null;
#endif
        }
    }

#if WINDOWS
    private AppWindow? _appWindow;

    private IntPtr _windowHwnd =
        IntPtr.Zero;

    private bool _isVisible;

    private int _currentX = 750;
    private int _currentY = 80;

    private int _currentWidth = 390;
    private int _currentHeight = 520;

    private int _dragStartX;
    private int _dragStartY;

    private int _resizeStartX;
    private int _resizeStartY;
    private int _resizeStartWidth;
    private int _resizeStartHeight;

    private OverlayResizeEdge
        _resizeEdge;

    private const int MinimumOverlayWidth = 300;
    private const int MinimumOverlayHeight = 220;

    private const int GwlExStyle = -20;
    private const long WsExLayered =
        0x00080000L;

    private const uint LwaAlpha =
        0x00000002;

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private const int
        MousePollMilliseconds = 40;

    private const int
        MouseStableMilliseconds = 220;

    private const int
        MouseMovementThreshold = 4;

    private const int
        RowFingerprintMaximumDistance = 8;
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
        marketDataChangeNotifier.Changed -=
            OnMarketDataChanged;

        marketDataChangeNotifier.Changed +=
            OnMarketDataChanged;

        ResetSessionState();

        _sessionId++;

        _isRunning = true;

        EnsureProcessingWorker();
        EnsureWindow();
        Show();

        PublishSnapshot();

        StartMouseMonitoring();
    }

    public void Stop()
    {
        _isRunning = false;

        StopMouseMonitoring();

        PublishSnapshot();

        // Les captures déjà en file continuent
        // volontairement leur traitement.
    }

    public void Show()
    {
        EnsureWindow();

#if WINDOWS
        _isVisible = true;

        if (_windowHwnd !=
            IntPtr.Zero)
        {
            ShowWindow(
                _windowHwnd,
                SwShowNoActivate
            );

            if (_appWindow?.Presenter
                is OverlappedPresenter
                presenter)
            {
                presenter.IsAlwaysOnTop =
                    true;
            }
        }
#endif

        PublishSnapshot();
    }

    public void Hide()
    {
#if WINDOWS
        _isVisible = false;

        if (_windowHwnd ==
            IntPtr.Zero)
        {
            return;
        }

        ShowWindow(
            _windowHwnd,
            SwHide
        );
#endif
    }

    public bool ContainsScreenPoint(
        int x,
        int y)
    {
#if WINDOWS
        if (!_isVisible ||
            _window is null)
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

    public void InvalidateForScroll()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;

        StopMouseMonitoring();

        // Une session qui a scrollé n'est plus fiable :
        // les captures déjà en file deviennent invalides.
        _sessionId++;

        lock (_stateLock)
        {
            _errorMessage =
                "Ne pas scroller";

            _scannedRuneCells
                .Clear();

            _runes.Clear();
        }

        PublishSnapshot();
    }

    public void CloseAndReset()
    {
        _isRunning = false;

        StopMouseMonitoring();

        _sessionId++;

        ResetSessionState();

        if (_window is null)
        {
            return;
        }

        Window window =
            _window;

        _window = null;
        _page = null;

#if WINDOWS
        _windowHwnd =
            IntPtr.Zero;

        _isVisible =
            false;
#endif

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

        _isVisible = true;

        LoadStoredLayout();

        CrushSessionOverlayPage page =
            new(
                this
            );

        Window window =
            new(page)
            {
                Title =
                    "BestCrush — Résultat concassage",

                Width = _currentWidth,
                Height = _currentHeight,

                X = _currentX,
                Y = _currentY
            };

        window.Created +=
            (_, _) =>
            {
#if WINDOWS
                if (window.Handler?.PlatformView
                    is not
                    Microsoft.UI.Xaml.Window
                    nativeWindow)
                {
                    return;
                }

                IntPtr hwnd =
                    WinRT.Interop.WindowNative
                        .GetWindowHandle(
                            nativeWindow
                        );

                _windowHwnd =
                    hwnd;

                Microsoft.UI.WindowId
                    windowId =
                        Microsoft.UI
                            .Win32Interop
                            .GetWindowIdFromWindow(
                                hwnd
                            );

                _appWindow =
                    AppWindow
                        .GetFromWindowId(
                            windowId
                        );

                if (_appWindow.Presenter
                    is OverlappedPresenter
                    presenter)
                {
                    presenter.IsAlwaysOnTop =
                        true;

                    presenter.IsResizable =
                        false;

                    presenter.IsMaximizable =
                        false;

                    presenter.IsMinimizable =
                        false;

                    presenter
                        .SetBorderAndTitleBar(
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

                if (!_isVisible)
                {
                    ShowWindow(
                        hwnd,
                        SwHide
                    );
                }
#endif
            };

        window.Destroying +=
            (_, _) =>
            {
                _isRunning =
                    false;

                StopMouseMonitoring();

                _sessionId++;

                ResetSessionState();

                _window =
                    null;

                _page =
                    null;

#if WINDOWS
                _windowHwnd =
                    IntPtr.Zero;

                _isVisible =
                    false;
#endif
            };

        _page = page;
        _window = window;

        Application.Current?
            .OpenWindow(
                window
            );
    }

    private void ResetSessionState()
    {
        lock (_stateLock)
        {
            _idleCaptureCount = 0;

            _lastCursorX = null;
            _lastCursorY = null;

            _errorMessage = null;

            _coefficientsScanned = false;

            _scannedRuneCells
                .Clear();

            _runes.Clear();
        }
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
                        .Crush
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

    private void LoadStoredLayout()
    {
#if WINDOWS
        OverlayWindowLayout layout =
            overlayLayoutSettingsService
                .GetValidatedLayout(
                    OverlayLayoutKind
                        .Crush,
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
                    .Crush,
                new OverlayWindowLayout(
                    _currentX,
                    _currentY,
                    _currentWidth,
                    _currentHeight
                )
            );
#endif
    }

    private CrushSessionSnapshot
        CreateSnapshot()
    {
        lock (_stateLock)
        {
            IReadOnlyList<
                CrushSessionRuneLine>
                runeLines =
                    _runes
                        .Values
                        .OrderBy(
                            rune =>
                                rune.Name
                        )
                        .Select(
                            rune =>
                                new
                                CrushSessionRuneLine(
                                    rune.Name,
                                    rune.Quantity,
                                    rune.Value
                                )
                        )
                        .ToList();

            double? totalValue =
                runeLines.Count > 0 &&
                runeLines.All(
                    rune =>
                        rune.Value
                            is not null
                )
                    ? runeLines.Sum(
                        rune =>
                            rune.Value
                                .GetValueOrDefault()
                    )
                    : null;

            return new CrushSessionSnapshot(
                _isRunning,
                _scannedRuneCells.Count,
                _idleCaptureCount,
                _lastCursorX,
                _lastCursorY,
                runeLines,
                totalValue,
                _errorMessage
            );
        }
    }

    private void PublishSnapshot()
    {
        CrushSessionSnapshot snapshot =
            CreateSnapshot();

        MainThread
            .BeginInvokeOnMainThread(
                () =>
                {
                    _page?.Update(
                        snapshot
                    );
                }
            );
    }

    private void EnsureProcessingWorker()
    {
        if (_processingTask is not null)
        {
            return;
        }

        _processingTask =
            Task.Run(
                () =>
                    ProcessQueueAsync(
                        _processingCancellation
                            .Token
                    )
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
                _mouseMonitorCancellation
                    .Token
            );
#endif
    }

    private async Task
        CaptureAndQueueAtCursorAsync(
#if WINDOWS
            WinPoint cursor,
#else
            object cursor,
#endif
            CancellationToken
                cancellationToken)
    {
#if WINDOWS
        DofusWindowInfo? dofusWindow =
            dofusWindowService
                .GetActiveDofusWindow();

        if (dofusWindow is null)
        {
            return;
        }

        if (cursor.X <
                dofusWindow.X ||
            cursor.Y <
                dofusWindow.Y ||
            cursor.X >=
                dofusWindow.X +
                dofusWindow.Width ||
            cursor.Y >=
                dofusWindow.Y +
                dofusWindow.Height)
        {
            return;
        }

        if (_window is not null &&
            cursor.X >=
                _currentX &&
            cursor.X <
                _currentX +
                _currentWidth &&
            cursor.Y >=
                _currentY &&
            cursor.Y <
                _currentY +
                _currentHeight)
        {
            return;
        }

        // Cette partie reste volontairement dans
        // le producteur : la capture doit représenter
        // exactement la position où la souris s'est
        // immobilisée.
        DofusCaptureResult capture =
            await dofusCaptureService
                .CaptureAsync(
                    dofusWindow,
                    cancellationToken
                );

        double relativeX =
            (
                cursor.X -
                dofusWindow.X
            ) /
            (double)
                dofusWindow.Width;

        double relativeY =
            (
                cursor.Y -
                dofusWindow.Y
            ) /
            (double)
                dofusWindow.Height;

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

        long sessionId =
            _sessionId;

        lock (_stateLock)
        {
            _idleCaptureCount++;

            _lastCursorX =
                captureX;

            _lastCursorY =
                captureY;
        }

        PublishSnapshot();

        CapturedCursorWorkItem workItem =
            new(
                sessionId,
                capture.FilePath,
                captureX,
                captureY
            );

        if (!_processingQueue
            .Writer
            .TryWrite(
                workItem
            ))
        {
            // La capture n'entrera jamais dans le
            // worker, elle n'est donc plus utile.
            dofusCaptureService
                .DeleteCaptureArtifacts(
                    capture.FilePath
                );
        }
#endif
    }

    private async Task ProcessQueueAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                CapturedCursorWorkItem workItem
                in _processingQueue
                    .Reader
                    .ReadAllAsync(
                        cancellationToken
                    ))
            {
                try
                {
                    if (workItem.SessionId !=
                        _sessionId)
                    {
                        continue;
                    }

                    await ProcessCaptureAsync(
                        workItem,
                        cancellationToken
                    );
                }
                catch (
                    OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Une capture défectueuse ne doit
                    // pas arrêter le worker.
                }
                finally
                {
                    // Inclut aussi les captures devenues
                    // obsolètes après changement de session.
                    dofusCaptureService
                        .DeleteCaptureArtifacts(
                            workItem
                                .CaptureFilePath
                        );
                }
            }
        }
        catch (
            OperationCanceledException)
        {
            // Arrêt de l'application.
        }
    }

    private async Task ProcessCaptureAsync(
        CapturedCursorWorkItem workItem,
        CancellationToken cancellationToken)
    {
        string? serverName =
            currentServerState
                .ServerName;

        if (string.IsNullOrWhiteSpace(
            serverName))
        {
            return;
        }

        DofusPanelDetectionResult?
            panel =
                await
                dofusPanelDetectionService
                    .DetectCrushResultPanelAsync(
                        workItem.CaptureFilePath,
                        cancellationToken
                    );

        if (panel is null)
        {
            return;
        }

        bool shouldScanCoefficients;

        lock (_stateLock)
        {
            shouldScanCoefficients =
                !_coefficientsScanned &&
                workItem.SessionId == _sessionId &&
                _errorMessage is null;
        }

        if (shouldScanCoefficients)
        {
            IReadOnlyList<CrushCoefficientScanResult>
                coefficientResults =
                    await coefficientScanService
                        .ScanAndStoreAsync(
                            panel.DebugImagePath,
                            serverName,
                            cancellationToken
                        );

            bool coefficientsUpdated = false;

            if (coefficientResults.Count > 0)
            {
                lock (_stateLock)
                {
                    if (workItem.SessionId == _sessionId &&
                        _errorMessage is null)
                    {
                        _coefficientsScanned = true;
                        coefficientsUpdated = true;
                    }
                }
            }

            if (coefficientsUpdated)
            {
                CoefficientsUpdated?.Invoke(
                    this,
                    EventArgs.Empty
                );
            }
        }

        int panelCursorX =
            workItem.CaptureX -
            panel.X;

        int panelCursorY =
            workItem.CaptureY -
            panel.Y;

        if (panelCursorX < 0 ||
            panelCursorY < 0 ||
            panelCursorX >=
                panel.Width ||
            panelCursorY >=
                panel.Height)
        {
            return;
        }

        using IServiceScope scope =
            serviceScopeFactory
                .CreateScope();

        DofusItemTooltipDetectionService
            tooltipDetectionService =
                scope
                    .ServiceProvider
                    .GetRequiredService<
                        DofusItemTooltipDetectionService>();

        DofusRuneRecognitionService
            runeRecognitionService =
                scope
                    .ServiceProvider
                    .GetRequiredService<
                        DofusRuneRecognitionService>();

        MarketPriceService
            marketPriceService =
                scope
                    .ServiceProvider
                    .GetRequiredService<
                        MarketPriceService>();

        DofusItemTooltipDetectionResult
            tooltipDetection =
                await
                tooltipDetectionService
                    .DetectAsync(
                        workItem.CaptureFilePath,
                        cancellationToken
                    );

        if (tooltipDetection
            .Candidates
            .Count != 1)
        {
            return;
        }

        DofusItemTooltipCandidate
            tooltip =
                tooltipDetection
                    .Candidates[0];

        if (string.IsNullOrWhiteSpace(
            tooltip.RecognizedTitle))
        {
            return;
        }

        RuneRecognitionResult?
            runeRecognition =
                await
                runeRecognitionService
                    .RecognizeRuneAsync(
                        tooltip
                            .RecognizedTitle
                    );

        if (runeRecognition is null)
        {
            return;
        }

        DofusCrushRuneCellDetectionResult?
            cell =
                await
                runeCellDetectionService
                    .DetectAsync(
                        panel.DebugImagePath,
                        panelCursorX,
                        panelCursorY,
                        cancellationToken
                    );

        if (cell is null)
        {
            return;
        }

        if (workItem.SessionId !=
            _sessionId)
        {
            return;
        }

        int quantity =
            tooltip.LotQuantity
            ?? 1;

        lock (_stateLock)
        {
            if (workItem.SessionId !=
                    _sessionId ||
                _errorMessage is not null)
            {
                return;
            }

            if (IsAlreadyScannedLocked(
                cell))
            {
                return;
            }

            _scannedRuneCells.Add(
                new ScannedRuneCellIdentity(
                    cell.RowFingerprint,
                    cell.RowHeight,
                    cell.RowOccurrence,
                    cell.ColumnIndex,
                    cell.RuneLineIndex
                )
            );

            long runeId =
                runeRecognition
                    .Rune
                    .DofusDbId;

            if (!_runes.TryGetValue(
                runeId,
                out AccumulatedRune?
                    accumulatedRune))
            {
                accumulatedRune =
                    new AccumulatedRune
                    {
                        Name =
                            runeRecognition
                                .Rune
                                .Name
                    };

                _runes[
                    runeId
                ] =
                    accumulatedRune;
            }

            accumulatedRune.Quantity =
                checked(
                    accumulatedRune
                        .Quantity +
                    quantity
                );
        }

        IReadOnlyDictionary<
            (
                long DofusDbId,
                int Quantity
            ),
            MarketPriceObservation>
            observations =
                await marketPriceService
                    .GetLatestObservationsForServerAsync(
                        MarketObjectType.Rune,
                        serverName,
                        cancellationToken
                    );

        lock (_stateLock)
        {
            if (workItem.SessionId !=
                    _sessionId ||
                _errorMessage is not null)
            {
                return;
            }

            foreach (
                KeyValuePair<
                    long,
                    AccumulatedRune>
                rune
                in _runes)
            {
                MarketValueResult?
                    value =
                        marketPriceService
                            .CalculateValue(
                                rune.Key,
                                rune.Value
                                    .Quantity,
                                observations
                            );

                rune.Value.Value =
                    value?.Value;
            }
        }

        PublishSnapshot();
    }

    private void OnMarketDataChanged(
        object? sender,
        MarketDataChangedEventArgs e)
    {
        if (e.ObjectType !=
                MarketObjectType.Rune ||
            !string.Equals(
                e.ServerName,
                currentServerState.ServerName,
                StringComparison.Ordinal))
        {
            return;
        }

        lock (_stateLock)
        {
            if (_runes.Count == 0 ||
                _errorMessage is not null)
            {
                return;
            }
        }

        _ = RefreshRuneValuesFromMarketAsync();
    }

    private async Task RefreshRuneValuesFromMarketAsync()
    {
        if (!await _marketRefreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            string? serverName =
                currentServerState.ServerName;

            if (string.IsNullOrWhiteSpace(serverName))
            {
                return;
            }

            using IServiceScope scope =
                serviceScopeFactory.CreateScope();

            MarketPriceService marketPriceService =
                scope.ServiceProvider.GetRequiredService<MarketPriceService>();

            IReadOnlyDictionary<
                (long DofusDbId, int Quantity),
                MarketPriceObservation> observations =
                    await marketPriceService
                        .GetLatestObservationsForServerAsync(
                            MarketObjectType.Rune,
                            serverName
                        );

            lock (_stateLock)
            {
                if (_errorMessage is not null)
                {
                    return;
                }

                foreach (KeyValuePair<long, AccumulatedRune> rune in _runes)
                {
                    MarketValueResult? value =
                        marketPriceService.CalculateValue(
                            rune.Key,
                            rune.Value.Quantity,
                            observations
                        );

                    rune.Value.Value =
                        value?.Value;
                }
            }

            PublishSnapshot();
        }
        finally
        {
            _marketRefreshLock.Release();
        }
    }

    private bool IsAlreadyScannedLocked(
        DofusCrushRuneCellDetectionResult
            cell)
    {
        foreach (
            ScannedRuneCellIdentity
            existing
            in _scannedRuneCells)
        {
            if (existing.ColumnIndex !=
                    cell.ColumnIndex ||
                existing.RuneLineIndex !=
                    cell.RuneLineIndex ||
                existing.RowOccurrence !=
                    cell.RowOccurrence)
            {
                continue;
            }

            if (Math.Abs(
                    existing.RowHeight -
                    cell.RowHeight
                ) > 12)
            {
                continue;
            }

            int fingerprintDistance =
                BitOperations.PopCount(
                    existing
                        .RowFingerprint ^
                    cell.RowFingerprint
                );

            if (fingerprintDistance <=
                RowFingerprintMaximumDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void StopMouseMonitoring()
    {
#if WINDOWS
        if (_mouseMonitorCancellation
            is null)
        {
            return;
        }

        try
        {
            _mouseMonitorCancellation
                .Cancel();
        }
        catch
        {
            // Rien.
        }

        _mouseMonitorCancellation
            .Dispose();

        _mouseMonitorCancellation =
            null;

        _mouseMonitorTask =
            null;
#endif
    }

    private async Task
        MonitorMouseAsync(
            CancellationToken
                cancellationToken)
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

                if (referencePosition
                    is null)
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
                        referencePosition
                            .Value
                            .X;

                    int dy =
                        cursor.Y -
                        referencePosition
                            .Value
                            .Y;

                    int distanceSquared =
                        dx * dx +
                        dy * dy;

                    if (distanceSquared >
                        MouseMovementThreshold *
                        MouseMovementThreshold)
                    {
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
                        TimeSpan
                            .FromMilliseconds(
                                MouseStableMilliseconds
                            ))
                    {
                        capturedForCurrentStop =
                            true;

                        await
                            CaptureAndQueueAtCursorAsync(
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
            catch (
                OperationCanceledException)
            {
                break;
            }
            catch
            {
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
            style |
            (nint)WsExLayered
        );

        SetLayeredWindowAttributes(
            hwnd,
            0,
            opacity,
            LwaAlpha
        );
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool
        GetCursorPos(
            out WinPoint point
        );

    [DllImport(
        "user32.dll",
        EntryPoint =
            "GetWindowLongPtrW"
    )]
    private static extern nint
        GetWindowLongPtr(
            IntPtr hwnd,
            int index
        );

    [DllImport(
        "user32.dll",
        EntryPoint =
            "SetWindowLongPtrW"
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
        ShowWindow(
            IntPtr hWnd,
            int nCmdShow
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

    private sealed record
        ScannedRuneCellIdentity(
            ulong RowFingerprint,
            int RowHeight,
            int RowOccurrence,
            int ColumnIndex,
            int RuneLineIndex
        );

    private sealed class
        AccumulatedRune
    {
        public required string Name
        {
            get;
            init;
        }

        public int Quantity
        {
            get;
            set;
        }

        public double? Value
        {
            get;
            set;
        }
    }

    private sealed record
        CapturedCursorWorkItem(
            long SessionId,
            string CaptureFilePath,
            int CaptureX,
            int CaptureY
        );
}
