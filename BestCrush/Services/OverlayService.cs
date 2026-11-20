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
    IServiceScopeFactory serviceScopeFactory,
    CurrentServerState currentServerState,
    FocusedEquipmentState focusedEquipmentState,
    LastNetworkEquipmentState lastNetworkEquipmentState,
    CrushSessionService crushSessionService,
    MarketDataChangeNotifier marketDataChangeNotifier,
    MarketCaptureOverlayService marketCaptureOverlayService,
    OverlayControlBarService overlayControlBarService,
    OverlayLayoutSettingsService overlayLayoutSettingsService)
{
    private OverlayWindowsService? _modern;
    private OverlayWorkspaceService? _workspace;
    private Window? _overlayWindow;
    private OverlayPage? _overlayPage;

    // Ephemeral verified-window equipment, separate from the legacy global
    // slot. Never persist window or character-specific focus state.
    private WindowScopedNetworkEquipmentState? _windowEquipmentState;
    private CurrentDofusClientsState? _dofusClientsState;
    private long _middleFocusRequestVersion;
    // Any explicit focus (including F8 or a manual UI action) supersedes
    // an older middle-click still waiting for a database lookup.
    private long _equipmentFocusMutationVersion;

    // F8 est le seul usage OCR du produit :
    // il capture l'infobulle actuellement survolÃ©e afin de
    // dÃ©finir le focus Ã©quipement. Le clic molette, lui,
    // reprend le dernier Ã©quipement identifiÃ© par le rÃ©seau.
    private readonly Channel<TooltipReadWorkItem>
        _tooltipReadQueue =
            Channel.CreateUnbounded<TooltipReadWorkItem>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                }
            );

    private readonly CancellationTokenSource
        _tooltipReadCancellation = new();

    private readonly object
        _tooltipReadWorkerLock = new();

    private Task? _tooltipReadWorkerTask;

    
    // Les changements de marchÃ© peuvent dÃ©clencher plusieurs
    // recalculs presque simultanÃ©s (notifier + capture directe).
    // Un seul calcul de rentabilitÃ© est autorisÃ© Ã  la fois.
    private readonly System.Threading.SemaphoreSlim
        _profitabilityRefreshLock =
            new(1, 1);

    // Chaque demande reÃ§oit une gÃ©nÃ©ration. Un calcul dont la
    // gÃ©nÃ©ration n'est plus la derniÃ¨re ne doit jamais Ã©craser
    // un rÃ©sultat plus rÃ©cent dans l'overlay.
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

    private const int VkF7 = 0x76;
    private const int VkF8 = 0x77;

    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;

    private LowLevelKeyboardProc? _keyboardProc;
    private LowLevelMouseProc? _mouseProc;

    private bool _f7Pressed;
    private bool _f8Pressed;
    private bool _middleButtonPressed;
