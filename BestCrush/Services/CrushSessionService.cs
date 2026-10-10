#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
using System.Runtime.InteropServices;
#endif

using BestCrush.Models;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Overlay;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;

namespace BestCrush.Services;

public sealed record CrushSessionRuneLotLine(
    long Count,
    int LotQuantity,
    long LotPrice,
    bool IsEstimated,
    MarketPriceSource? PriceSource = null,
    DateTime? PriceObservedAtUtc = null
);

public sealed record CrushSessionRuneLine(
    string Name,
    int Quantity,
    double? Value,
    IReadOnlyList<CrushSessionRuneLotLine> Lots
);

public sealed record NetworkCrushRuneResult(
    long DofusDbId,
    int Quantity
);

public sealed record NetworkCrushResultLine(
    long EquipmentDofusDbId,
    double CoefficientPercent,
    IReadOnlyList<NetworkCrushRuneResult> Runes
);

public sealed record CrushSessionSnapshot(
    bool IsRunning,
    int ScannedCells,
    int IdleCaptures,
    int? LastCursorX,
    int? LastCursorY,
    int NetworkCrushCount,
    IReadOnlyList<CrushSessionRuneLine> Runes,
    double? TotalValue,
    double DiscountPercent,
    double? DiscountedTotalValue,
    string? ErrorMessage
);

