using System.Collections.ObjectModel;

namespace BestCrush.Network.Protocol;

/// <summary>
/// PUBLIC name-ID reference used by the opt-in P3g probe ONLY.
/// Source: https://www.frigost.dev/API/Dofus%203.0/Data/Jobs.json
/// (directory dated 2026-10-09). This is not the runtime BestCrush mapping.
/// The source lists 23 IDs. P3g.1 observed 20 distinct known IDs in ipz.
/// </summary>
public static class ProfessionPublicJobIdReference
{
    public static IReadOnlyDictionary<string, ulong> ByName { get; } =
        new ReadOnlyDictionary<string, ulong>(
            new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
            {
                ["Base"] = 1,
                ["Bûcheron"] = 2,
                ["Forgeron"] = 11,
                ["Sculpteur"] = 13,
                ["Cordonnier"] = 15,
                ["Bijoutier"] = 16,
                ["Mineur"] = 24,
                ["Alchimiste"] = 26,
                ["Tailleur"] = 27,
                ["Paysan"] = 28,
                ["Pêcheur"] = 36,
                ["Chasseur"] = 41,
                ["Forgemage"] = 44,
                ["Sculptemage"] = 48,
                ["Façonneur"] = 60,
                ["Cordomage"] = 62,
                ["Joaillomage"] = 63,
                ["Costumage"] = 64,
                ["Bricoleur"] = 65,
                ["Façomage"] = 74,
                ["Parchomage"] = 75,
                ["Bestiologue"] = 78,
                ["Éleveur"] = 79
            });

    /// <summary>
    /// Twenty IDs observed together in an anonymized Dofus 3.6.11.15
    /// ipz roster (P3g.1, 2026-10-19 UTC). Name associations beyond
    /// the directly tested jobs still rely on the public reference.
    /// Base, Bestiologue and Parchomage remain public extras.
    /// </summary>
    public static IReadOnlyList<ulong> ReferenceTwenty { get; } =
        Array.AsReadOnly(new ulong[]
        {
            2, 11, 13, 15, 16, 24, 26, 27, 28, 36,
            41, 44, 48, 60, 62, 63, 64, 65, 74, 79
        });
}
