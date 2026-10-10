using BestCrush.Domain.Models;
using BestCrush.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.BestCrush;

public sealed class StartupInitializationServiceTest
{
    [Fact]
    public async Task ConcurrentStartsShareOneInitializationAndRetainItsOwnScope()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddScoped<ScopedMarker>()
            .BuildServiceProvider();

        TaskCompletionSource<bool> entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        int executionCount = 0;
        ScopedMarker? marker = null;

        StartupInitializationService initializer = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            async (scopedProvider, _, _) =>
            {
                marker = scopedProvider.GetRequiredService<ScopedMarker>();
                Interlocked.Increment(ref executionCount);
                entered.TrySetResult(true);
                await release.Task;

                marker.IsDisposed.Should().BeFalse(
                    "the operation must retain its database/service scope");
            });

        ProgressSync<ProgressMessage> progress = new(_ => { });

        Task first = initializer.InitializeAsync(progress);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task second = initializer.InitializeAsync(progress);
        executionCount.Should().Be(1);

        release.SetResult(true);
        await Task.WhenAll(first, second);

        executionCount.Should().Be(1,
            "overlapping splash pages must not start two migrations");
        marker.Should().NotBeNull();
        marker!.IsDisposed.Should().BeTrue(
            "the owned scope must be disposed after initialization completes");
    }

    [Fact]
    public async Task FailureReleasesScopeAndAllowsLaterRetry()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddScoped<ScopedMarker>()
            .BuildServiceProvider();

        int attempts = 0;
        List<ScopedMarker> scopes = [];

        StartupInitializationService initializer = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            async (scopedProvider, _, _) =>
            {
                scopes.Add(scopedProvider.GetRequiredService<ScopedMarker>());
                await Task.Yield();
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new InvalidOperationException("Simulated failed migration");
            });

        ProgressSync<ProgressMessage> progress = new(_ => { });

        Func<Task> first = () => initializer.InitializeAsync(progress);
        await first.Should().ThrowAsync<InvalidOperationException>();

        await initializer.InitializeAsync(progress);
        await initializer.InitializeAsync(progress);

        attempts.Should().Be(2);
        scopes.Should().HaveCount(2);
        scopes.Should().OnlyContain(scope => scope.IsDisposed);
    }

    private sealed class ScopedMarker : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
