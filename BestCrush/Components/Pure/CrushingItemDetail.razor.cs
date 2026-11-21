using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BestCrush.Components.Pure;

public partial class CrushingItemDetail : IDisposable
{
    [Inject] private IServiceScopeFactory Scopes { get; set; } = default!;
    [Inject] private MarketDataChangeNotifier PriceChanges { get; set; } = default!;
    [Parameter, EditorRequired] public Equipment Equipment { get; set; } = default!;
    [Parameter, EditorRequired] public string ServerName { get; set; } = "";
    [Parameter] public OwnedForgemagingEquipment? Owned { get; set; }
    [Parameter] public MarketPriceObservation? Purchase { get; set; }
    [Parameter] public CraftCostResult? Craft { get; set; }
    [Parameter] public EventCallback<CrushingResultSummary> SummaryChanged { get; set; }

    private sealed record DetailData(CrushingItemMarket Market, OwnedForgemagingEquipment[] Owned, CraftCostResult? Craft);
    private enum HistoryKind { Coefficient, Price }
    private enum JetPreset { Minimum, Middle, Maximum }
    private static readonly (JetPreset Jet, string Icon, string Label)[] Presets =
        [(JetPreset.Minimum, "bi-circle", "Jet minimum"), (JetPreset.Middle, "bi-circle-half", "Jet moyen"),
            (JetPreset.Maximum, "bi-circle-fill", "Jet maximum")];

