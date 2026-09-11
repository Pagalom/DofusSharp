using System.Globalization;
using System.Text;

using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

public sealed class DofusResourceRecognitionService(
    ItemsService itemsService)
{
    public async Task<ResourceRecognitionResult?>
        RecognizeResourceAsync(
            string recognizedText)
    {
        string itemNameText =
            ExtractItemName(recognizedText);

        string normalizedInput =
            Normalize(itemNameText);

        if (string.IsNullOrWhiteSpace(
            normalizedInput))
        {
            return null;
        }

        IReadOnlyCollection<Resource> resources =
            await itemsService
                .GetResourcesAsync();

        List<ResourceCandidate> candidates =
            resources
                .Select(resource =>
                    new ResourceCandidate(
                        resource,
                        Normalize(resource.Name)
                    )
                )
                .ToList();

        ResourceCandidate? exactMatch =
            candidates
                .FirstOrDefault(candidate =>
                    normalizedInput.Equals(
                        candidate.NormalizedName,
                        StringComparison.Ordinal
                    )
                );

        if (exactMatch is not null)
        {
            return new ResourceRecognitionResult(
                exactMatch.Resource,
                1.0
            );
        }

        ResourceCandidate? prefixMatch =
            candidates
                .Where(candidate =>
                    StartsWithWholeName(
                        normalizedInput,
                        candidate.NormalizedName
                    )
                )
                .OrderByDescending(candidate =>
                    candidate.NormalizedName.Length)
                .FirstOrDefault();

        if (prefixMatch is not null)
        {
            // Le texte OCR peut contenir des métadonnées après le nom
            // (type, niveau, etc.). On conserve donc le comportement
            // "préfixe exact = nom valide".
            //
            // Exception importante : si ce préfixe correspond aussi au
            // début d'un AUTRE nom de ressource plus long, on vérifie
            // d'abord si la suite du texte OCR correspond presque
            // parfaitement à cette ressource plus précise.
            //
            // Exemple :
            //   OCR       : "Pince de Crabe Yolonistc"
            //   préfixe   : "Pince de Crabe"
            //   vrai nom  : "Pince de Crabe Yoloniste"
            //
            // Le texte situé après le vrai nom n'est pas utilisé :
            // la comparaison d'une extension porte seulement sur les
            // premiers mots nécessaires pour couvrir ce nom candidat.
            List<ResourceRecognitionResult>
                longerPrefixMatches =
                    candidates
                        .Where(candidate =>
                            candidate.NormalizedName.Length >
                                prefixMatch.NormalizedName.Length &&
                            candidate.NormalizedName.StartsWith(
                                prefixMatch.NormalizedName + " ",
                                StringComparison.Ordinal
                            )
                        )
                        .Select(candidate =>
                        {
                            string comparableInput =
                                TakeLeadingWords(
                                    normalizedInput,
                                    CountWords(
                                        candidate.NormalizedName
                                    )
                                );

                            double confidence =
                                Similarity(
                                    comparableInput,
                                    candidate.NormalizedName
                                );

                            return new ResourceRecognitionResult(
                                candidate.Resource,
                                confidence
                            );
                        })
                        .OrderByDescending(result =>
                            result.Confidence)
                        .ToList();

            if (longerPrefixMatches.Count > 0)
            {
                ResourceRecognitionResult
                    bestLongerPrefixMatch =
                        longerPrefixMatches[0];

                bool isStrongLongerMatch =
                    bestLongerPrefixMatch.Confidence >=
                        0.90;

                bool isUnambiguousLongerMatch =
                    longerPrefixMatches.Count == 1 ||
                    bestLongerPrefixMatch.Confidence -
                        longerPrefixMatches[1].Confidence >=
                            0.05;

                if (isStrongLongerMatch &&
                    isUnambiguousLongerMatch)
                {
                    return bestLongerPrefixMatch;
                }
            }

            return new ResourceRecognitionResult(
                prefixMatch.Resource,
                1.0
            );
        }

        List<ResourceRecognitionResult> matches =
            candidates
                .Select(candidate =>
                {
                    double confidence =
                        Similarity(
                            normalizedInput,
                            candidate.NormalizedName
                        );

                    return new ResourceRecognitionResult(
                        candidate.Resource,
                        confidence
                    );
                })
                .OrderByDescending(result =>
                    result.Confidence)
                .ToList();

        if (matches.Count == 0)
        {
            return null;
        }

        ResourceRecognitionResult best =
            matches[0];

        if (best.Confidence < 0.82)
        {
            return null;
        }

        if (matches.Count > 1 &&
            best.Confidence -
            matches[1].Confidence < 0.05)
        {
            return null;
        }

        return best;
    }

    private static bool StartsWithWholeName(
        string input,
        string candidateName)
    {
        return input.StartsWith(
                   candidateName,
                   StringComparison.Ordinal
               ) &&
               (
                   input.Length ==
                       candidateName.Length ||
                   char.IsWhiteSpace(
                       input[
                           candidateName.Length
                       ]
                   )
               );
    }

    private static int CountWords(
        string value)
    {
        return value
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
            )
            .Length;
    }

    private static string TakeLeadingWords(
        string value,
        int wordCount)
    {
        if (wordCount <= 0)
        {
            return string.Empty;
        }

        string[] words =
            value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
            );

        return string.Join(
            ' ',
            words.Take(
                Math.Min(
                    wordCount,
                    words.Length
                )
            )
        );
    }

    private static string ExtractItemName(
        string recognizedText)
    {
        if (string.IsNullOrWhiteSpace(
            recognizedText))
        {
            return string.Empty;
        }

        string text =
            recognizedText.Trim();

        int levelIndex =
            text.IndexOf(
                "Niv.",
                StringComparison.OrdinalIgnoreCase
            );

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

        StringBuilder result = new();

        foreach (char character in decomposed)
        {
            UnicodeCategory category =
                CharUnicodeInfo
                    .GetUnicodeCategory(
                        character
                    );

            if (category !=
                UnicodeCategory.NonSpacingMark)
            {
                result.Append(character);
            }
        }

        return result
            .ToString()
            .Normalize(
                NormalizationForm.FormC
            );
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

        for (int i = 0;
             i <= first.Length;
             i++)
        {
            matrix[i, 0] = i;
        }

        for (int j = 0;
             j <= second.Length;
             j++)
        {
            matrix[0, j] = j;
        }

        for (int i = 1;
             i <= first.Length;
             i++)
        {
            for (int j = 1;
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
                        matrix[i - 1, j - 1] +
                        cost
                    );
            }
        }

        return matrix[
            first.Length,
            second.Length
        ];
    }

    private sealed record ResourceCandidate(
        Resource Resource,
        string NormalizedName
    );
}

public sealed record ResourceRecognitionResult(
    Resource Resource,
    double Confidence
);
