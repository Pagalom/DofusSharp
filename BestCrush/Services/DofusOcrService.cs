using System.Globalization;
using System.Text.RegularExpressions;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;
using System.Text;

using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BestCrush.Services;

public sealed class DofusOcrService
{
    private readonly OcrEngine _ocrEngine;

    public DofusOcrService()
    {
        _ocrEngine =
            OcrEngine.TryCreateFromLanguage(
                new Language("fr-FR")
            )
            ?? OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "Aucun moteur OCR Windows compatible n'est disponible."
            );
    }

    public async Task<string> RecognizeTextAsync(
        string imageFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        StorageFile file =
            await StorageFile.GetFileFromPathAsync(
                imageFilePath
            );

        using IRandomAccessStream stream =
            await file.OpenAsync(
                FileAccessMode.Read
            );

        BitmapDecoder decoder =
            await BitmapDecoder.CreateAsync(
                stream
            );

        using SoftwareBitmap bitmap =
            await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied
            );

        OcrResult result =
            await _ocrEngine.RecognizeAsync(
                bitmap
            );

        return NormalizeText(
            result.Text
        );
    }

    private static string PrepareImageForOcr(
        string imagePath)
    {
        using Mat source =
            Cv2.ImRead(
                imagePath,
                ImreadModes.Color
            );

        if (source.Empty())
        {
            return imagePath;
        }

        using Mat enlarged = new();

        Cv2.Resize(
            source,
            enlarged,
            new CvSize(
                source.Width * 4,
                source.Height * 4
            ),
            0,
            0,
            InterpolationFlags.Cubic
        );

        string directory =
            Path.GetDirectoryName(imagePath)
            ?? Path.GetTempPath();

        string preparedPath =
            Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(imagePath)}-ocr.png"
            );

        Cv2.ImWrite(
            preparedPath,
            enlarged
        );

        return preparedPath;
    }

    public async Task<string> RecognizeUpscaledTextAsync(
        string imagePath)
    {
        string preparedImage =
            PrepareImageForOcr(
                imagePath
            );

        return await RecognizeTextAsync(
            preparedImage
        );
    }

    public async Task<double?> RecognizeCoefficientAsync(
        string imageFilePath,
        CancellationToken cancellationToken = default)
    {
        string text =
            await RecognizeTextAsync(
                imageFilePath,
                cancellationToken
            );

        Match match =
            Regex.Match(
                text,
                @"(\d+(?:[.,]\d+)?)\s*%"
            );

        if (!match.Success)
        {
            return null;
        }

        string number =
            match.Groups[1]
                .Value
                .Replace(',', '.');

        if (!double.TryParse(
            number,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double coefficient))
        {
            return null;
        }

        return coefficient;
    }

    public Task<long?> RecognizePriceAsync(
        string imagePath)
    {
        return RecognizeNumberAsync(
            imagePath
        );
    }

    public async Task<int?> RecognizeMarketQuantityAsync(
        string imagePath)
    {
        string text =
            await RecognizeUpscaledTextAsync(
                imagePath
            );

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        StringBuilder normalized = new();

        foreach (char character in text)
        {
            if (char.IsDigit(character))
            {
                normalized.Append(character);
                continue;
            }

            switch (char.ToUpperInvariant(character))
            {
                // Confusions OCR fréquentes avec le chiffre 1.
                case 'I':
                case 'L':
                case '|':
                    normalized.Append('1');
                    break;

                // Confusion OCR fréquente avec le chiffre 0.
                case 'O':
                    normalized.Append('0');
                    break;
            }
        }

        if (!int.TryParse(
            normalized.ToString(),
            out int quantity))
        {
            return null;
        }

        return quantity is
            1 or 10 or 100 or 1000
                ? quantity
                : null;
    }

    public async Task<long?> RecognizeNumberAsync(
        string imagePath)
    {
        string text =
            await RecognizeUpscaledTextAsync(
                imagePath
            );

        string digits =
            new(
                text
                    .Where(char.IsDigit)
                    .ToArray()
            );

        if (long.TryParse(
            digits,
            out long value))
        {
            return value;
        }

        return null;
    }
    private static string NormalizeText(
        string text)
    {
        return Regex.Replace(
            text,
            @"\s+",
            " "
        ).Trim();
    }
}