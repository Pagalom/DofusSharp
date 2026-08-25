using System.Net.Http.Headers;
using DofusSharp.Common;

namespace DofusSharp.Dofocus.ApiClients;

internal static class DofocusHttpClientFactory
{
    private static readonly Uri Referrer = new("https://dofocus.fr/");

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/151.0.0.0 Safari/537.36";

    public static HttpClient Create(
        IHttpClientFactory? httpClientFactory,
        Uri baseAddress)
    {
        HttpClient httpClient = HttpClientUtils.CreateHttpClient(
            httpClientFactory,
            baseAddress,
            Referrer
        );

        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );

        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Origin",
            "https://dofocus.fr"
        );

        return httpClient;
    }
}