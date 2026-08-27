namespace BestCrush.Services;

public sealed class CurrentServerState
{
    public string? ServerName { get; private set; }

    public bool HasSelectedServer =>
        !string.IsNullOrWhiteSpace(ServerName);

    public void SelectServer(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            throw new ArgumentException(
                "Le nom du serveur ne peut pas être vide.",
                nameof(serverName)
            );
        }

        ServerName = serverName;
    }

    public void Clear()
    {
        ServerName = null;
    }
}