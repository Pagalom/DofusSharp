namespace BestCrush.Services;

/// <summary>
/// Static public Dofus class-name labels for already confirmed class IDs.
/// Never converts a window title into a verified protocol identity.
/// </summary>
public static class DofusClassDisplayNames
{
    public static string ForId(int? id) => id switch
    {
        1 => "Féca",
        2 => "Osamodas",
        3 => "Enutrof",
        4 => "Sram",
        5 => "Xélor",
        6 => "Écaflip",
        7 => "Eniripsa",
        8 => "Iop",
        9 => "Crâ",
        10 => "Sadida",
        11 => "Sacrieur",
        12 => "Pandawa",
        13 => "Roublard",
        14 => "Zobal",
        15 => "Steamer",
        16 => "Éliotrope",
        17 => "Huppermage",
        18 => "Ouginak",
        19 => "Forgelance",
        _ => "Classe non observée"
    };
}
