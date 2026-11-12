using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Network.Protocol;
using BestCrush.Services;
using BestCrush.Components.Pure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.BestCrush;

public sealed class PersonalResaleQuotaTest
{
    [Theory]
    [InlineData(0, "× 3", false)]
    [InlineData(1, "× 2", false)]
    [InlineData(3, "Ne plus mettre en vente", true)]
    public async Task IndicatorCountsDownAndShowsTheStopIconAtTheLimit(long listed, string expected, bool stop)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<ResaleQuotaIndicator>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Quota"] = new ResaleQuota(35, DateTimeOffset.UtcNow.AddHours(1)), ["ListedUnits"] = listed }));
            return System.Net.WebUtility.HtmlDecode(view.ToHtmlString());
        });
        Assert.Contains(expected, html);
        Assert.Equal(stop, html.Contains("bi-x-circle"));
        Assert.Contains($"Vos mises en vente détectées sur 24 h : {listed}", html);
    }

    [Fact]
    public void RevokedCaptureCannotRecordANewListing()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new GlobalSalesRepository(Path.Combine(folder, "sales.db"));
            using var services = new ServiceCollection().AddSingleton(repository).BuildServiceProvider();
            var state = new CurrentServerState();
            state.SelectServer("Kourial");
            var lease = state.GetCaptureLease()!.Value;
            var writer = new NetworkObservationWriter(services.GetRequiredService<IServiceScopeFactory>(), state,
                new LastNetworkEquipmentState(), new Mock<IBestCrushSettingsProvider>().Object,
                new MarketDataChangeNotifier(), new NetworkDebugWriter(state, () => false, 5555),
                (_, _, _, _) => Task.CompletedTask, NullLogger<DofusNetworkCaptureService>.Instance);
            var now = DateTime.UtcNow;
            writer.PersistMarketListing(new(1, 5000, 10, 1000, []), now, lease);
            state.SelectServer("Tylezia");
            writer.PersistMarketListing(new(2, 5000, 100, 10000, []), now, lease);
            Assert.Equal(10, repository.ReadListingTotals("Kourial", new DateTimeOffset(now)).ByItem[5000]);
            Assert.Empty(repository.ReadListingTotals("Tylezia", new DateTimeOffset(now)).ByItem);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 2)]
    [InlineData(28, 2)]
    [InlineData(35, 3)]
    public void UsesHalfTheUsualDailyVolumeRoundedUp(long weekly, long suggested)
    {
        var quota = new ResaleQuota(weekly, DateTimeOffset.UtcNow);
        Assert.Equal(suggested, quota.SuggestedUnits);
        Assert.Equal(Math.Max(0, suggested - 1), quota.Remaining(1));
        Assert.Equal(0, quota.Remaining(10));
    }

    [Fact]
    public void CountsUnitsOncePerListingAndServerAndGroupsVerifiedSoulVariants()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new GlobalSalesRepository(Path.Combine(folder, "global-sales.db"));
            DateTimeOffset now = new(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);
            const long boss = 180;
            const int firstSoul = 1234, secondSoul = 1235, resource = 5000;
            foreach (int id in new[] { firstSoul, secondSoul })
                repository.SaveHistory(new(new("Kourial", MarketObjectType.Resource, id, $"boss:{boss}"), now, [], []));

            var first = new MarketListingCreatedObservation(1, firstSoul, 1, 500, []);
            Assert.True(repository.SaveListing("Kourial", first, now.AddHours(-2)));
            Assert.False(repository.SaveListing("kourial", first, now.AddHours(-1)));
            Assert.True(repository.SaveListing("Tylezia", first, now.AddHours(-2)));
            Assert.True(repository.SaveListing("Kourial", new(2, secondSoul, 10, 4000, []), now));
            Assert.True(repository.SaveListing("Kourial", new(3, resource, 100, 1000, []), now.AddHours(-24)));
            Assert.True(repository.SaveListing("Kourial", new(4, resource, 10, 1000, []), now.AddMinutes(1)));
            var totals = repository.ReadListingTotals("Kourial", now);
            Assert.Equal(11, totals.ByBoss[boss]);
            Assert.Equal(10, totals.ByItem[secondSoul]);
            Assert.False(totals.ByItem.ContainsKey(resource));
            Assert.Empty(repository.ReadListingTotals("Tylezia", now).ByBoss);
            Assert.Equal(10, repository.ReadListingTotals("Kourial", now.AddHours(22)).ByBoss[boss]);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