public sealed class CrushSessionService(
    IServiceScopeFactory serviceScopeFactory,
    CurrentServerState currentServerState,
    OverlayControlBarService overlayControlBarService,
    OverlayLayoutSettingsService
        overlayLayoutSettingsService,
    IBestCrushSettingsProvider settingsProvider)
{
    private Window? _window;
    private CrushSessionOverlayPage? _page;

    private bool _isRunning;

    private readonly object
        _stateLock = new();

    private int _idleCaptureCount;

    private int? _lastCursorX;
    private int? _lastCursorY;

    private string? _errorMessage;

    private readonly List<
        ScannedRuneCellIdentity>
        _scannedRuneCells = [];

    private readonly Dictionary<
        long,
        AccumulatedRune>
        _runes = [];

    private readonly Dictionary<
        long,
        CrushCoefficientScanResult>
        _sessionEquipments = [];

    private DateTime
        _sessionStartedAtUtc;

    private string?
        _sessionServerName;

    private double
        _sessionDiscountPercent;

    private bool
        _historySaveRequested;

    private bool
        _historySaveStarted;

    private bool
        _historySaved;

    private int
        _activeCaptureCount;

    private int
        _pendingProcessingCount;

    private int
        _networkCrushCount;

    private long _sessionId;

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

#endif

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

        overlayControlBarService
            .RefreshState();
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

        overlayControlBarService
            .RefreshState();
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

    public void CloseAndReset()
    {
        _isRunning = false;

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

            _sessionStartedAtUtc =
                default;

            _sessionServerName =
                null;

            _sessionDiscountPercent =
                0.0;

            _historySaveRequested =
                false;

            _historySaveStarted =
                false;

            _historySaved =
                false;

            _activeCaptureCount =
                0;

            _pendingProcessingCount =
                0;

            _networkCrushCount =
                0;

            _sessionEquipments
                .Clear();

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
                                    rune.Value,
                                    rune.Lots
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

            double? discountedTotalValue =
                totalValue is double rawTotal
                    ? rawTotal *
                      (
                          1.0 -
                          _sessionDiscountPercent /
                          100.0
                      )
                    : null;

            return new CrushSessionSnapshot(
                _isRunning,
                _scannedRuneCells.Count,
                _idleCaptureCount,
                _lastCursorX,
                _lastCursorY,
                _networkCrushCount,
                runeLines,
                totalValue,
                _sessionDiscountPercent,
                discountedTotalValue,
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

    public async Task ApplyNetworkCrushAsync(
        IReadOnlyList<NetworkCrushResultLine> lines,
        DateTime observedAtUtc,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(serverName))
            return;

        using IServiceScope scope =
            serviceScopeFactory.CreateScope();

        RunesService runesService =
            scope.ServiceProvider
                .GetRequiredService<RunesService>();

        ItemsService itemsService =
            scope.ServiceProvider
                .GetRequiredService<ItemsService>();

        MarketPriceService marketPriceService =
            scope.ServiceProvider
                .GetRequiredService<MarketPriceService>();

        IReadOnlyCollection<Rune> runeCatalog =
            await runesService
                .GetLocalRunesAsync(
                    cancellationToken);

        Dictionary<long, Rune> runesById =
            runeCatalog.ToDictionary(
                rune => rune.DofusDbId);

        Dictionary<long, Equipment?> equipments =
            [];

        foreach (
            long equipmentId in lines
                .Select(line =>
                    line.EquipmentDofusDbId)
                .Where(id =>
                    id > 0)
                .Distinct())
        {
            equipments[equipmentId] =
                await itemsService
                    .GetEquipmentAsync(
                        equipmentId,
                        cancellationToken);
        }

        IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation>
            observations =
                await marketPriceService
                    .GetLatestObservationsForServerAsync(
                        MarketObjectType.Rune,
                        serverName,
                        cancellationToken);

        CrushHistorySessionWriteModel?
            history = null;

        lock (_stateLock)
        {
            // The Dofus result window represents one crushing
            // transaction. Mirror that behavior: each kci replaces
            // the previous result instead of accumulating forever.
            _sessionId++;

            _isRunning = false;
            _errorMessage = null;

            _scannedRuneCells.Clear();
            _runes.Clear();
            _sessionEquipments.Clear();

            _idleCaptureCount = 0;
            _lastCursorX = null;
            _lastCursorY = null;

            _networkCrushCount =
                lines.Count;

            _sessionStartedAtUtc =
                observedAtUtc;

            _sessionServerName =
                serverName;

            _sessionDiscountPercent =
                Math.Clamp(
                    settingsProvider
                        .CrushValueDiscountPercent,
                    0.0,
                    100.0);

            _historySaveRequested =
                false;
            _historySaveStarted =
                false;
            _historySaved =
                false;
            _activeCaptureCount =
                0;
            _pendingProcessingCount =
                0;

            int rowIndex = 0;

            foreach (
                NetworkCrushResultLine line
                in lines)
            {
                rowIndex++;

                if (line.EquipmentDofusDbId > 0)
                {
                    string equipmentName =
                        equipments.TryGetValue(
                                line.EquipmentDofusDbId,
                                out Equipment? equipment) &&
                            equipment is not null
                            ? equipment.Name
                            : $"Item #{line.EquipmentDofusDbId}";

                    _sessionEquipments[
                        line.EquipmentDofusDbId] =
                            new CrushCoefficientScanResult(
                                line.EquipmentDofusDbId,
                                equipmentName,
                                line.CoefficientPercent,
                                rowIndex);
                }

                foreach (
                    NetworkCrushRuneResult runeResult
                    in line.Runes)
                {
                    if (runeResult.DofusDbId <= 0 ||
                        runeResult.Quantity <= 0)
                    {
                        continue;
                    }

                    if (!_runes.TryGetValue(
                            runeResult.DofusDbId,
                            out AccumulatedRune?
                                accumulatedRune))
                    {
                        string runeName =
                            runesById.TryGetValue(
                                    runeResult.DofusDbId,
                                    out Rune? rune)
                                ? rune.Name
                                : $"Rune #{runeResult.DofusDbId}";

                        accumulatedRune =
                            new AccumulatedRune
                            {
                                Name =
                                    runeName
                            };

                        _runes[
                            runeResult.DofusDbId] =
                                accumulatedRune;
                    }

                    accumulatedRune.Quantity =
                        checked(
                            accumulatedRune.Quantity +
                            runeResult.Quantity);
                }
            }

            foreach (
                KeyValuePair<long, AccumulatedRune>
                    rune in _runes)
            {
                MarketValueResult? value =
                    marketPriceService
                        .CalculateValue(
                            rune.Key,
                            rune.Value.Quantity,
                            observations);

                rune.Value.Value =
                    value?.Value;

                rune.Value.Lots =
                    value is null
                        ? []
                        : BuildRuneLotBreakdown(
                            rune.Key,
                            rune.Value.Quantity,
                            observations);
            }

            // Each passive kci is already a complete and final result,
            // so it can be written to crushing history immediately.
            _historySaveRequested =
                true;

            history =
                TryPrepareHistorySaveLocked(
                    _sessionId);
        }

        PublishSnapshot();
        ScheduleHistorySave(
            history);

        // A passive kci is an explicit crushing result:
        // surface it immediately even if the user had hidden
        // the overlay previously.
        await MainThread
            .InvokeOnMainThreadAsync(
                Show
            );
    }

    private static IReadOnlyList<
        CrushSessionRuneLotLine>
        BuildRuneLotBreakdown(
            long dofusDbId,
            int quantity,
            IReadOnlyDictionary<
                (long DofusDbId, int Quantity),
                MarketPriceObservation> observations)
    {
        if (quantity <= 0)
        {
            return [];
        }

        MarketPriceObservation[] prices =
            observations
                .Where(entry =>
                    entry.Key.DofusDbId ==
                        dofusDbId)
                .Select(entry =>
                    entry.Value)
                .Where(observation =>
                    observation.Quantity > 0 &&
                    observation.Price > 0)
                .OrderByDescending(observation =>
                    observation.Quantity)
                .ToArray();

        if (prices.Length == 0)
        {
            return [];
        }

        long remaining =
            quantity;

        List<CrushSessionRuneLotLine>
            result = [];

        // Même décomposition que MarketPriceService.CalculateValue :
        // x1000 → x100 → x10 → x1, en prenant toujours
        // le plus gros lot effectivement disponible.
        foreach (
            MarketPriceObservation lot
            in prices)
        {
            if (remaining <
                lot.Quantity)
            {
                continue;
            }

            long count =
                remaining /
                lot.Quantity;

            if (count <= 0)
            {
                continue;
            }

            result.Add(
                new CrushSessionRuneLotLine(
                    count,
                    lot.Quantity,
                    lot.Price,
                    false,
                    lot.Source,
                    lot.ObservedAtUtc
                )
            );

            remaining -=
                count *
                lot.Quantity;

            if (remaining == 0)
            {
                break;
            }
        }

        // Si un type de lot manque, CalculateValue estime déjà
        // le reliquat au prix unitaire du plus petit lot disponible.
        // On conserve cette information dans le breakdown pour que
        // la formule Excel reproduise exactement l'estimation.
        if (remaining > 0)
        {
            MarketPriceObservation fallback =
                prices
                    .OrderBy(observation =>
                        observation.Quantity)
                    .First();

            result.Add(
                new CrushSessionRuneLotLine(
                    remaining,
                    fallback.Quantity,
                    fallback.Price,
                    true,
                    fallback.Source,
                    fallback.ObservedAtUtc
                )
            );
        }

        return result;
    }

    private CrushHistorySessionWriteModel?
        TryPrepareHistorySaveLocked(
            long sessionId)
    {
        if (sessionId !=
                _sessionId ||
            !_historySaveRequested ||
            _historySaveStarted ||
            _historySaved ||
            _activeCaptureCount > 0 ||
            _pendingProcessingCount > 0 ||
            _errorMessage is not null ||
            _runes.Count == 0 ||
            string.IsNullOrWhiteSpace(
                _sessionServerName))
        {
            return null;
        }

        IReadOnlyList<
            CrushHistoryEquipmentWriteModel>
            equipments =
                _sessionEquipments
                    .Values
                    .OrderBy(result =>
                        result.RowY)
                    .Select(result =>
                        new
                        CrushHistoryEquipmentWriteModel(
                            result.DofusDbId,
                            result.EquipmentName,
                            result.CoefficientPercent,
                            CoefficientSource
                                .InGameAutomatic,
                            result.RowY
                        )
                    )
                    .ToArray();

        IReadOnlyList<
            CrushHistoryRuneWriteModel>
            runes =
                _runes
                    .OrderBy(entry =>
                        entry.Value.Name)
                    .Select(entry =>
                        new
                        CrushHistoryRuneWriteModel(
                            entry.Key,
                            entry.Value.Name,
                            entry.Value.Quantity,
                            entry.Value.Value,
                            entry.Value.Lots
                                .Select(lot =>
                                    new
                                    CrushHistoryRuneLotWriteModel(
                                        lot.Count,
                                        lot.LotQuantity,
                                        lot.LotPrice,
                                        lot.IsEstimated,
                                        lot.PriceSource,
                                        lot.PriceObservedAtUtc
                                    )
                                )
                                .ToArray()
                        )
                    )
                    .ToArray();

        double? totalValue =
            runes.All(rune =>
                rune.Value is not null)
                ? runes.Sum(rune =>
                    rune.Value
                        .GetValueOrDefault())
                : null;

        double? discountedTotalValue =
            totalValue is double rawTotal
                ? rawTotal *
                  (
                      1.0 -
                      _sessionDiscountPercent /
                      100.0
                  )
                : null;

        _historySaveStarted =
            true;

        return new CrushHistorySessionWriteModel(
            _sessionServerName!,
            _sessionStartedAtUtc == default
                ? DateTime.UtcNow
                : _sessionStartedAtUtc,
            DateTime.UtcNow,
            totalValue,
            _sessionDiscountPercent,
            discountedTotalValue,
            equipments,
            runes
        );
    }

    private void ScheduleHistorySave(
        CrushHistorySessionWriteModel?
            history)
    {
        if (history is null)
        {
            return;
        }

        _ =
            PersistHistoryAsync(
                _sessionId,
                history
            );
    }

    private async Task PersistHistoryAsync(
        long sessionId,
        CrushHistorySessionWriteModel
            history)
    {
        try
        {
            using IServiceScope scope =
                serviceScopeFactory
                    .CreateScope();

            HistoryService historyService =
                scope
                    .ServiceProvider
                    .GetRequiredService<
                        HistoryService>();

            await historyService
                .SaveCrushSessionAsync(
                    history
                );

            lock (_stateLock)
            {
                if (sessionId ==
                    _sessionId)
                {
                    _historySaved =
                        true;
                }
            }
        }
        catch
        {
            lock (_stateLock)
            {
                if (sessionId ==
                    _sessionId)
                {
                    _historySaveStarted =
                        false;
                }
            }

            // L'historique ne doit jamais casser
            // une session F9 ou l'overlay.
        }
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

        public IReadOnlyList<
            CrushSessionRuneLotLine> Lots
        {
            get;
            set;
        } = [];
    }

}
