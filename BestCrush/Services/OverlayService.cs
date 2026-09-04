using BestCrush.Overlay;
using System.Threading.Channels;
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
    FocusedEquipmentState focusedEquipmentState,
    CrushSessionService crushSessionService,
    MarketDataChangeNotifier marketDataChangeNotifier,
    MarketCaptureOverlayService marketCaptureOverlayService,
    OverlayControlBarService overlayControlBarService,
    OverlayLayoutSettingsService overlayLayoutSettingsService)
{
    private Window? _overlayWindow;
    private OverlayPage? _overlayPage;

    // Le clic molette doit rester instantané.
    // Chaque clic démarre immédiatement sa propre
    // capture, puis le traitement lourd (OpenCV, OCR,
    // SQLite, calculs) est sérialisé sur un worker.
    private readonly Channel<MiddleClickReadWorkItem>
        _middleClickReadQueue =
            Channel.CreateUnbounded<MiddleClickReadWorkItem>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                }
            );

    private readonly CancellationTokenSource
        _middleClickReadCancellation = new();

    private readonly object
        _middleClickReadWorkerLock = new();

    private Task? _middleClickReadWorkerTask;

    
    // Les changements de marché peuvent déclencher plusieurs
    // recalculs presque simultanés (notifier + capture directe).
    // Un seul calcul de rentabilité est autorisé à la fois.
    private readonly System.Threading.SemaphoreSlim
        _profitabilityRefreshLock =
            new(1, 1);

    // Chaque demande reçoit une génération. Un calcul dont la
    // génération n'est plus la dernière ne doit jamais écraser
    // un résultat plus récent dans l'overlay.
    private long
        _profitabilityRefreshVersion;
private bool _hasF7VisibilitySnapshot;
    private bool _restoreProfitabilityAfterF7;
    private bool _restoreMarketAfterF7;
    private bool _restoreCrushAfterF7;

#if WINDOWS
    private AppWindow? _appWindow;

    private IntPtr _overlayHwnd = IntPtr.Zero;

    private bool _isOverlayVisible;

    private int _currentX = 40;
    private int _currentY = 70;

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
    private const int WhMouseLl = 14;

    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;

    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;

    private const int VkF7 = 0x76;
    private const int VkF9 = 0x78;

    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;

    private LowLevelKeyboardProc? _keyboardProc;
    private LowLevelMouseProc? _mouseProc;

    private bool _f7Pressed;
    private bool _f9Pressed;
    private bool _middleButtonPressed;