#endif

    public void Initialize()
    {
        if (_modern is not null) return;
        using var scope = serviceScopeFactory.CreateScope();
        _windowEquipmentState = scope.ServiceProvider.GetService<WindowScopedNetworkEquipmentState>();
        _dofusClientsState = scope.ServiceProvider.GetService<CurrentDofusClientsState>();
        _modern = scope.ServiceProvider.GetRequiredService<OverlayWindowsService>();
        _workspace = scope.ServiceProvider.GetRequiredService<OverlayWorkspaceService>();
        _modern.OpenMain = ActivateMainWindow;
        crushSessionService.AttachOverlays(_modern, _workspace);
        _modern.Initialize();
#if WINDOWS
        InstallKeyboardHook();
        InstallMouseHook();
#endif
    }

    public bool IsVisible
    {
        get
        {
            if (_modern is not null) return _modern.IsVisible(WorkspaceOverlayKind.Profitability);
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
        if (_modern is not null) { _modern.Toggle(WorkspaceOverlayKind.Profitability); return; }
        if (_modern is not null) { _modern.Toggle(WorkspaceOverlayKind.Profitability); return; }
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
        if (_modern is not null) { _modern.ToggleAll(); return; }
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

            overlayControlBarService
                .RefreshState();

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

        overlayControlBarService
            .RefreshState();
#endif
    }

    public void RequestTooltipRead()
    {
        DofusWindowInfo? dofusWindow =
            dofusWindowService
                .GetActiveDofusWindow();

        if (dofusWindow is null)
        {
            PostUi(marketCaptureOverlayService.ShowReadCancelled);
            return;
        }

        if (currentServerState.AutomaticRoutingEnabled)
        {
            DofusClientWindowKey target = new(
                dofusWindow.Handle.ToInt64(), dofusWindow.ProcessId);
            NetworkCaptureLease? gameLease =
                currentServerState.GetWindowCaptureLease(target);

            // F8 is an explicit in-game action. The temporary SQL filter
            // must NEVER provide the server context for this tooltip.
            if (gameLease is not { } verified ||
                !currentServerState.IsCaptureLeaseActive(verified))
            {
                PostUi(marketCaptureOverlayService.ShowServerSelectionRequired);
                return;
            }

            currentServerState.SelectServer(verified.ServerName);
            _dofusClientsState?.SelectWindow(target);
        }
        else if (!currentServerState.HasSelectedServer)
        {
            PostUi(marketCaptureOverlayService.ShowServerSelectionRequired);
            return;
        }

        PostUi(
            () =>
                marketCaptureOverlayService
                    .ShowCaptureStarted(
                        dofusWindow
                    )
        );

        EnsureTooltipReadWorker();

        // La capture dÃ©marre immÃ©diatement Ã  l'appui sur F8.
        // Elle n'attend jamais que l'OCR ou le calcul
        // de la demande prÃ©cÃ©dente soit terminÃ©.
        Task<DofusCaptureResult> captureTask =
            Task.Run(
                async () =>
                    await dofusCaptureService
                        .CaptureAsync(
                            dofusWindow,
                            _tooltipReadCancellation
                                .Token
                        )
                        .ConfigureAwait(false),
                _tooltipReadCancellation.Token
            );

        TooltipReadWorkItem workItem =
            new(
                captureTask
            );

        if (!_tooltipReadQueue
            .Writer
            .TryWrite(
                workItem
            ))
        {
            // La file a Ã©tÃ© fermÃ©e alors que la
            // capture Ã©tait dÃ©jÃ  partie : elle ne
            // sera jamais consommÃ©e par le worker.
            _ = CleanupAbandonedCaptureAsync(
                captureTask
            );
        }
    }

    private void EnsureTooltipReadWorker()
    {
        if (_tooltipReadWorkerTask is not null)
        {
            return;
        }

        lock (_tooltipReadWorkerLock)
        {
            if (_tooltipReadWorkerTask is not null)
            {
                return;
            }

            _tooltipReadWorkerTask =
                Task.Run(
                    () =>
                        ProcessTooltipReadQueueAsync(
                            _tooltipReadCancellation
                                .Token
                        )
                );
        }
    }

    private async Task ProcessTooltipReadQueueAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (
                TooltipReadWorkItem workItem
                in _tooltipReadQueue
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
                    // Les captures ont dÃ©jÃ  commencÃ© en parallÃ¨le.
                    // Elles sont cependant analysÃ©es dans l'ordre
                    // des demandes F8 afin de conserver un focus
                    // dÃ©terministe.
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
                    // les crops gÃ©nÃ©rÃ©s dans son dossier.
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
            // Si CaptureAsync a Ã©chouÃ© aprÃ¨s avoir
            // crÃ©Ã© son dossier, DofusCaptureService
            // effectue dÃ©jÃ  son propre nettoyage.
        }
    }

    private async Task ProcessCapturedReadAsync(
        DofusCaptureResult capture,
        CancellationToken cancellationToken)
    {
        PostUi(
            () =>
                marketCaptureOverlayService
                    .ShowCaptureSuccess(
                        capture
                    )
        );

        // Le rÃ©seau est dÃ©sormais la source de vÃ©ritÃ© pour
        // les donnÃ©es de jeu. La capture visuelle ne sert
        // plus qu'Ã  identifier l'Ã©quipement sous la souris,
        // information qu'aucun paquet n'Ã©met lors d'un simple
        // survol d'inventaire.
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
                        capture.FilePath,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

        if (tooltipDetection.Candidates.Count > 1)
        {
            PostUi(
                () =>
                    marketCaptureOverlayService
                        .ShowMultipleTooltipsDetected(
                            tooltipDetection.Candidates.Count
                        )
            );

            return;
        }

        if (tooltipDetection.Candidates.Count == 0)
        {
            PostUi(
                marketCaptureOverlayService
                    .ShowTooltipNotDetected
            );

            return;
        }

        DofusItemTooltipCandidate candidate =
            tooltipDetection.Candidates[0];

        if (candidate.Recognition is null)
        {
            PostUi(
                () =>
                    marketCaptureOverlayService
                        .ShowEquipmentRecognitionFailed(
                            candidate.RecognizedTitle
                        )
            );

            return;
        }

        Equipment equipment =
            candidate.Recognition.Equipment;

        if (_workspace is not null) await _workspace.FocusTooltipAsync(equipment).ConfigureAwait(false);
        else await FocusEquipmentAsync(equipment).ConfigureAwait(false);

        PostUi(
            () =>
                marketCaptureOverlayService
                    .ShowTooltipEquipmentFocused(
                        equipment.Name,
                        candidate.Recognition.Confidence
                    )
        );
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

    private async Task FocusLastNetworkEquipmentAsync(
        WindowEquipmentFocusToken request, long requestVersion,
        long previousFocusVersion)
    {
        // The click snapshots the verified OS window + manual server lease.
        // Recheck after database access: another click, closed Dofus window,
        // capture reset or server change must make the pending focus inert.
        if (previousFocusVersion !=
                CurrentEquipmentFocusVersion ||
            !IsValidMiddleClickRequest(request, requestVersion))
            return;

        using IServiceScope scope = serviceScopeFactory.CreateScope();
        ItemsService itemsService = scope.ServiceProvider
            .GetRequiredService<ItemsService>();

        Equipment? equipment = await itemsService
            .GetEquipmentAsync(request.Equipment.DofusDbId)
            .ConfigureAwait(false);

        if (equipment is null ||
            previousFocusVersion !=
                CurrentEquipmentFocusVersion ||
            !IsValidMiddleClickRequest(request, requestVersion))
            return;

        await FocusEquipmentAsync(equipment).ConfigureAwait(false);
    }

    private long CurrentEquipmentFocusVersion => _workspace?.FocusVersion ?? Interlocked.Read(ref _equipmentFocusMutationVersion);

    private bool IsValidMiddleClickRequest(
        WindowEquipmentFocusToken request, long requestVersion)
    {
        if (requestVersion !=
                Interlocked.Read(ref _middleFocusRequestVersion) ||
            !currentServerState.IsCaptureLeaseActive(request.Lease) ||
            (currentServerState.AutomaticRoutingEnabled &&
             !string.Equals(currentServerState.ServerName,
                 request.Lease.ServerName, StringComparison.Ordinal)) ||
            _windowEquipmentState?.IsCurrent(request) != true)
            return false;

        // A cached pointer event is not enough: a closed/changed window
        // must not receive another client's network equipment.
        DofusWindowInfo[] matching = dofusWindowService
            .ListVisibleDofusWindows()
            .Where(window =>
                window.Handle.ToInt64() == request.Window.Handle &&
                window.ProcessId == request.Window.ProcessId)
            .Take(2).ToArray();
        return matching.Length == 1;
    }

    public async Task FocusEquipmentAndCopyNameAsync(Equipment equipment, string? serverName)
    {
        if (!currentServerState.AutomaticRoutingEnabled && serverName is not null)
            currentServerState.SelectServer(serverName);

        await FocusEquipmentAsync(equipment);
        await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(equipment.Name);
    }

    public async Task FocusEquipmentAsync(
        Equipment equipment)
    {
        Interlocked.Increment(ref _equipmentFocusMutationVersion);
        ClearF7VisibilitySnapshot();
        if (_workspace is not null)
        {
            focusedEquipmentState.SetEquipment(equipment);
            await _workspace.FocusAsync(equipment);
            return;
        }

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
        if (_workspace is not null) { await _workspace.RefreshAsync(); return; }
        long refreshVersion =
            Interlocked.Increment(
                ref _profitabilityRefreshVersion
            );

        await _profitabilityRefreshLock
            .WaitAsync();

        try
        {
            // Si une demande plus rÃ©cente attend dÃ©jÃ ,
            // celle-ci est obsolÃ¨te avant mÃªme de calculer.
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
            // afficher ce rÃ©sultat devenu obsolÃ¨te.
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
                        // DerniÃ¨re vÃ©rification au moment
                        // exact oÃ¹ l'UI est mise Ã  jour.
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
        if (_modern is not null) { _modern.Show(WorkspaceOverlayKind.Profitability); return; }
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
        if (_modern is not null) { _modern.Hide(WorkspaceOverlayKind.Profitability.ToString()); return; }
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
#if WINDOWS
        var window = Application.Current?.Windows.FirstOrDefault(w =>
            !w.Title.StartsWith("BestCrush · ", StringComparison.Ordinal) && w is not null);
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native) native.Activate();
#endif
    }

    public void RestoreOverlayLayoutsToDefaults()
    {
        if (_modern is not null) { overlayLayoutSettingsService.ResetAll(); _modern.RestoreLayouts(); return; }
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
        _modern?.Shutdown(); _modern = null;
        marketDataChangeNotifier.Changed -=
            OnMarketDataChanged;

        try
        {
            _tooltipReadCancellation
                .Cancel();

            _tooltipReadQueue
                .Writer
                .TryComplete();
        }
        catch
        {
            // La fermeture ne doit jamais Ãªtre
            // bloquÃ©e par le worker de lecture.
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

        // On le masque immÃ©diatement.
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

            if (virtualKeyCode == VkF8)
            {
                if (wParam ==
                        (IntPtr)WmKeyDown &&
                    !_f8Pressed)
                {
                    _f8Pressed = true;

                    MainThread
                        .BeginInvokeOnMainThread(
                            RequestTooltipRead
                        );
                }
                else if (
                    wParam ==
                    (IntPtr)WmKeyUp)
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
            message == WmMButtonUp;

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
            _modern?.ContainsScreenPoint(mouseData.Point.X, mouseData.Point.Y) == true ||
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

        // Require the actual top-level Dofus window under the pointer.
        // The foreground window or the first Dofus window is never proof.
        DofusWindowInfo? targetWindow = dofusWindowService
            .GetDofusWindowAtScreenPoint(
                mouseData.Point.X,
                mouseData.Point.Y);

        if (targetWindow is null)
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

            // An empty click also cancels a previous asynchronous focus.
            long requestVersion = Interlocked.Increment(
                ref _middleFocusRequestVersion);
            long focusVersion = CurrentEquipmentFocusVersion;
            DofusClientWindowKey target = new(
                targetWindow.Handle.ToInt64(), targetWindow.ProcessId);
            NetworkCaptureLease? lease =
                currentServerState.GetWindowCaptureLease(target);

            if (lease is { } selectedLease &&
                _windowEquipmentState is { } windows)
            {
                if (currentServerState.AutomaticRoutingEnabled &&
                    currentServerState.IsCaptureLeaseActive(selectedLease))
                {
                    // A wheel click on the real Dofus window is another
                    // explicit game focus: restore that window's server
                    // before calculating, never the SQL browsing server.
                    currentServerState.SelectServer(selectedLease.ServerName);
                    _dofusClientsState?.SelectWindow(target);
                }

                WindowEquipmentFocusToken? token =
                    windows.GetFocusToken(target, selectedLease);

                // Fail closed: NEVER use the previous global equipment if
                // the clicked Dofus window has no verified network item.
                if (token is not null)
                {
                    _ = Task.Run(() =>
                        FocusLastNetworkEquipmentAsync(
                            token, requestVersion, focusVersion));
                }
            }
        }

        return CallNextHookEx(
            _mouseHook,
            nCode,
            wParam,
            lParam
        );
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

    private sealed record TooltipReadWorkItem(
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
