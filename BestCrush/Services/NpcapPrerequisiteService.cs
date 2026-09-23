#if WINDOWS
using Microsoft.Win32;
#endif

namespace BestCrush.Services;

/// <summary>
/// Detects whether the local Windows installation contains the Npcap
/// prerequisites required by SharpPcap. BestCrush never downloads or
/// redistributes Npcap; installation remains an explicit user action.
/// </summary>
public sealed class NpcapPrerequisiteService
{
    public const string DownloadUrl =
        "https://npcap.com/#download";

    private readonly object _sync = new();

    public bool HasChecked
    {
        get;
        private set;
    }

    public bool IsInstalled
    {
        get;
        private set;
    }

    public string StatusText
    {
        get;
        private set;
    } = "Vérification de Npcap...";

    public event EventHandler? Changed;

    public bool Refresh()
    {
        bool installed;
        string status;

#if WINDOWS
        try
        {
            string systemDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.System);

            string npcapDirectory =
                Path.Combine(
                    systemDirectory,
                    "Npcap");

            bool nativeLibraryPresent =
                File.Exists(
                    Path.Combine(
                        npcapDirectory,
                        "wpcap.dll")) &&
                File.Exists(
                    Path.Combine(
                        npcapDirectory,
                        "Packet.dll"));

            bool driverFilePresent =
                File.Exists(
                    Path.Combine(
                        systemDirectory,
                        "drivers",
                        "npcap.sys"));

            bool driverRegistered;

            using (
                RegistryKey? driverKey =
                    Registry.LocalMachine
                        .OpenSubKey(
                            @"SYSTEM\CurrentControlSet\Services\npcap"))
            {
                driverRegistered =
                    driverKey is not null;
            }

            installed =
                nativeLibraryPresent &&
                (driverFilePresent ||
                 driverRegistered);

            status =
                installed
                    ? "Npcap détecté."
                    : "Npcap n'est pas installé ou son installation est incomplète.";
        }
        catch (Exception ex)
        {
            installed = false;
            status =
                $"Impossible de vérifier Npcap : {ex.Message}";
        }
#else
        installed = true;
        status =
            "Npcap n'est requis que sous Windows.";
#endif

        bool changed;

        lock (_sync)
        {
            changed =
                !HasChecked ||
                IsInstalled != installed ||
                !string.Equals(
                    StatusText,
                    status,
                    StringComparison.Ordinal);

            HasChecked = true;
            IsInstalled = installed;
            StatusText = status;
        }

        if (changed)
        {
            Changed?.Invoke(
                this,
                EventArgs.Empty);
        }

        return installed;
    }
}
