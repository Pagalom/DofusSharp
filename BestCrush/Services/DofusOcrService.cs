using System.Text.RegularExpressions;
using OpenCvSharp;
using CvSize = OpenCvSharp.Size;

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

    public async Task<int?> RecognizeTooltipLotQuantityAsync(
        string imageFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string text =
            await RecognizeUpscaledTextAsync(
                imageFilePath
            );

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string normalized =
            Regex.Replace(
                text
                    .ToUpperInvariant()
                    .Replace('–', '-')
                    .Replace('—', '-'),
                @"\s+",
                " "
            );

        // Une pile de plusieurs runes affiche par
        // exemple :
        //
        // POIDS 1 - LOT 2
        //
        // Plus bas, l'infobulle peut aussi afficher
        // "- LOT 104 K", qui correspond au prix du lot.
        //
        // On exige donc que LOT soit rattaché à POIDS.
        Match match =
            Regex.Match(
                normalized,
                @"POIDS.{0,60}?\bLOT\s*[:\-]?\s*(\d+)\b",
                RegexOptions.IgnoreCase
            );

        if (!match.Success)
        {
            // Absence de "LOT n" près de POIDS :
            // Dofus affiche alors une seule rune.
            return null;
        }

        if (!int.TryParse(
            match.Groups[1].Value,
            out int quantity))
        {
            return null;
        }

        return quantity > 0
            ? quantity
            : null;
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