#endif

    public void Initialize()
    {
        if (_overlayWindow is not null)
        {
            return;
        }

        LoadStoredLayout();

        crushSessionService.CoefficientsUpdated -=
            OnCrushSessionCoefficientsUpdated;

        crushSessionService.CoefficientsUpdated +=
            OnCrushSessionCoefficientsUpdated;

        marketDataChangeNotifier.Changed -=
            OnMarketDataChanged;

        marketDataChangeNotifier.Changed +=
            OnMarketDataChanged;

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
            Y = 70
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

        overlayControlBarService.Initialize(
            new OverlayControlBarBindings(
                () => IsVisible,
                ToggleProfitabilityOverlayVisibility,
                () => marketCaptureOverlayService.IsVisible,
                ToggleMarketOverlayVisibility,
                () => crushSessionService.IsVisible,
                ToggleCrushOverlayVisibility,
                ActivateMainWindow
            )
        );
    }

    public bool IsVisible
    {
        get
        {
#if WINDOWS
            return
                _overlayWindow is not null &&
                _isOverlayVisible;
#else
            return _overlayWindow is not null;
#endif
        }
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

    public void ToggleAllOverlays()
    {
#if WINDOWS
        bool profitabilityVisible =
            IsVisible;

        bool marketVisible =
            marketCaptureOverlayService.IsVisible;

        bool crushVisible =
            crushSessionService.IsVisible;

        bool anyOverlayVisible =
            profitabilityVisible ||
            marketVisible ||
            crushVisible;

        if (anyOverlayVisible)
        {
            _restoreProfitabilityAfterF7 =
                profitabilityVisible;

            _restoreMarketAfterF7 =
                marketVisible;

            _restoreCrushAfterF7 =
                crushVisible;

            _hasF7VisibilitySnapshot =
                true;

            if (profitabilityVisible)
            {
                Hide();
            }

            if (marketVisible)
            {
                marketCaptureOverlayService.Hide();
            }

            if (crushVisible)
            {
                crushSessionService.Hide();
            }

            return;
        }

        if (!_hasF7VisibilitySnapshot)
        {
            return;
        }

        if (_restoreProfitabilityAfterF7)
        {
            Show();
        }

        if (_restoreMarketAfterF7)
        {
            marketCaptureOverlayService.Show();
        }

        if (_restoreCrushAfterF7)
        {
            crushSessionService.Show();
        }

        ClearF7VisibilitySnapshot();
#endif
    }

    public void RequestRead()
    {
        if (!currentServerState.HasSelectedServer)
        {
            PostUi(
                marketCaptureOverlayService
                    .ShowServerSelectionRequired
            );

            return;
        }

        DofusWindowInfo? dofusWindow =
            dofusWindowService
                .GetActiveDofusWindow();

        if (dofusWindow is null)
        {
            PostUi(
                marketCaptureOverlayService
                    .ShowReadCancelled
            );

            return;
        }

        PostUi(
            () =>
                marketCaptureOverlayService
                    .ShowCaptureStarted(
                        dofusWindow
                    )
        );

        EnsureMiddleClickReadWorker();

        // La capture démarre immédiatement au moment
        // du clic. Elle n'attend jamais que l'OCR ou
        // le calcul du clic précédent soit terminé.
        Task<DofusCaptureResult> captureTask =
            Task.Run(
                async () =>
                    await dofusCaptureService
                        .CaptureAsync(
                            dofusWindow,
                            _middleClickReadCancellation
                                .Token
                        )
                        .ConfigureAwait(false),
                _middleClickReadCancellation.Token
            );

        MiddleClickReadWorkItem workItem =
            new(
                captureTask
            );

        if (!_middleClickReadQueue
            .Writer
            .TryWrite(
                workItem
            ))
        {
            // La file a été fermée alors que la
            // capture était déjà partie : elle ne
            // sera jamais consommée par le worker.
            _ = CleanupAbandonedCaptureAsync(
                captureTask
            );
        }
    }

    private void EnsureMiddleClickReadWorker()
    {
        if (_middleClickReadWorkerTask is not null)
        {
            return;
        }

        lock (_middleClickReadWorkerLock)
        {
            if (_middleClickReadWorkerTask is not null)
            {
                return;
            }

            _middleClickReadWorkerTask =
                Task.Run(
                    () =>
                        ProcessMiddleClickReadQueueAsync(
                            _middleClickReadCancellation
                                .Token
                        )
                );
        }
    }

    private async Task ProcessMiddleClickReadQueueAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                MiddleClickReadWorkItem workItem
                in _middleClickReadQueue
                    .Reader
                    .ReadAllAsync(
                        cancellationToken
                    )
                    .ConfigureAwait(false))
            {
                DofusCaptureResult? capture =
                    null;

                try
                {
                    // Les captures ont déjà commencé en
                    // parallèle. Elles sont cependant analysées
                    // dans l'ordre des clics afin de conserver
                    // un focus et des écritures BDD déterministes.
                    capture =
                        await workItem
                            .CaptureTask
                            .ConfigureAwait(false);

                    await ProcessCapturedReadAsync(
                        capture,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    PostUi(
                        () =>
                            marketCaptureOverlayService
                                .ShowCaptureFailed(
                                    ex.Message
                                )
                    );
                }
                finally
                {
                    // ProcessCapturedReadAsync est le dernier
                    // consommateur de la capture et de tous
                    // les crops générés dans son dossier.
                    if (capture is not null)
                    {
                        dofusCaptureService
                            .DeleteCaptureArtifacts(
                                capture.FilePath
                            );
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Fermeture de BestCrush.
        }
    }

    private async Task CleanupAbandonedCaptureAsync(
        Task<DofusCaptureResult> captureTask)
    {
        try
        {
            DofusCaptureResult capture =
                await captureTask
                    .ConfigureAwait(false);

            dofusCaptureService
                .DeleteCaptureArtifacts(
                    capture.FilePath
                );
        }
        catch
        {
            // Si CaptureAsync a échoué après avoir
            // créé son dossier, DofusCaptureService
            // effectue déjà son propre nettoyage.
        }
    }

    private async Task ProcessCapturedReadAsync(
        DofusCaptureResult capture,
        CancellationToken cancellationToken)
    {
        try
        {
            PostUi(
                () =>
                    marketCaptureOverlayService
                        .ShowCaptureSuccess(
                            capture
                        )
            );


            // Priorité absolue à l'HDV.
            //
            // Les panneaux Dofus partagent une palette
            // et une géométrie proches. La détection de
            // concassage possède un fallback géométrique ;
            // elle ne doit donc jamais prendre la main sur
            // un HDV déjà reconnu avec son template dédié.
            DofusMarketPanelDetectionResult?
                preDetectedMarketPanel =
                    await dofusMarketPanelDetectionService
                        .DetectMarketPanelAsync(
                            capture.FilePath
                        );

            DofusPanelDetectionResult? panel =
                preDetectedMarketPanel is null
                    ? await dofusPanelDetectionService
                        .DetectCrushResultPanelAsync(
                            capture.FilePath
                        )
                    : null;

                    if (panel is null)
                    {
                        // Hors HDV seulement, une infobulle
                        // d'équipement peut servir directement
                        // à définir le focus. En HDV, le panneau
                        // a priorité afin d'enregistrer aussi les
                        // prix affichés.
                        if (preDetectedMarketPanel is null)
                        {
                        using IServiceScope tooltipScope =
                            serviceScopeFactory.CreateScope();

                        DofusItemTooltipDetectionService
                            tooltipDetectionService =
                                tooltipScope.ServiceProvider
                                    .GetRequiredService<
                                        DofusItemTooltipDetectionService>();

                        DofusItemTooltipDetectionResult
                            tooltipDetection =
                                await tooltipDetectionService
                                    .DetectAsync(
                                        capture.FilePath
                                    );

                        if (tooltipDetection.Candidates.Count > 1)
                        {
                            PostUi(
                                () =>
                                {
                                    marketCaptureOverlayService
                                        .ShowMultipleTooltipsDetected(
                                            tooltipDetection.Candidates.Count
                                        );
                                }
                            );

                            return;
                        }

                        if (tooltipDetection.Candidates.Count == 1)
                        {
                            DofusItemTooltipCandidate candidate =
                                tooltipDetection.Candidates[0];

                            if (candidate.Recognition is not null)
                            {
                                Equipment tooltipEquipment =
                                    candidate
                                        .Recognition
                                        .Equipment;

                                focusedEquipmentState
                                    .SetEquipment(
                                        tooltipEquipment
                                    );

                                await RefreshFocusedProfitabilityAsync();

                                await MainThread
                                    .InvokeOnMainThreadAsync(
                                        Show
                                    );

                                PostUi(
                                    () =>
                                    {
                                        marketCaptureOverlayService
                                            .ShowTooltipEquipmentFocused(
                                                tooltipEquipment.Name,
                                                candidate.Recognition.Confidence
                                            );
                                    }
                                );

                                return;
                            }

                            // Une infobulle est présente mais ce
                            // n'est pas un équipement reconnu.
                            //
                            // On ne bloque surtout pas les autres
                            // usages du clic molette : HDV rune, ressource, etc.
                        }
                        }

                        DofusMarketPanelDetectionResult? marketPanel =
                            preDetectedMarketPanel
                            ?? await dofusMarketPanelDetectionService
                                .DetectMarketPanelAsync(
                                    capture.FilePath
                                );

                        if (marketPanel is not null)
                        {
                            bool isSellTab =
                                marketPanel.IsSellPanel;

                            if (isSellTab)
                            {
                                string sellItemNameRegion =
                                    await dofusImageRegionService
                                        .ExtractRegionAsync(
                                            marketPanel.DebugImagePath,
                                            new RelativeImageRegion(
                                                X: 0.09,
                                                Y: 0.105,
                                                Width: 0.72,
                                                Height: 0.060
                                            ),
                                            "hdv-sell-item-name"
                                        );

                                string recognizedSellItemName =
                                    await dofusOcrService
                                        .RecognizeUpscaledTextAsync(
                                            sellItemNameRegion
                                        );

                                recognizedSellItemName =
                                    recognizedSellItemName
                                        .Split(
                                            ['\r', '\n'],
                                            StringSplitOptions.RemoveEmptyEntries
                                        )
                                        .Select(line => line.Trim())
                                        .FirstOrDefault(
                                            line =>
                                                !line.StartsWith(
                                                    "NIV",
                                                    StringComparison.OrdinalIgnoreCase
                                                ) &&
                                                !line.StartsWith(
                                                    "Niv",
                                                    StringComparison.OrdinalIgnoreCase
                                                )
                                        )
                                    ?? string.Empty;

                                if (string.IsNullOrWhiteSpace(
                                    recognizedSellItemName))
                                {
                                    PostUi(
                                        () =>
                                        {
                                            marketCaptureOverlayService.ShowAuxiliaryMarketReadFailed(
                                                                                        "Rune non reconnue"
                                                                                    );
                                        }
                                    );

                                    return;
                                }

                                using IServiceScope sellScope =
                                    serviceScopeFactory.CreateScope();

                                DofusRuneRecognitionService
                                    sellRuneRecognitionService =
                                        sellScope.ServiceProvider
                                            .GetRequiredService<
                                                DofusRuneRecognitionService>();

                                MarketPriceService
                                    sellMarketPriceService =
                                        sellScope.ServiceProvider
                                            .GetRequiredService<
                                                MarketPriceService>();

                                RuneRecognitionResult? sellRuneRecognition =
                                    await sellRuneRecognitionService
                                        .RecognizeRuneAsync(
                                            recognizedSellItemName
                                        );

                                if (sellRuneRecognition is null)
                                {
                                    PostUi(
                                        () =>
                                        {
                                            marketCaptureOverlayService.ShowAuxiliaryMarketReadFailed(
                                                                                        recognizedSellItemName
                                                                                    );
                                        }
                                    );

                                    return;
                                }

                                IReadOnlyList<DofusMarketLot> sellLots =
                                    await dofusMarketLotReaderService
                                        .ReadSellMaterialLotsAsync(
                                            marketPanel.DebugImagePath
                                        );

                                if (sellLots.Count == 0)
                                {
                                    PostUi(
                                        () =>
                                        {
                                            marketCaptureOverlayService.ShowAuxiliaryMarketReadFailed(
                                                                                        sellRuneRecognition
                                                                                            .Rune
                                                                                            .Name
                                                                                    );
                                        }
                                    );

                                    return;
                                }

                                string sellServerName =
                                    currentServerState.ServerName!;

                                foreach (DofusMarketLot lot in sellLots)
                                {
                                    await sellMarketPriceService
                                        .AddObservationAsync(
                                            MarketObjectType.Rune,
                                            sellRuneRecognition
                                                .Rune
                                                .DofusDbId,
                                            sellServerName,
                                            lot.Price,
                                            lot.Quantity,
                                            MarketPriceSource
                                                .InGameAutomatic
                                        );
                                }

                                marketDataChangeNotifier.Notify(
                                    MarketObjectType.Rune,
                                    sellRuneRecognition.Rune.DofusDbId,
                                    sellServerName,
                                    0
                                );
                                PostUi(
                                    () =>
                                    {
                                        marketCaptureOverlayService.ShowAuxiliaryMarketDataRecorded(
                                                                                sellRuneRecognition
                                                                                    .Rune
                                                                                    .Name,
                                                                                sellLots.Count,
                                                                                focusedEquipmentState
                                                                                    .Equipment?
                                                                                    .Name
                                                                            );
                                    }
                                );

                                await RefreshFocusedProfitabilityAsync();

                                return;
                            }
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

                            string recognizedMarketItemText =
                                await dofusOcrService
                                    .RecognizeUpscaledTextAsync(
                                        marketItemNameRegion
                                    );

                            string recognizedMarketItemName =
                                ExtractMarketPrimaryName(
                                    recognizedMarketItemText
                                );

                            MarketObjectHint explicitMarketHint =
                                DetectExplicitMarketObjectHint(
                                    recognizedMarketItemText
                                );

                            long? recognizedMarketPrice =
                                await dofusOcrService
                                    .RecognizePriceAsync(
                                        marketFirstPriceRegion
                                    );

                                    if (string.IsNullOrWhiteSpace(
                                            recognizedMarketItemName))
                                    {
                                        PostUi(
                                            () =>
                                            {
                                                marketCaptureOverlayService.ShowMarketEquipmentRead(
                                                                                            recognizedMarketItemName,
                                                                                            null
                                                                                        );
                                            }
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

                            // Les trois familles sont reconnues AVANT de
                            // décider quoi que ce soit.
                            //
                            // On ne classe plus par simple priorité
                            // rune -> ressource -> équipement : un fuzzy
                            // match d'une famille ne doit jamais absorber
                            // silencieusement un objet d'une autre famille.
                            RuneRecognitionResult? runeRecognition =
                                await runeRecognitionService
                                    .RecognizeRuneAsync(
                                        recognizedMarketItemName
                                    );

                            ResourceRecognitionResult? resourceRecognition =
                                await resourceRecognitionService
                                    .RecognizeResourceAsync(
                                        recognizedMarketItemName
                                    );

                            ItemRecognitionResult? marketRecognition =
                                await marketItemRecognitionService
                                    .RecognizeEquipmentAsync(
                                        recognizedMarketItemName
                                    );

                            // Le type affiche par Dofus est une preuve forte.
                            // Exemple : Rune Invo ... Rune de forgemagie.
                            if (explicitMarketHint ==
                                    MarketObjectHint.Rune &&
                                runeRecognition is not null)
                            {
                                resourceRecognition = null;
                            }
                            else if (
                                explicitMarketHint ==
                                    MarketObjectHint.Resource &&
                                resourceRecognition is not null)
                            {
                                runeRecognition = null;
                            }

                            bool runeExact =
                                runeRecognition is not null &&
                                MarketNamesEquivalent(
                                    recognizedMarketItemName,
                                    runeRecognition.Rune.Name
                                );

                            bool resourceExact =
                                resourceRecognition is not null &&
                                MarketNamesEquivalent(
                                    recognizedMarketItemName,
                                    resourceRecognition.Resource.Name
                                );

                            bool equipmentExact =
                                explicitMarketHint ==
                                    MarketObjectHint.None &&
                                marketRecognition is not null &&
                                MarketNamesEquivalent(
                                    recognizedMarketItemName,
                                    marketRecognition.Equipment.Name
                                );

                            double runeScore =
                                runeRecognition is null
                                    ? 0
                                    : GetAdjustedMaterialConfidence(
                                        recognizedMarketItemName,
                                        runeRecognition.Rune.Name,
                                        runeRecognition.Confidence
                                    );

                            double resourceScore =
                                resourceRecognition is null
                                    ? 0
                                    : GetAdjustedMaterialConfidence(
                                        recognizedMarketItemName,
                                        resourceRecognition.Resource.Name,
                                        resourceRecognition.Confidence
                                    );

                            // La lecture des lots est aussi une preuve
                            // structurelle :
                            //
                            // matériau -> x1 / x10 / x100 / x1000,
                            // équipement -> plusieurs offres individuelles x1.
                            //
                            // Le lecteur sécurisé rejette déjà les quantités
                            // dupliquées, donc quatre lignes x1 donnent
                            // volontairement zéro lot matière.
                            IReadOnlyList<DofusMarketLot> materialLots =
                                [];

                            if (!equipmentExact &&
                                (
                                    runeRecognition is not null ||
                                    resourceRecognition is not null
                                ))
                            {
                                materialLots =
                                    await dofusMarketLotReaderService
                                        .ReadMaterialLotsAsync(
                                            marketPanel.DebugImagePath
                                        );
                            }

                            bool hasStrongMaterialGeometry =
                                materialLots.Any(
                                    lot =>
                                        lot.Quantity != 1
                                );

                            bool hasOnlySingleX1MaterialGeometry =
                                materialLots.Count == 1 &&
                                materialLots[0].Quantity == 1;

                            string? selectedMaterialKind =
                                null;

                            double selectedMaterialScore =
                                0;

                            bool selectedMaterialExact =
                                false;

                            if (runeRecognition is not null &&
                                resourceRecognition is null)
                            {
                                selectedMaterialKind =
                                    "Rune";

                                selectedMaterialScore =
                                    runeScore;

                                selectedMaterialExact =
                                    runeExact;
                            }
                            else if (
                                resourceRecognition is not null &&
                                runeRecognition is null)
                            {
                                selectedMaterialKind =
                                    "Resource";

                                selectedMaterialScore =
                                    resourceScore;

                                selectedMaterialExact =
                                    resourceExact;
                            }
                            else if (
                                runeRecognition is not null &&
                                resourceRecognition is not null)
                            {
                                if (runeExact &&
                                    !resourceExact)
                                {
                                    selectedMaterialKind =
                                        "Rune";

                                    selectedMaterialScore =
                                        runeScore;

                                    selectedMaterialExact =
                                        true;
                                }
                                else if (
                                    resourceExact &&
                                    !runeExact)
                                {
                                    selectedMaterialKind =
                                        "Resource";

                                    selectedMaterialScore =
                                        resourceScore;

                                    selectedMaterialExact =
                                        true;
                                }
                                else if (
                                    Math.Abs(
                                        runeScore -
                                        resourceScore
                                    ) >= 0.05)
                                {
                                    if (runeScore >
                                        resourceScore)
                                    {
                                        selectedMaterialKind =
                                            "Rune";

                                        selectedMaterialScore =
                                            runeScore;

                                        selectedMaterialExact =
                                            runeExact;
                                    }
                                    else
                                    {
                                        selectedMaterialKind =
                                            "Resource";

                                        selectedMaterialScore =
                                            resourceScore;

                                        selectedMaterialExact =
                                            resourceExact;
                                    }
                                }
                            }

                            bool selectEquipment =
                                equipmentExact;

                            string decisionReason =
                                equipmentExact
                                    ? "exact equipment name"
                                    : string.Empty;

                            if (!selectEquipment &&
                                explicitMarketHint ==
                                    MarketObjectHint.None &&
                                marketRecognition is not null)
                            {
                                if (materialLots.Count == 0)
                                {
                                    // Cas typique d'un équipement :
                                    // plusieurs offres x1 ont été vues et
                                    // rejetées comme quantité dupliquée.
                                    selectEquipment =
                                        true;

                                    decisionReason =
                                        "no valid material lots; equipment candidate available";
                                }
                                else if (
                                    hasOnlySingleX1MaterialGeometry &&
                                    !selectedMaterialExact)
                                {
                                    // Une seule ligne x1 ne permet pas de
                                    // distinguer à elle seule un matériau
                                    // d'un équipement ayant une seule offre.
                                    //
                                    // On exige donc une avance significative
                                    // du candidat matériau. Un prefix-match
                                    // est plafonné à 0.92 par
                                    // GetAdjustedMaterialConfidence.
                                    if (marketRecognition.Confidence >=
                                        selectedMaterialScore - 0.05)
                                    {
                                        selectEquipment =
                                            true;

                                        decisionReason =
                                            "single x1 is ambiguous; equipment candidate is at least as credible";
                                    }
                                }
                            }

                            if (selectEquipment)
                            {
                                await WriteMarketClassificationDebugAsync(
                                    marketPanel.DebugImagePath,
                                    recognizedMarketItemName,
                                    recognizedMarketPrice,
                                    runeRecognition,
                                    runeScore,
                                    runeExact,
                                    resourceRecognition,
                                    resourceScore,
                                    resourceExact,
                                    marketRecognition,
                                    equipmentExact,
                                    materialLots,
                                    "Equipment",
                                    decisionReason
                                );

                                // On continue plus bas dans le chemin
                                // équipement déjà existant.
                            }
                            else if (
                                materialLots.Count > 0 &&
                                selectedMaterialKind == "Rune" &&
                                runeRecognition is not null &&
                                (
                                    explicitMarketHint ==
                                        MarketObjectHint.Rune ||
                                    selectedMaterialExact ||
                                    selectedMaterialScore >= 0.85
                                ))
                            {
                                await WriteMarketClassificationDebugAsync(
                                    marketPanel.DebugImagePath,
                                    recognizedMarketItemName,
                                    recognizedMarketPrice,
                                    runeRecognition,
                                    runeScore,
                                    runeExact,
                                    resourceRecognition,
                                    resourceScore,
                                    resourceExact,
                                    marketRecognition,
                                    equipmentExact,
                                    materialLots,
                                    "Rune",
                                    selectedMaterialExact
                                        ? "exact rune name + valid material lots"
                                        : hasStrongMaterialGeometry
                                            ? "best rune candidate + strong material lot geometry"
                                            : "best rune candidate + valid material lot"
                                );

                                string runeServerName =
                                    currentServerState.ServerName!;

                                foreach (DofusMarketLot lot in materialLots)
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

                                marketDataChangeNotifier.Notify(
                                    MarketObjectType.Rune,
                                    runeRecognition.Rune.DofusDbId,
                                    runeServerName,
                                    0
                                );

                                PostUi(
                                    () =>
                                    {
                                        marketCaptureOverlayService
                                            .ShowAuxiliaryMarketDataRecorded(
                                                runeRecognition.Rune.Name,
                                                materialLots.Count,
                                                focusedEquipmentState
                                                    .Equipment?
                                                    .Name
                                            );
                                    }
                                );

                                await RefreshFocusedProfitabilityAsync();

                                return;
                            }
                            else if (
                                materialLots.Count > 0 &&
                                selectedMaterialKind == "Resource" &&
                                resourceRecognition is not null &&
                                (
                                    explicitMarketHint ==
                                        MarketObjectHint.Resource ||
                                    selectedMaterialExact ||
                                    selectedMaterialScore >= 0.85
                                ))
                            {
                                await WriteMarketClassificationDebugAsync(
                                    marketPanel.DebugImagePath,
                                    recognizedMarketItemName,
                                    recognizedMarketPrice,
                                    runeRecognition,
                                    runeScore,
                                    runeExact,
                                    resourceRecognition,
                                    resourceScore,
                                    resourceExact,
                                    marketRecognition,
                                    equipmentExact,
                                    materialLots,
                                    "Resource",
                                    selectedMaterialExact
                                        ? "exact resource name + valid material lots"
                                        : hasStrongMaterialGeometry
                                            ? "best resource candidate + strong material lot geometry"
                                            : "best resource candidate + valid material lot"
                                );

                                string resourceServerName =
                                    currentServerState.ServerName!;

                                foreach (DofusMarketLot lot in materialLots)
                                {
                                    await hdvMarketPriceService
                                        .AddObservationAsync(
                                            MarketObjectType.Resource,
                                            resourceRecognition.Resource.DofusDbId,
                                            resourceServerName,
                                            lot.Price,
                                            lot.Quantity,
                                            MarketPriceSource.InGameAutomatic
                                        );
                                }

                                marketDataChangeNotifier.Notify(
                                    MarketObjectType.Resource,
                                    resourceRecognition.Resource.DofusDbId,
                                    resourceServerName,
                                    0
                                );

                                PostUi(
                                    () =>
                                    {
                                        marketCaptureOverlayService
                                            .ShowAuxiliaryMarketDataRecorded(
                                                resourceRecognition.Resource.Name,
                                                materialLots.Count,
                                                focusedEquipmentState
                                                    .Equipment?
                                                    .Name
                                            );
                                    }
                                );

                                await RefreshFocusedProfitabilityAsync();

                                return;
                            }
                            else
                            {
                                await WriteMarketClassificationDebugAsync(
                                    marketPanel.DebugImagePath,
                                    recognizedMarketItemName,
                                    recognizedMarketPrice,
                                    runeRecognition,
                                    runeScore,
                                    runeExact,
                                    resourceRecognition,
                                    resourceScore,
                                    resourceExact,
                                    marketRecognition,
                                    equipmentExact,
                                    materialLots,
                                    "Rejected",
                                    "classification ambiguous or unsupported by market geometry"
                                );

                                if (marketRecognition is null)
                                {
                                    if (recognizedMarketPrice is long detectedPrice)
                                    {
                                        PostUi(
                                            () =>
                                            {
                                                marketCaptureOverlayService
                                                    .ShowMarketEquipmentRecognitionFailed(
                                                        recognizedMarketItemName,
                                                        detectedPrice
                                                    );
                                            }
                                        );
                                    }
                                    else
                                    {
                                        PostUi(
                                            () =>
                                            {
                                                marketCaptureOverlayService
                                                    .ShowAuxiliaryMarketReadFailed(
                                                        recognizedMarketItemName
                                                    );
                                            }
                                        );
                                    }

                                    return;
                                }

                                // Un équipement candidat existe mais les
                                // preuves sont contradictoires : on refuse
                                // toute écriture plutôt que de choisir
                                // arbitrairement.
                                PostUi(
                                    () =>
                                    {
                                        marketCaptureOverlayService
                                            .ShowAuxiliaryMarketReadFailed(
                                                recognizedMarketItemName
                                            );
                                    }
                                );

                                return;
                            }
                            Equipment marketEquipment =
                                marketRecognition.Equipment;
                            if (recognizedMarketPrice is null ||
                                recognizedMarketPrice <= 0)
                            {
                                PostUi(
                                    () =>
                                    {
                                        marketCaptureOverlayService
                                            .ShowMarketEquipmentRead(
                                                marketEquipment.Name,
                                                null
                                            );
                                    }
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

                            marketDataChangeNotifier.Notify(
                                MarketObjectType.Equipment,
                                marketEquipment.DofusDbId,
                                hdvServerName,
                                1
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

                            PostUi(
                                () =>
                                {
                                    marketCaptureOverlayService.ShowMarketEquipmentRecorded(
                                                                    marketEquipment.Name,
                                                                    marketRecognition.Confidence,
                                                                    capturedPrice.Price,
                                                                    appliedPrice.Price,
                                                                    manualPricePreserved
                                                                );
                                }
                            );
                            await RefreshFocusedProfitabilityAsync();

                            await MainThread
                                .InvokeOnMainThreadAsync(
                                    Show
                                );

                            return;
                        }

                        PostUi(
                            () =>
                            {
                                marketCaptureOverlayService.ShowPanelNotDetected();
                            }
                        );

                        return;
                    }

            PostUi(
                () =>
                {
                    marketCaptureOverlayService.ShowPanelDetected(
                                    panel
                                );
                }
            );

            CrushRowDetectionResult? lastRow =
                await dofusCrushRowDetectionService
                    .DetectLastRowAsync(
                        panel.DebugImagePath
                    );

            if (lastRow is null)
            {
                PostUi(
                    () =>
                    {
                        marketCaptureOverlayService.ShowCrushRowNotDetected();
                    }
                );

                return;
            }

            PostUi(
                () =>
                {
                    marketCaptureOverlayService.ShowLastCrushRowDetected(
                                    lastRow
                                );
                }
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

                PostUi(
                    () =>
                    {
                        marketCaptureOverlayService.ShowCrushOcrResult(
                                            recognizedItemName,
                                            recognizedCoefficient
                                        );
                    }
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
                    PostUi(
                        () =>
                        {
                            marketCaptureOverlayService.ShowEquipmentRecognitionFailed(
                                                    recognizedItemName
                                                );
                        }
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
                    PostUi(
                        () =>
                        {
                            marketCaptureOverlayService.ShowServerNotSelected();
                        }
                    );

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

                PostUi(
                    () =>
                    {
                        marketCaptureOverlayService.ShowRecognizedEquipment(
                                            equipment.Name,
                                            recognition.Confidence,
                                            recognizedCoefficient.Value,
                                            appliedCoefficient.CoefficientPercent,
                                            manualCoefficientPreserved,
                                            equipmentPrice?.Price
                                        );
                    }
                );
                await RefreshFocusedProfitabilityAsync();

                await MainThread
                    .InvokeOnMainThreadAsync(
                        Show
                    );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PostUi(
                () =>
                    marketCaptureOverlayService
                        .ShowCaptureFailed(
                            ex.Message
                        )
            );
        }
    }

    private enum MarketObjectHint
    {
        None,
        Rune,
        Resource
    }

    private static string ExtractMarketPrimaryName(
        string recognizedText)
    {
        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return string.Empty;
        }

        string compact =
            System.Text.RegularExpressions.Regex.Replace(
                recognizedText,
                @"\s+",
                " "
            ).Trim();

        // Dans l'HDV, tout ce qui suit "Niv." / "Niveau"
        // appartient aux métadonnées et non au nom de l'objet.
        //
        // On ne dépend volontairement plus de la lecture du niveau :
        // l'OCR peut confondre "1" avec "l", comme dans "Niv.l".
        // Dès que le marqueur de niveau est détecté, le nom s'arrête.
        string primaryName =
            System.Text.RegularExpressions.Regex.Replace(
                compact,
                @"\s+(?:NIVEAU|NIV\.?).*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            ).Trim();

        return string.IsNullOrWhiteSpace(primaryName)
            ? compact
            : primaryName;
    }

    private static MarketObjectHint DetectExplicitMarketObjectHint(
        string recognizedText)
    {
        string normalized =
            NormalizeMarketName(recognizedText);

        if (normalized.Contains(
            "rune de forgemagie",
            StringComparison.Ordinal))
        {
            return MarketObjectHint.Rune;
        }

        if (normalized.Contains(
            "ressource",
            StringComparison.Ordinal))
        {
            return MarketObjectHint.Resource;
        }

        return MarketObjectHint.None;
    }

    private static double
        GetAdjustedMaterialConfidence(
            string recognizedName,
            string candidateName,
            double confidence)
    {
        if (MarketNamesEquivalent(
            recognizedName,
            candidateName))
        {
            return 1.0;
        }

        string normalizedRecognized =
            NormalizeMarketName(
                recognizedName
            );

        string normalizedCandidate =
            NormalizeMarketName(
                candidateName
            );

        // Les services Rune/Ressource utilisent historiquement
        // un prefix-match à 1.0 pour tolérer du texte OCR
        // supplémentaire. Pour la classification inter-familles,
        // ce n'est PAS une correspondance exacte.
        if (normalizedRecognized.Length >
                normalizedCandidate.Length &&
            normalizedRecognized.StartsWith(
                normalizedCandidate + " ",
                StringComparison.Ordinal
            ))
        {
            return Math.Min(
                confidence,
                0.80
            );
        }

        return confidence;
    }

    private static bool MarketNamesEquivalent(
        string first,
        string second)
    {
        return string.Equals(
            NormalizeMarketName(
                first
            ),
            NormalizeMarketName(
                second
            ),
            StringComparison.Ordinal
        );
    }

    private static string NormalizeMarketName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
            value))
        {
            return string.Empty;
        }

        string decomposed =
            value
                .Trim()
                .ToLowerInvariant()
                .Replace(
                    '’',
                    '\''
                )
                .Normalize(
                    System.Text
                        .NormalizationForm
                        .FormD
                );

        System.Text.StringBuilder builder =
            new();

        bool previousWasSpace =
            false;

        foreach (char character in decomposed)
        {
            System.Globalization.UnicodeCategory
                category =
                    System.Globalization
                        .CharUnicodeInfo
                        .GetUnicodeCategory(
                            character
                        );

            if (category ==
                System.Globalization
                    .UnicodeCategory
                    .NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(
                character))
            {
                builder.Append(
                    character
                );

                previousWasSpace =
                    false;

                continue;
            }

            if (!previousWasSpace &&
                builder.Length > 0)
            {
                builder.Append(
                    ' '
                );

                previousWasSpace =
                    true;
            }
        }

        return builder
            .ToString()
            .Trim();
    }

    private static async Task
        WriteMarketClassificationDebugAsync(
            string marketPanelImagePath,
            string recognizedName,
            long? firstPrice,
            RuneRecognitionResult? runeRecognition,
            double runeScore,
            bool runeExact,
            ResourceRecognitionResult? resourceRecognition,
            double resourceScore,
            bool resourceExact,
            ItemRecognitionResult? equipmentRecognition,
            bool equipmentExact,
            IReadOnlyList<DofusMarketLot> materialLots,
            string decision,
            string reason)
    {
        try
        {
            string directory =
                Path.GetDirectoryName(
                    marketPanelImagePath
                ) ??
                Path.GetTempPath();

            string debugPath =
                Path.Combine(
                    directory,
                    "hdv-classification.txt"
                );

            System.Text.StringBuilder debug =
                new();

            debug.AppendLine(
                "BESTCRUSH - HDV BUY CLASSIFICATION"
            );

            debug.AppendLine(
                $"OCR name: {recognizedName}"
            );

            debug.AppendLine(
                $"First price: {(firstPrice?.ToString() ?? "null")}"
            );

            debug.AppendLine();

            debug.AppendLine(
                runeRecognition is null
                    ? "Rune candidate: null"
                    : $"Rune candidate: {runeRecognition.Rune.Name} | raw={runeRecognition.Confidence:0.000} | adjusted={runeScore:0.000} | exact={runeExact}"
            );

            debug.AppendLine(
                resourceRecognition is null
                    ? "Resource candidate: null"
                    : $"Resource candidate: {resourceRecognition.Resource.Name} | raw={resourceRecognition.Confidence:0.000} | adjusted={resourceScore:0.000} | exact={resourceExact}"
            );

            debug.AppendLine(
                equipmentRecognition is null
                    ? "Equipment candidate: null"
                    : $"Equipment candidate: {equipmentRecognition.Equipment.Name} | score={equipmentRecognition.Confidence:0.000} | exact={equipmentExact}"
            );

            debug.AppendLine();

            debug.AppendLine(
                $"Accepted material lots: {materialLots.Count}"
            );

            foreach (
                DofusMarketLot lot
                in materialLots)
            {
                debug.AppendLine(
                    $"  x{lot.Quantity} = {lot.Price}"
                );
            }

            debug.AppendLine();

            debug.AppendLine(
                $"DECISION: {decision}"
            );

            debug.AppendLine(
                $"REASON: {reason}"
            );

            await File.WriteAllTextAsync(
                debugPath,
                debug.ToString()
            );
        }
        catch
        {
            // Le debug ne doit jamais empêcher
            // une lecture HDV.
        }
    }
    private static void PostUi(
        Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();

            return;
        }

        MainThread
            .BeginInvokeOnMainThread(
                action
            );
    }

    private void OnMarketDataChanged(
        object? sender,
        MarketDataChangedEventArgs e)
    {
        string? serverName =
            currentServerState.ServerName;

        if (string.IsNullOrWhiteSpace(serverName) ||
            !string.Equals(
                serverName,
                e.ServerName,
                StringComparison.Ordinal))
        {
            return;
        }

        _ = RefreshFocusedProfitabilityAsync();
    }

    private void OnCrushSessionCoefficientsUpdated(
        object? sender,
        EventArgs e)
    {
        _ = RefreshFocusedProfitabilityAsync();
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

    public async Task RefreshFocusedProfitabilityAsync()
    {
        long refreshVersion =
            Interlocked.Increment(
                ref _profitabilityRefreshVersion
            );

        await _profitabilityRefreshLock
            .WaitAsync();

        try
        {
            // Si une demande plus récente attend déjà,
            // celle-ci est obsolète avant même de calculer.
            if (refreshVersion !=
                Volatile.Read(
                    ref _profitabilityRefreshVersion
                ))
            {
                return;
            }

            Equipment? equipment =
                focusedEquipmentState.Equipment;

            string? serverName =
                currentServerState.ServerName;

            if (equipment is null ||
                string.IsNullOrWhiteSpace(
                    serverName))
            {
                return;
            }

            long equipmentId =
                equipment.DofusDbId;

            using IServiceScope scope =
                serviceScopeFactory
                    .CreateScope();

            EquipmentProfitabilityService
                profitabilityService =
                    scope.ServiceProvider
                        .GetRequiredService<
                            EquipmentProfitabilityService>();

            EquipmentProfitabilityResult
                result =
                    await profitabilityService
                        .CalculateAsync(
                            equipment,
                            serverName
                        );

            // Un prix, coefficient ou focus a pu changer
            // pendant le calcul. Dans ce cas, ne jamais
            // afficher ce résultat devenu obsolète.
            if (refreshVersion !=
                Volatile.Read(
                    ref _profitabilityRefreshVersion
                ))
            {
                return;
            }

            Equipment? currentEquipment =
                focusedEquipmentState
                    .Equipment;

            string? currentServerName =
                currentServerState
                    .ServerName;

            if (currentEquipment?.DofusDbId !=
                    equipmentId ||
                !string.Equals(
                    currentServerName,
                    serverName,
                    StringComparison.Ordinal
                ))
            {
                return;
            }

            await MainThread
                .InvokeOnMainThreadAsync(
                    () =>
                    {
                        // Dernière vérification au moment
                        // exact où l'UI est mise à jour.
                        if (refreshVersion !=
                            Volatile.Read(
                                ref _profitabilityRefreshVersion
                            ))
                        {
                            return;
                        }

                        _overlayPage?
                            .ShowProfitability(
                                result
                            );
                    }
                );
        }
        finally
        {
            _profitabilityRefreshLock
                .Release();
        }
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

        overlayControlBarService
            .RefreshState();
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

        overlayControlBarService
            .RefreshState();
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

    private void ToggleProfitabilityOverlayVisibility()
    {
        ClearF7VisibilitySnapshot();
        Toggle();
    }

    private void ToggleMarketOverlayVisibility()
    {
        ClearF7VisibilitySnapshot();
        marketCaptureOverlayService.Toggle();
    }

    private void ToggleCrushOverlayVisibility()
    {
        ClearF7VisibilitySnapshot();

        if (crushSessionService.IsVisible)
        {
            crushSessionService.Hide();
        }
        else
        {
            crushSessionService.Show();
        }
    }

    private void ClearF7VisibilitySnapshot()
    {
        _hasF7VisibilitySnapshot = false;
        _restoreProfitabilityAfterF7 = false;
        _restoreMarketAfterF7 = false;
        _restoreCrushAfterF7 = false;
    }

    private void ActivateMainWindow()
    {
        Window? mainWindow =
            Application.Current?
                .Windows
                .FirstOrDefault(
                    window =>
                        !ReferenceEquals(
                            window,
                            _overlayWindow
                        ) &&
                        !string.Equals(
                            window.Title,
                            "BestCrush",
                            StringComparison.Ordinal
                        ) &&
                        !string.Equals(
                            window.Title,
                            "BestCrush — Mise à jour marché",
                            StringComparison.Ordinal
                        ) &&
                        !string.Equals(
                            window.Title,
                            "BestCrush — Résultat concassage",
                            StringComparison.Ordinal
                        ) &&
                        !string.Equals(
                            window.Title,
                            "BestCrush Overlay",
                            StringComparison.Ordinal
                        )
                );

#if WINDOWS
        if (mainWindow?.Handler?.PlatformView
            is Microsoft.UI.Xaml.Window nativeWindow)
        {
            nativeWindow.Activate();
        }
#endif
    }

    public void RestoreOverlayLayoutsToDefaults()
    {
        overlayLayoutSettingsService
            .ResetAll();

        RestoreDefaultLayout();

        marketCaptureOverlayService
            .RestoreDefaultLayout();

        crushSessionService
            .RestoreDefaultLayout();

        overlayControlBarService
            .RestoreDefaultLayout();
    }

    public void RestoreDefaultLayout()
    {
#if WINDOWS
        OverlayWindowLayout layout =
            overlayLayoutSettingsService
                .GetValidatedLayout(
                    OverlayLayoutKind
                        .Profitability,
                    MinimumOverlayWidth,
                    MinimumOverlayHeight,
                    allowResize: true
                );

        ApplyLayout(
            layout
        );

        SaveCurrentLayout();
#endif
    }

    public void Shutdown()
    {
        crushSessionService.CoefficientsUpdated -=
            OnCrushSessionCoefficientsUpdated;

        marketDataChangeNotifier.Changed -=
            OnMarketDataChanged;

        try
        {
            _middleClickReadCancellation
                .Cancel();

            _middleClickReadQueue
                .Writer
                .TryComplete();
        }
        catch
        {
            // La fermeture ne doit jamais être
            // bloquée par le worker de lecture.
        }

#if WINDOWS
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(
                _keyboardHook
            );

            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(
                _mouseHook
            );

            _mouseHook = IntPtr.Zero;
        }
#endif

        marketCaptureOverlayService
            .Shutdown();

        overlayControlBarService
            .Shutdown();

        if (_overlayWindow is null)
        {
            crushSessionService
                .CloseAndReset();

            return;
        }

        Window window = _overlayWindow;

        _overlayWindow = null;

        crushSessionService
            .CloseAndReset();

        Application.Current?.CloseWindow(
            window
        );
    }

    private void LoadStoredLayout()
    {
#if WINDOWS
        OverlayWindowLayout layout =
            overlayLayoutSettingsService
                .GetValidatedLayout(
                    OverlayLayoutKind
                        .Profitability,
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
                    .Profitability,
                new OverlayWindowLayout(
                    _currentX,
                    _currentY,
                    _currentWidth,
                    _currentHeight
                )
            );
#endif
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
        InstallMouseHook();
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
            SetWindowsHookExKeyboard(
                WhKeyboardLl,
                _keyboardProc,
                moduleHandle,
                0
            );
    }

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            return;
        }

        _mouseProc =
            MouseHookCallback;

        IntPtr moduleHandle =
            GetModuleHandle(null);

        _mouseHook =
            SetWindowsHookExMouse(
                WhMouseLl,
                _mouseProc,
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
                Marshal.ReadInt32(
                    lParam
                );

            if (virtualKeyCode == VkF7)
            {
                if (wParam ==
                        (IntPtr)WmKeyDown &&
                    !_f7Pressed)
                {
                    _f7Pressed = true;

                    MainThread
                        .BeginInvokeOnMainThread(
                            ToggleAllOverlays
                        );
                }
                else if (
                    wParam ==
                    (IntPtr)WmKeyUp)
                {
                    _f7Pressed = false;
                }
            }

            if (virtualKeyCode == VkF9)
            {
                if (wParam ==
                        (IntPtr)WmKeyDown &&
                    !_f9Pressed)
                {
                    _f9Pressed = true;

                    MainThread
                        .BeginInvokeOnMainThread(
                            crushSessionService.Toggle
                        );
                }
                else if (
                    wParam ==
                    (IntPtr)WmKeyUp)
                {
                    _f9Pressed = false;
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

    private IntPtr MouseHookCallback(
        int nCode,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(
                _mouseHook,
                nCode,
                wParam,
                lParam
            );
        }

        int message =
            wParam.ToInt32();

        bool isRelevantMessage =
            message == WmMButtonDown ||
            message == WmMButtonUp ||
            message == WmMouseWheel ||
            message == WmMouseHWheel;

        if (!isRelevantMessage)
        {
            return CallNextHookEx(
                _mouseHook,
                nCode,
                wParam,
                lParam
            );
        }

        LowLevelMouseHookData mouseData =
            Marshal.PtrToStructure<
                LowLevelMouseHookData
            >(lParam);

        if (message == WmMButtonUp)
        {
            _middleButtonPressed = false;
        }

        bool overBestCrushOverlay =
            IsPointInsideMainOverlay(
                mouseData.Point.X,
                mouseData.Point.Y
            ) ||
            marketCaptureOverlayService
                .ContainsScreenPoint(
                    mouseData.Point.X,
                    mouseData.Point.Y
                ) ||
            crushSessionService
                .ContainsScreenPoint(
                    mouseData.Point.X,
                    mouseData.Point.Y
                ) ||
            overlayControlBarService
                .ContainsScreenPoint(
                    mouseData.Point.X,
                    mouseData.Point.Y
                );

        if (overBestCrushOverlay)
        {
            return CallNextHookEx(
                _mouseHook,
                nCode,
                wParam,
                lParam
            );
        }

        bool overDofus =
            IsPointInsideActiveDofusWindow(
                mouseData.Point.X,
                mouseData.Point.Y
            );

        if (!overDofus)
        {
            return CallNextHookEx(
                _mouseHook,
                nCode,
                wParam,
                lParam
            );
        }

        if (message == WmMButtonDown &&
            !_middleButtonPressed)
        {
            _middleButtonPressed = true;

            // Le hook souris doit rendre la main à Windows
            // immédiatement. Le déclenchement de la capture
            // est donc lui aussi envoyé sur le pool de threads.
            _ = Task.Run(
                async () =>
                {
                    await Task.Delay(
                        35
                    )
                    .ConfigureAwait(false);

                    RequestRead();
                }
            );
        }
        else if (
            (
                message == WmMouseWheel ||
                message == WmMouseHWheel
            ) &&
            crushSessionService.IsRunning)
        {
            MainThread
                .BeginInvokeOnMainThread(
                    crushSessionService
                        .InvalidateForScroll
                );
        }

        return CallNextHookEx(
            _mouseHook,
            nCode,
            wParam,
            lParam
        );
    }

    private bool IsPointInsideActiveDofusWindow(
        int x,
        int y)
    {
        DofusWindowInfo? dofusWindow =
            dofusWindowService
                .GetActiveDofusWindow();

        if (dofusWindow is null)
        {
            return false;
        }

        return
            x >= dofusWindow.X &&
            y >= dofusWindow.Y &&
            x <
                dofusWindow.X +
                dofusWindow.Width &&
            y <
                dofusWindow.Y +
                dofusWindow.Height;
    }

    private bool IsPointInsideMainOverlay(
        int x,
        int y)
    {
        if (!_isOverlayVisible)
        {
            return false;
        }

        return
            x >= _currentX &&
            y >= _currentY &&
            x <
                _currentX +
                _currentWidth &&
            y <
                _currentY +
                _currentHeight;
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

    private delegate IntPtr
        LowLevelMouseProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam
        );

    [StructLayout(
        LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(
        LayoutKind.Sequential)]
    private struct LowLevelMouseHookData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowsHookExW",
        SetLastError = true
    )]
    private static extern IntPtr
        SetWindowsHookExKeyboard(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId
        );

    [DllImport(
        "user32.dll",
        EntryPoint = "SetWindowsHookExW",
        SetLastError = true
    )]
    private static extern IntPtr
        SetWindowsHookExMouse(
            int idHook,
            LowLevelMouseProc lpfn,
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

    private sealed record MiddleClickReadWorkItem(
        Task<DofusCaptureResult> CaptureTask
    );
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
