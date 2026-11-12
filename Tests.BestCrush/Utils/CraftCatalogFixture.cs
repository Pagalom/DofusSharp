using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using BestCrush.Services;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.BestCrush.Utils;

internal sealed class CraftCatalogFixture : HttpMessageHandler, IHttpClientFactory
{
    internal sealed record Recipe(long ItemId, long JobId, int? Level);
    public Recipe[] Recipes = [];
    public object[] Skills = [];
    public int Calls;
    public bool Offline;
    public TaskCompletionSource? HoldRecipes;
    public readonly TaskCompletionSource RecipeRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public HttpClient CreateClient(string name) => new(this, false);

    public static CraftCatalogFixture Register(IServiceCollection services, string folder,
        CurrentWindowProfessionsState? professions = null, CraftCatalogFixture? fixture = null)
    {
        fixture ??= new();
        services.AddSingleton(professions ?? new CurrentWindowProfessionsState(new CurrentServerState()));
        services.AddSingleton(new ActivityCatalogService(DofusDbClient.Production(), fixture, Path.Combine(folder, "recipes")));
        return fixture;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Interlocked.Increment(ref Calls);
        if (Offline) throw new HttpRequestException("Offline public recipe fixture");
        var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        object[] data;
        if (request.RequestUri.AbsolutePath.Trim('/') == "jobs")
            data = [new { id = 16, name = new { fr = "Bijoutier" } }, new { id = 27, name = new { fr = "Tailleur" } },
                new { id = 11, name = new { fr = "Forgeron" } }, new { id = 65, name = new { fr = "Bricoleur" } }];
        else if (request.RequestUri.AbsolutePath.Trim('/') == "skills") data = Skills;
        else
        {
            Assert.Equal("recipes", request.RequestUri.AbsolutePath.Trim('/'));
            RecipeRequest.TrySetResult();
            if (HoldRecipes is { } hold) await hold.Task.WaitAsync(token);
            var ids = (query.GetValues("resultId[$in][]") ?? []).Select(long.Parse).ToHashSet();
            data = Recipes.Where(x => ids.Contains(x.ItemId)).OrderBy(x => x.ItemId)
                .Select(x => (object)new { id = x.ItemId, resultId = x.ItemId, jobId = x.JobId, resultLevel = x.Level }).ToArray();
        }
        int skip = int.Parse(query["$skip"] ?? "0");
        int limit = int.Parse(query["$limit"] ?? "50");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(
            new { total = data.Length, limit, skip, data = data.Skip(skip).Take(limit).ToArray() }), Encoding.UTF8, "application/json") };
    }
}
