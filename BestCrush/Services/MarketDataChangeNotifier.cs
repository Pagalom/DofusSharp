using BestCrush.Domain.Models;

namespace BestCrush.Services;

public sealed record MarketDataChangedEventArgs(
    MarketObjectType ObjectType,
    long DofusDbId,
    string ServerName,
    int Quantity
);

public sealed class MarketDataChangeNotifier
{
    public event EventHandler<MarketDataChangedEventArgs>?
        Changed;

    public void Notify(
        MarketObjectType objectType,
        long dofusDbId,
        string serverName,
        int quantity = 0)
    {
        Changed?.Invoke(
            this,
            new MarketDataChangedEventArgs(
                objectType,
                dofusDbId,
                serverName,
                quantity
            )
        );
    }
}
