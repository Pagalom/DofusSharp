using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services.Upgrades;
using DofusSharp.DofusDb.ApiClients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

/// <summary>
/// Runs database migration and game-data upgrades in an owned DI scope.
/// The gate prevents overlapping initializations when the splash page is
/// recreated, and prevents a page-scoped DbContext from being disposed
/// while its startup work is still running.
/// </summary>
public sealed class StartupInitializationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Func<
        IServiceProvider,
        ProgressSync<ProgressMessage>,
        CancellationToken,
        Task> _initialize;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _completed;

    public StartupInitializationService(
        IServiceScopeFactory scopeFactory,
        IDofusDbClientsFactory dofusDbClientsFactory,
        ILogger<StartupInitializationService> logger)
        : this(
            scopeFactory,
            async (provider, progress, cancellationToken) =>
            {
                BestCrushDbContext db =
                    provider.GetRequiredService<BestCrushDbContext>();

                progress.Report(
                    new ProgressMessage("Migration de la base de données...", null));

                logger.LogInformation("Applying BestCrush database migrations.");
                await db.Database.MigrateAsync(cancellationToken);

                progress.Report(
                    new ProgressMessage("Mise à jour des données...", 0));

                ApplicationUpgradesHandler appUpgrades =
                    provider.GetRequiredService<ApplicationUpgradesHandler>();

                await appUpgrades.UpgradeAsync(
                    CurrentVersion.Version,
                    progress.DeriveSubtask(0, 50),
                    cancellationToken);

                Version gameVersion = await dofusDbClientsFactory
                    .Version()
                    .GetVersionAsync(cancellationToken);

                GameDataUpgradeHandler gameUpgrades =
                    provider.GetRequiredService<GameDataUpgradeHandler>();

                await gameUpgrades.UpgradeAsync(
                    gameVersion,
                    progress.DeriveSubtask(50, 100),
                    cancellationToken);

                logger.LogInformation("BestCrush database initialization completed.");
            })
    {
    }

    // Minimal seam for testing overlapping initialization and scope disposal.
    internal StartupInitializationService(
        IServiceScopeFactory scopeFactory,
        Func<
            IServiceProvider,
            ProgressSync<ProgressMessage>,
            CancellationToken,
            Task> initialize)
    {
        _scopeFactory = scopeFactory;
        _initialize = initialize;
    }

    public async Task InitializeAsync(
        ProgressSync<ProgressMessage> progress,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_completed)
                return;

            await using AsyncServiceScope scope =
                _scopeFactory.CreateAsyncScope();

            await _initialize(
                scope.ServiceProvider,
                progress,
                cancellationToken);

            // Only successful initialization is memoized. After a failure,
            // the next launch can retry with a new owned scope.
            _completed = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
