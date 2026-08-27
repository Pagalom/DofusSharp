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

        Resource? prefixMatch =
            resources
                .Select(resource => new
                {
                    Resource = resource,
                    NormalizedName =
                        Normalize(resource.Name)
                })
                .Where(candidate =>
                    normalizedInput.StartsWith(
                        candidate.NormalizedName,
                        StringComparison.Ordinal
                    ) &&
                    (
                        normalizedInput.Length ==
                            candidate.NormalizedName.Length ||
                        char.IsWhiteSpace(
                            normalizedInput[
                                candidate.NormalizedName.Length
                            ]
                        )
                    )
                )
                .OrderByDescending(candidate =>
                    candidate.NormalizedName.Length)
                .Select(candidate =>
                    candidate.Resource)
                .FirstOrDefault();

        if (prefixMatch is not null)
        {
            return new ResourceRecognitionResult(
                prefixMatch,
                1.0
            );
        }

        List<ResourceRecognitionResult> matches =
            resources
                .Select(resource =>
                {
                    double confidence =
                        Similarity(
                            normalizedInput,
                            Normalize(resource.Name)
                        );

                    return new ResourceRecognitionResult(
                        resource,
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
}

public sealed record ResourceRecognitionResult(
    Resource Resource,
    double Confidence
);