    private readonly BackgroundDataLoad<DetailData> _load = new();
    private BackgroundDataLoad<(DateTime At, double Value)[]> _coefficientChart = new();
    private BackgroundDataLoad<(DateTime At, double Value)[]> _priceChart = new();
    private LiveStockService? _stock;
    private IBestCrushSettingsProvider? _settings;
    private DetailData? _data;
    private (string Server, long Item, string? Instance) _key;
    private OwnedForgemagingEquipment? _ownedSelection;
    private List<ForgemagingLine> _simulation = [];
    private IReadOnlyList<ForgemagingLine>? _roll => (Owned ?? _ownedSelection) is { } owned ? owned.Roll : _simulation;
    private bool IsOwned => Owned is not null || _ownedSelection is not null;
    private bool _disposed, _refreshing;
    private int _pendingRefresh;
    private CrushingRollEstimate? _estimate;
    private bool _showCoefficient, _showPrice;
    private CrushingResultSummary? _lastSummary;
    private double? _purchase => (_data is { } data ? data.Market.Purchase : Purchase)?.Price;
    private double? _craft => Equipment.Recipe.Count + Equipment.EquipmentRecipe.Count > 0 ? Craft?.TotalCost : null;
    private bool UseCraft => _craft.HasValue && (!_purchase.HasValue || _craft < _purchase);
    private string CostLabel => (Owned ?? _ownedSelection)?.Acquisition?.Kind switch
    {
        EquipmentAcquisitionKind.Craft => "Prix de craft",
        EquipmentAcquisitionKind.Purchase => "Prix d’achat",
        _ when IsOwned => "Prix d’obtention",
        _ => UseCraft ? "Prix de craft" : "Prix d’achat"
    };
    private double? AcquisitionCost => (Owned ?? _ownedSelection) is { } owned
        ? owned.AcquisitionCost : UseCraft ? _craft : _purchase;
    private double? ObtainmentCost => (Owned ?? _ownedSelection) is { } owned
        ? AcquisitionCost + (owned.UnpricedRunes == 0 && !owned.FmCostUnknown && !owned.UnobservedChanges ? owned.KnownRuneCost : null)
        : AcquisitionCost;
    protected override void OnInitialized()
    {
        using var scope = Scopes.CreateScope();
        _stock = scope.ServiceProvider.GetService<LiveStockService>();
        _settings = scope.ServiceProvider.GetService<IBestCrushSettingsProvider>();
        if (_stock is not null) _stock.Changed += StockChanged;
        PriceChanges.Changed += PricesChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        var key = (ServerName, Equipment.DofusDbId, Owned?.Key);
        if (_key == key) { await RecalculateAsync(); return; }
        _key = key;
        _data = null;
        _lastSummary = null;
        _ownedSelection = null;
        _showCoefficient = _showPrice = false;
        _coefficientChart.Dispose();
        _priceChart.Dispose();
        _coefficientChart = new();
        _priceChart = new();
        bool startAtMinimum = _settings?.CrushYieldEstimationMode == CrushYieldEstimationMode.Conservative;
        _simulation = Equipment.Characteristics.GroupBy(stat => stat.Characteristic).Select(group =>
        {
            var stat = group.First();
            return new ForgemagingLine { Characteristic = stat.Characteristic, Minimum = Math.Min(stat.From, stat.To),
                Maximum = Math.Max(stat.From, stat.To), Current = startAtMinimum ? Math.Min(stat.From, stat.To) : (int)Math.Round((stat.From + (double)stat.To) / 2) };
        }).OrderBy(line => line.Characteristic).ToList();
        await RecalculateAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var key = _key;
        var source = Owned is null ? _stock?.SelectedForServer(key.Server) : null;
        bool needsCraft = Owned is not null;
        var loaded = await _load.RunAsync(async token =>
        {
            using var scope = Scopes.CreateScope();
            var services = scope.ServiceProvider;
            var data = services.GetRequiredService<ForgemagingDataService>();
            var market = await data.GetCrushingItemMarketAsync(key.Item, key.Server, token);
            OwnedForgemagingEquipment[] owned = [];
            if (source is not null && services.GetService<ForgemagingSessionStore>() is { } store)
            {
                owned = await ForgemagedEquipmentStock.LoadAsync(source, key.Server, store, data,
                    services.GetService<ActivityCatalogService>(), services.GetService<CoefficientService>(),
                    services.GetService<IBestCrushSettingsProvider>(), true, token, key.Item);
                owned = owned.OrderBy(row => row.Container == "bank").ThenByDescending(row => row.Roll is not null).ToArray();
            }
            var craft = needsCraft
                ? (await data.GetEquipmentCraftCostsAsync(key.Server, [key.Item], token)).GetValueOrDefault(key.Item) : null;
            return new(market, owned, craft);
        });
        if (!loaded || _disposed || _key != key || _load.Value is not { } result) return;
        _data = result;
        _ownedSelection = result.Owned.FirstOrDefault(row => row.Key == _ownedSelection?.Key) ?? result.Owned.FirstOrDefault();
        await RecalculateAsync();
    }

    private async Task RecalculateAsync()
    {
        _estimate = _data is { } data && _roll is { } roll
            ? ForgemagingCrushAssessment.EstimateRoll(Equipment.Level, roll, data.Market.Runes.Runes, data.Market.Context) : null;
        var best = _estimate?.Best;
        double? cost = ObtainmentCost;
        var focusRow = best?.Focus is { } focus ? _estimate!.Lines.FirstOrDefault(row => row.Stat == focus) : null;
        var summary = new CrushingResultSummary(best?.Value, best?.Value - cost, cost, best?.Focus, focusRow?.Rune?.IconId);
        if (_lastSummary == summary) return;
        _lastSummary = summary;
        await SummaryChanged.InvokeAsync(summary);
    }

    private Task SelectInstance(ChangeEventArgs args)
    {
        _ownedSelection = _data?.Owned.FirstOrDefault(row => row.Key == args.Value?.ToString()) ?? _ownedSelection;
        return RecalculateAsync();
    }

    private Task SetPreset(JetPreset preset)
    {
        if (IsOwned) return Task.CompletedTask;
        foreach (var line in _simulation)
        {
            if (!line.IsNative) continue;
            line.Current = preset switch
            {
                JetPreset.Minimum => line.Minimum,
                JetPreset.Maximum => line.Maximum,
                _ => (int)Math.Round((line.Minimum!.Value + (double)line.Maximum!.Value) / 2)
            };
        }
        return RecalculateAsync();
    }

