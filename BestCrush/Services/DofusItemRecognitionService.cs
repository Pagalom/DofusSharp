using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed class DofusItemRecognitionService(
    ItemsService itemsService)
{
    public async Task<ItemRecognitionResult?>
        RecognizeEquipmentAsync(
            string recognizedText)
    {
        string itemNameText =
            ExtractItemName(
                recognizedText
            );

        string normalizedInput =
            Normalize(
                itemNameText
            );

        if (string.IsNullOrWhiteSpace(
            normalizedInput))
        {
            return null;
        }

        IReadOnlyCollection<Equipment>
            equipments =
                await itemsService
                    .GetAllEquipmentsAsync();

        // Un nom exact reste la preuve la plus forte.
        // On le teste avant les métadonnées OCR afin
        // qu'un niveau/type mal lu ne casse jamais une
        // reconnaissance de nom certaine.
        Equipment? exactMatch =
            equipments.FirstOrDefault(
                equipment =>
                    string.Equals(
                        Normalize(
                            equipment.Name
                        ),
                        normalizedInput,
                        StringComparison.Ordinal
                    )
            );

        if (exactMatch is not null)
        {
            return new ItemRecognitionResult(
                exactMatch,
                1.0
            );
        }

        int? recognizedLevel =
            ExtractLevel(
                recognizedText
            );

        EquipmentType? recognizedType =
            ExtractEquipmentType(
                recognizedText
            );

        IReadOnlyCollection<Equipment>
            candidates =
                NarrowCandidates(
                    equipments,
                    recognizedLevel,
                    recognizedType
                );

        List<ItemRecognitionResult> matches =
            candidates
                .Select(equipment =>
                {
                    string normalizedName =
                        Normalize(
                            equipment.Name
                        );

                    double levenshtein =
                        Similarity(
                            normalizedInput,
                            normalizedName
                        );

                    // Si l'OCR a perdu le début ou la fin
                    // d'un nom, la proportion de mots communs
                    // permet de récupérer le candidat.
                    //
                    // Le score reste plafonné à 0.92 :
                    // il ne peut donc jamais se faire passer
                    // pour une correspondance exacte.
                    double tokenContainment =
                        TokenContainmentSimilarity(
                            normalizedInput,
                            normalizedName
                        );

                    double confidence =
                        Math.Max(
                            levenshtein,
                            tokenContainment * 0.92
                        );

                    return new ItemRecognitionResult(
                        equipment,
                        confidence
                    );
                })
                .OrderByDescending(
                    result =>
                        result.Confidence
                )
                .ToList();

        if (matches.Count == 0)
        {
            return null;
        }

        ItemRecognitionResult best =
            matches[0];

        if (best.Confidence >= 0.999)
        {
            return best;
        }

        if (best.Confidence < 0.82)
        {
            return null;
        }

        if (matches.Count > 1)
        {
            ItemRecognitionResult second =
                matches[1];

            // Même avec les métadonnées, deux noms
            // quasiment équivalents restent ambigus :
            // ne jamais choisir arbitrairement.
            if (best.Confidence -
                    second.Confidence <
                0.05)
            {
                return null;
            }
        }

        return best;
    }

    private static IReadOnlyCollection<Equipment>
        NarrowCandidates(
            IReadOnlyCollection<Equipment>
                equipments,
            int? recognizedLevel,
            EquipmentType? recognizedType)
    {
        IReadOnlyCollection<Equipment>
            candidates =
                equipments;

        if (recognizedType is not null)
        {
            Equipment[] sameType =
                candidates
                    .Where(
                        equipment =>
                            equipment.Type ==
                            recognizedType.Value
                    )
                    .ToArray();

            // Une métadonnée OCR n'est utilisée que
            // lorsqu'elle produit effectivement des
            // candidats. Sinon on la considère douteuse.
            if (sameType.Length > 0)
            {
                candidates =
                    sameType;
            }
        }

        if (recognizedLevel is not null)
        {
            Equipment[] sameLevel =
                candidates
                    .Where(
                        equipment =>
                            Math.Abs(
                                equipment.Level -
                                recognizedLevel.Value
                            ) <= 1
                    )
                    .ToArray();

            if (sameLevel.Length > 0)
            {
                candidates =
                    sameLevel;
            }
        }

        return candidates;
    }

    private static string ExtractItemName(
        string recognizedText)
    {
        if (string.IsNullOrWhiteSpace(
            recognizedText))
        {
            return string.Empty;
        }

        string[] lines =
            recognizedText
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions
                        .RemoveEmptyEntries
                )
                .Select(
                    line =>
                        line.Trim()
                )
                .Where(
                    line =>
                        !string.IsNullOrWhiteSpace(
                            line
                        )
                )
                .ToArray();

        if (lines.Length == 0)
        {
            return string.Empty;
        }

        // Le bandeau Dofus place toujours le nom
        // d'objet sur la première ligne. C'est plus
        // robuste que de concaténer "Niveau ... Cape".
        string text =
            lines[0];

        int shortLevelIndex =
            text.IndexOf(
                "Niv.",
                StringComparison.OrdinalIgnoreCase
            );

        int fullLevelIndex =
            text.IndexOf(
                "Niveau",
                StringComparison.OrdinalIgnoreCase
            );

        int levelIndex =
            new[]
            {
                shortLevelIndex,
                fullLevelIndex
            }
            .Where(index =>
                index > 0)
            .DefaultIfEmpty(-1)
            .Min();

        if (levelIndex > 0)
        {
            text =
                text[..levelIndex]
                    .Trim();
        }

        return text
            .TrimEnd(
                '•',
                '·',
                '-',
                ' '
            );
    }

    private static int? ExtractLevel(
        string recognizedText)
    {
        string normalized =
            Normalize(
                recognizedText
            );

        Match match =
            Regex.Match(
                normalized,
                @"\b(?:niv|niveau)\s+(\d{1,3})\b",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant
            );

        if (!match.Success ||
            !int.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int level))
        {
            return null;
        }

        return level;
    }

    private static EquipmentType?
        ExtractEquipmentType(
            string recognizedText)
    {
        string normalizedText =
            $" {Normalize(recognizedText)} ";

        foreach (
            EquipmentType type
            in Enum.GetValues<EquipmentType>())
        {
            string normalizedType =
                Normalize(
                    type.ToDisplayName()
                );

            if (normalizedText.Contains(
                $" {normalizedType} ",
                StringComparison.Ordinal))
            {
                return type;
            }
        }

        return null;
    }

    private static string Normalize(
        string value)
    {
        string decomposed =
            value
                .Trim()
                .ToLowerInvariant()
                .Replace('’', '\'')
                .Normalize(
                    NormalizationForm.FormD
                );

        StringBuilder result =
            new();

        bool previousWasSeparator =
            true;

        foreach (
            char character
            in decomposed)
        {
            UnicodeCategory category =
                CharUnicodeInfo
                    .GetUnicodeCategory(
                        character
                    );

            if (category ==
                UnicodeCategory
                    .NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(
                character))
            {
                result.Append(
                    character
                );

                previousWasSeparator =
                    false;

                continue;
            }

            // Ponctuation, apostrophe et tirets deviennent
            // un unique séparateur. Cela rend par exemple
            // "Père-Phorreur", "Père Phorreur" et les
            // variantes OCR comparables.
            if (!previousWasSeparator)
            {
                result.Append(' ');
                previousWasSeparator =
                    true;
            }
        }

        return result
            .ToString()
            .Trim()
            .Normalize(
                NormalizationForm.FormC
            );
    }

    private static double
        TokenContainmentSimilarity(
            string first,
            string second)
    {
        string[] firstTokens =
            first.Split(
                ' ',
                StringSplitOptions
                    .RemoveEmptyEntries |
                StringSplitOptions
                    .TrimEntries
            );

        string[] secondTokens =
            second.Split(
                ' ',
                StringSplitOptions
                    .RemoveEmptyEntries |
                StringSplitOptions
                    .TrimEntries
            );

        if (firstTokens.Length == 0 ||
            secondTokens.Length == 0)
        {
            return 0.0;
        }

        HashSet<string> firstSet =
            firstTokens.ToHashSet(
                StringComparer.Ordinal
            );

        HashSet<string> secondSet =
            secondTokens.ToHashSet(
                StringComparer.Ordinal
            );

        int common =
            firstSet.Count(
                secondSet.Contains
            );

        int denominator =
            Math.Min(
                firstSet.Count,
                secondSet.Count
            );

        return denominator == 0
            ? 0.0
            : (double)common /
                denominator;
    }

    private static double Similarity(
        string first,
        string second)
    {
        if (first == second)
        {
            return 1.0;
        }

        int maximumLength =
            Math.Max(
                first.Length,
                second.Length
            );

        if (maximumLength == 0)
        {
            return 1.0;
        }

        int distance =
            LevenshteinDistance(
                first,
                second
            );

        return 1.0 -
            ((double)distance /
                maximumLength);
    }

    private static int LevenshteinDistance(
        string first,
        string second)
    {
        int[,] matrix =
            new int[
                first.Length + 1,
                second.Length + 1
            ];

        for (
            int i = 0;
            i <= first.Length;
            i++)
        {
            matrix[i, 0] =
                i;
        }

        for (
            int j = 0;
            j <= second.Length;
            j++)
        {
            matrix[0, j] =
                j;
        }

        for (
            int i = 1;
            i <= first.Length;
            i++)
        {
            for (
                int j = 1;
                j <= second.Length;
                j++)
            {
                int cost =
                    first[i - 1] ==
                    second[j - 1]
                        ? 0
                        : 1;

                matrix[i, j] =
                    Math.Min(
                        Math.Min(
                            matrix[i - 1, j] + 1,
                            matrix[i, j - 1] + 1
                        ),
                        matrix[
                            i - 1,
                            j - 1
                        ] +
                        cost
                    );
            }
        }

        return matrix[
            first.Length,
            second.Length
        ];
    }
}

public sealed record ItemRecognitionResult(
    Equipment Equipment,
    double Confidence
);
