using System.Net;
using BestCrush.Components.Pages;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.BestCrush;

public sealed class BreedingComparisonTest
{
    [Fact]
    public async Task ComparisonKeepsBreedingReservationsAndDemandChecksAlongsideFinancialValues()
    {
        var now = DateTimeOffset.UtcNow;
        var held = new PaddockMountReading(Guid.NewGuid(), 10, 0, 0, 0) { SpeciesId = 18 };
        var sell = held with { Id = Guid.NewGuid() };
        var unverified = held with { Id = Guid.NewGuid() };
        var sale = new BreedingResaleOption(100, 7500, 250, now);
        var report = EmptyReport() with
        {
            Resale = new[] { held, sell, unverified }.ToDictionary(m => m.Id,
                _ => new BreedingResaleAssessment(null, sale, 2, 1, now)),
            Sellable = new Dictionary<Guid, BreedingResaleOption> { [sell.Id] = sale },
            OwnedCapacity = new Dictionary<Guid, BreedingMarketCapacity>
            {
                [unverified.Id] = new(0, 0, 0, 0, 0, now) { Evidence = BreedingDemandEvidence.HistoryUnavailable }
            }
        };
        var parameters = new Dictionary<string, object?>
        {
            [nameof(BreedingOwnedComparison.Report)] = report,
            [nameof(BreedingOwnedComparison.Mounts)] = new[] { held, sell, unverified },
            [nameof(BreedingOwnedComparison.SpeciesById)] = new Dictionary<long, BreedingSpecies> { [18] = new(18, "Dragodinde Dorée", 1, 1) },
            [nameof(BreedingOwnedComparison.MountLocations)] = new Dictionary<Guid, string> { [sell.Id] = "Enclos 1" },
            [nameof(BreedingOwnedComparison.HeldMounts)] = new HashSet<Guid> { held.Id },
            [nameof(BreedingOwnedComparison.FocusedMount)] = sell.Id
        };
        string html = await Render<BreedingOwnedComparison>(parameters);
        Assert.Contains("Garder pour l’accouplement", html);
        Assert.Contains("Monter au niv. 100", html);
        Assert.Contains("Débouché à vérifier", html);
        Assert.Contains("aucune vente n’est conseillée", html);
        Assert.Contains(sale.Net.ToString("N0"), html);
        Assert.Contains($"data-breeding-action=\"{sell.Id:N}\"", html);
        Assert.Contains("breeding-action-focused", html);
        Assert.Contains("Enclos 1", html);
        Assert.Contains("img/dofus/mounts/33011.png", html);
        Assert.Contains("Voir Dragodinde Dorée", html);
        Assert.DoesNotContain("Endurance", html);
        Assert.DoesNotContain("Sérénité", html);

        if (Environment.GetEnvironmentVariable("BREEDING_COMPARISON_PREVIEW") is { } folder)
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "owned.html"), html);
            await File.WriteAllTextAsync(Path.Combine(folder, "board.html"), await Render<BreedingObjectiveBoard>(new()
            {
                [nameof(BreedingObjectiveBoard.Report)] = report,
                [nameof(BreedingObjectiveBoard.Journal)] = new BreedingObjectiveJournal()
            }));
        }
    }

    private static BreedingTradeReport EmptyReport() => new("Kourial", [], new Dictionary<Guid, BreedingResaleAssessment>(),
        false, [], [], [], new Dictionary<string, BreedingResaleOption>(), new([], [], null), [],
        new Dictionary<Guid, BreedingResaleOption>(), new Dictionary<Guid, BreedingMarketCapacity>(), new HashSet<string>());

    private static async Task<string> Render<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters)));
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
    }
}