    private Task EditJet(ForgemagingLine line, ChangeEventArgs args) => int.TryParse(args.Value?.ToString(), out int value)
        ? SetJet(line, value) : Task.CompletedTask;

    private Task SetJet(ForgemagingLine line, int value)
    {
        if (IsOwned) return Task.CompletedTask;
        var stat = line.Characteristic;
        double remaining = Math.Max(0, 101 - ForgemagingPlanner.ExcessWeight(_simulation.Where(other => other != line)));
        int maximum = Math.Min(ForgemagingPlanner.LineCap(line), (line.Maximum ?? 0) + (int)Math.Floor(remaining / stat.GetWeight() + 1e-9));
        line.Current = Math.Clamp(value, Math.Min(line.Minimum ?? 0, 0), maximum);
        return RecalculateAsync();
    }

    private Task AddExo(Characteristic stat)
    {
        if (IsOwned || _simulation.Any(line => line.Characteristic == stat)) return Task.CompletedTask;
        var line = new ForgemagingLine { Characteristic = stat, Current = 0 };
        _simulation.Add(line);
        return SetJet(line, 1);
    }

    private async Task ToggleChartAsync(HistoryKind kind)
    {
        bool coefficient = kind == HistoryKind.Coefficient;
        var loader = coefficient ? _coefficientChart : _priceChart;
        bool show = coefficient ? _showCoefficient = !_showCoefficient : _showPrice = !_showPrice;
        if (!show) { loader.Cancel(); return; }

        var key = _key;
        await loader.RunAsync(async token =>
        {
            using var scope = Scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ForgemagingDataService>()
                .GetCrushingHistoryAsync(key.Item, key.Server, coefficient, DateTime.UtcNow.AddDays(-60), token);
        });
    }

    private void StockChanged()
    {
        if (Owned is null) QueueRefresh();
    }

    private void PricesChanged(object? sender, MarketDataChangedEventArgs change)
    {
        if (string.Equals(change.ServerName, ServerName, StringComparison.OrdinalIgnoreCase) &&
            (change.DofusDbId==0 || change.ObjectType == MarketObjectType.Rune || change.ObjectType == MarketObjectType.Equipment && change.DofusDbId == Equipment.DofusDbId ||
                change.ObjectType == MarketObjectType.Resource && Owned is not null))
            QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_disposed) return;
        Interlocked.Exchange(ref _pendingRefresh, 1);
        _ = InvokeAsync(async () =>
        {
            if (_disposed || _refreshing) return;
            _refreshing = true;
            try
            {
                while (!_disposed && Volatile.Read(ref _pendingRefresh) != 0)
                {
                    await Task.Delay(250);
                    Interlocked.Exchange(ref _pendingRefresh, 0);
                    if (_disposed) return;
                    await LoadAsync();
                    if (!_disposed) StateHasChanged();
                }
            }
            finally { _refreshing = false; }
        });
    }

    private static string RuneImage(ForgemagingRune rune) => rune.IconId is > 0
        ? $"img/runes/{rune.IconId}.png" : "img/runes/unknown.svg";
    private static string? JetClass(ForgemagingLine line) => !line.IsNative ? "exo" : line.Current > line.Maximum ? "over" : null;
    private static string Money(double? value) => value is { } amount ? $"{amount:N0} K" : "—";
    private static string Percent(double? value) => value is { } amount ? $"{amount:N1} %" : "—";
    private static string Quantity(double? value) => value is { } amount ? amount.ToString("0.##") : "—";

    public void Dispose()
    {
        _disposed = true;
        _load.Dispose();
        _coefficientChart.Dispose();
        _priceChart.Dispose();
        PriceChanges.Changed -= PricesChanged;
        if (_stock is not null) _stock.Changed -= StockChanged;
    }
}
