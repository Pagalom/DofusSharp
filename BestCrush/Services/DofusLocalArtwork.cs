using System.Globalization;
using System.Text;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

/// <summary>
/// Static, packaged DOFUS artwork paths. No remote URL, HTTP call or
/// runtime download. Files are installed once by scripts/install_dofus_artwork.ps1
/// under wwwroot/img/dofus, then bundled with the application.
/// Artwork is a generic class head, NOT the exact character's appearance.
/// </summary>
public static class DofusLocalArtwork
{
    public static string? ProfessionIcon(string? professionName)
    {
        if (string.IsNullOrWhiteSpace(professionName) ||
            !ProfessionPublicJobIdReference.ByName.TryGetValue(
                professionName, out ulong id) ||
            !ProfessionPublicJobIdReference.ReferenceTwenty.Contains(id))
            return null;

        return $"img/dofus/jobs/{id}.png";
    }

    public static string? ClassPortrait(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return null;

        string name = StripAccents(className).ToLowerInvariant().Trim();
        int id = name switch
        {
            "feca" => 1,
            "osamodas" => 2,
            "enutrof" => 3,
            "sram" => 4,
            "xelor" => 5,
            "ecaflip" => 6,
            "eniripsa" => 7,
            "iop" => 8,
            "cra" => 9,
            "sadida" => 10,
            "sacrieur" => 11,
            "pandawa" => 12,
            "roublard" => 13,
            "zobal" => 14,
            "steamer" => 15,
            "eliotrope" => 16,
            "huppermage" => 17,
            "ouginak" => 18,
            // The public art pack indexes Forgelance under breed ID 20.
            "forgelance" => 20,
            _ => 0
        };

        return id == 0 ? null : $"img/dofus/classes/{id}.png";
    }

    private static string StripAccents(string input)
    {
        string decomposed = input.Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) !=
                        UnicodeCategory.NonSpacingMark));
    }
}
