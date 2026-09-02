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

    private static string PrepareNumberImageForOcr(
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

        using Mat gray = new();

        Cv2.CvtColor(
            source,
            gray,
            ColorConversionCodes.BGR2GRAY
        );

        using Mat binary = new();

        Cv2.Threshold(
            gray,
            binary,
            0,
            255,
            ThresholdTypes.Binary |
            ThresholdTypes.Otsu
        );

        // Windows OCR est généralement plus à l'aise
        // avec du texte sombre sur fond clair.
        Cv2.BitwiseNot(
            binary,
            binary
        );

        using Mat enlarged = new();

        Cv2.Resize(
            binary,
            enlarged,
            new CvSize(
                binary.Width * 4,
                binary.Height * 4
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
                $"{Path.GetFileNameWithoutExtension(imagePath)}-number-ocr.png"
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

    public async Task<double?> RecognizeCoefficientAsync(
        string imageFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Le symbole % est parfois lu comme un zéro :
        // "371 %" devient alors "3710".
        //
        // On repère l'espace horizontal entre le nombre
        // et le symbole %, puis on OCR uniquement la partie
        // numérique. Un vrai 3710 % conserve donc bien ses
        // quatre chiffres.
        //
        // IMPORTANT :
        // le comportement de reconnaissance reste identique.
        // On conserve simplement, pour le DevTool, le texte
        // OCR brut de chaque étape afin de localiser exactement
        // une éventuelle transformation comme 98 -> 980.
        string numberOnlyImage =
            PrepareCoefficientNumberRegion(
                imageFilePath
            );

        string numberOnlyPreparedImage =
            PrepareImageForOcr(
                numberOnlyImage
            );

        string numberOnlyRawText =
            await RecognizeRawTextAsync(
                numberOnlyPreparedImage,
                cancellationToken
            );

        string numberOnlyText =
            NormalizeText(
                numberOnlyRawText
            );

        double? numberOnlyValue =
            TryParseCoefficientText(
                numberOnlyText
            );

        string? fallbackPreparedImage =
            null;

        string? fallbackRawText =
            null;

        string? fallbackText =
            null;

        double? fallbackValue =
            null;

        if (numberOnlyValue is null)
        {
            fallbackPreparedImage =
                PrepareImageForOcr(
                    imageFilePath
                );

            fallbackRawText =
                await RecognizeRawTextAsync(
                    fallbackPreparedImage,
                    cancellationToken
                );

            fallbackText =
                NormalizeText(
                    fallbackRawText
                );

            fallbackValue =
                TryParseCoefficientText(
                    fallbackText
                );
        }

        double? finalValue =
            numberOnlyValue
            ?? fallbackValue;

        await WriteCoefficientOcrDebugAsync(
            imageFilePath,
            numberOnlyImage,
            numberOnlyPreparedImage,
            numberOnlyRawText,
            numberOnlyText,
            numberOnlyValue,
            fallbackPreparedImage,
            fallbackRawText,
            fallbackText,
            fallbackValue,
            finalValue
        );

        return finalValue;
    }

    private static async Task
        WriteCoefficientOcrDebugAsync(
            string sourceImagePath,
            string numberOnlyImage,
            string numberOnlyPreparedImage,
            string numberOnlyRawText,
            string numberOnlyText,
            double? numberOnlyValue,
            string? fallbackPreparedImage,
            string? fallbackRawText,
            string? fallbackText,
            double? fallbackValue,
            double? finalValue)
    {
        try
        {
            string directory =
                Path.GetDirectoryName(
                    sourceImagePath
                ) ??
                Path.GetTempPath();

            string debugPath =
                Path.Combine(
                    directory,
                    $"{Path.GetFileNameWithoutExtension(sourceImagePath)}-coefficient-debug.txt"
                );

            StringBuilder debug =
                new();

            debug.AppendLine(
                "BESTCRUSH - COEFFICIENT OCR DEBUG"
            );

            debug.AppendLine(
                $"Source: {Path.GetFileName(sourceImagePath)}"
            );

            debug.AppendLine(
                $"UTC: {DateTime.UtcNow:O}"
            );

            debug.AppendLine();

            debug.AppendLine(
                "NUMBER-ONLY PASS"
            );

            debug.AppendLine(
                $"Digits region : {Path.GetFileName(numberOnlyImage)}"
            );

            debug.AppendLine(
                $"Digits source equals original : {Path.GetFullPath(numberOnlyImage).Equals(Path.GetFullPath(sourceImagePath), StringComparison.OrdinalIgnoreCase)}"
            );

            debug.AppendLine(
                $"OCR image     : {Path.GetFileName(numberOnlyPreparedImage)}"
            );

            debug.AppendLine(
                $"Raw escaped   : {EscapeOcrDebugText(numberOnlyRawText)}"
            );

            debug.AppendLine(
                $"Raw unicode   : {DescribeOcrCharacters(numberOnlyRawText)}"
            );

            debug.AppendLine(
                $"Normalized    : {EscapeOcrDebugText(numberOnlyText)}"
            );

            debug.AppendLine(
                $"Parsed        : {FormatNullableCoefficient(numberOnlyValue)}"
            );

            debug.AppendLine();

            if (fallbackPreparedImage is not null)
            {
                debug.AppendLine(
                    "FULL-CROP FALLBACK"
                );

                debug.AppendLine(
                    $"OCR image     : {Path.GetFileName(fallbackPreparedImage)}"
                );

                debug.AppendLine(
                    $"Raw escaped   : {EscapeOcrDebugText(fallbackRawText ?? string.Empty)}"
                );

                debug.AppendLine(
                    $"Raw unicode   : {DescribeOcrCharacters(fallbackRawText ?? string.Empty)}"
                );

                debug.AppendLine(
                    $"Normalized    : {EscapeOcrDebugText(fallbackText ?? string.Empty)}"
                );

                debug.AppendLine(
                    $"Parsed        : {FormatNullableCoefficient(fallbackValue)}"
                );

                debug.AppendLine();
            }
            else
            {
                debug.AppendLine(
                    "FULL-CROP FALLBACK: not executed (number-only pass accepted)"
                );

                debug.AppendLine();
            }

            debug.AppendLine(
                $"FINAL COEFFICIENT: {FormatNullableCoefficient(finalValue)}"
            );

            await File.WriteAllTextAsync(
                debugPath,
                debug.ToString()
            );
        }
        catch
        {
            // Le debug ne doit jamais casser
            // la lecture d'un coefficient.
        }
    }

    private static string FormatNullableCoefficient(
        double? coefficient)
    {
        return coefficient is double value
            ? value.ToString(
                "0.###",
                CultureInfo.InvariantCulture
            )
            : "null";
    }

    private static double? TryParseCoefficientText(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        Match match =
            Regex.Match(
                text,
                @"\d+(?:[.,]\d+)?"
            );

        if (!match.Success)
        {
            return null;
        }

        string number =
            match.Value.Replace(',', '.');

        if (!double.TryParse(
            number,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double coefficient))
        {
            return null;
        }

        return coefficient > 0
            ? coefficient
            : null;
    }

    private static string PrepareCoefficientNumberRegion(
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

        using Mat gray = new();
        using Mat bright = new();

        Cv2.CvtColor(
            source,
            gray,
            ColorConversionCodes.BGR2GRAY
        );

        Cv2.Threshold(
            gray,
            bright,
            120,
            255,
            ThresholdTypes.Binary
        );

        bool[] activeColumns =
            new bool[source.Width];

        int firstActive = -1;
        int lastActive = -1;

        for (int x = 0; x < source.Width; x++)
        {
            using Mat column =
                new(
                    bright,
                    new OpenCvSharp.Rect(
                        x,
                        0,
                        1,
                        bright.Height
                    )
                );

            bool active =
                Cv2.CountNonZero(column) > 0;

            activeColumns[x] = active;

            if (!active)
            {
                continue;
            }

            if (firstActive < 0)
            {
                firstActive = x;
            }

            lastActive = x;
        }

        if (firstActive < 0 ||
            lastActive <= firstActive)
        {
            return imagePath;
        }

        int bestGapStart = -1;
        int bestGapLength = 0;
        int cursor = firstActive;

        while (cursor <= lastActive)
        {
            if (activeColumns[cursor])
            {
                cursor++;
                continue;
            }

            int gapStart = cursor;

            while (
                cursor <= lastActive &&
                !activeColumns[cursor])
            {
                cursor++;
            }

            int gapLength =
                cursor - gapStart;

            if (gapLength >= 4 &&
                gapLength > bestGapLength)
            {
                bestGapStart = gapStart;
                bestGapLength = gapLength;
            }
        }

        int cropLeft =
            Math.Max(
                0,
                firstActive - 6
            );

        int cropRight =
            bestGapStart > firstActive
                ? bestGapStart
                : Math.Min(
                    source.Width,
                    lastActive + 7
                );

        if (cropRight <= cropLeft)
        {
            return imagePath;
        }

        OpenCvSharp.Rect cropRect =
            new(
                cropLeft,
                0,
                cropRight - cropLeft,
                source.Height
            );

        using Mat numberOnly =
            new(
                source,
                cropRect
            );

        string directory =
            Path.GetDirectoryName(imagePath)
            ?? Path.GetTempPath();

        string outputPath =
            Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(imagePath)}-coefficient-digits.png"
            );

        Cv2.ImWrite(
            outputPath,
            numberOnly
        );

        return outputPath;
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
        // Pour les quantites HDV, on conserve volontairement
        // le texte BRUT retourne par Windows OCR avant
        // NormalizeText(). Cela permet de comprendre les cas
        // ou un chiffre visuellement net (notamment "1")
        // revient pourtant null apres reconnaissance.
        string firstPreparedImage =
            PrepareImageForOcr(
                imagePath
            );

        string firstRawText =
            await RecognizeRawTextAsync(
                firstPreparedImage
            );

        string firstNormalizedText =
            NormalizeText(
                firstRawText
            );

        int? firstQuantity =
            TryParseMarketQuantity(
                firstNormalizedText
            );

        string? fallbackPreparedImage =
            null;

        string? fallbackRawText =
            null;

        string? fallbackNormalizedText =
            null;

        int? fallbackQuantity =
            null;

        if (firstQuantity is null)
        {
            fallbackPreparedImage =
                PrepareNumberImageForOcr(
                    imagePath
                );

            fallbackRawText =
                await RecognizeRawTextAsync(
                    fallbackPreparedImage
                );

            fallbackNormalizedText =
                NormalizeText(
                    fallbackRawText
                );

            fallbackQuantity =
                TryParseMarketQuantity(
                    fallbackNormalizedText
                );
        }

        MarketQuantityVisualRecognitionResult?
            visualRecognition =
                null;

        if (firstQuantity is null &&
            fallbackQuantity is null)
        {
            visualRecognition =
                RecognizeMarketQuantityVisually(
                    imagePath
                );
        }

        int? finalQuantity =
            firstQuantity
            ?? fallbackQuantity
            ?? visualRecognition?.Quantity;

        await WriteMarketQuantityOcrDebugAsync(
            imagePath,
            firstPreparedImage,
            firstRawText,
            firstNormalizedText,
            firstQuantity,
            fallbackPreparedImage,
            fallbackRawText,
            fallbackNormalizedText,
            fallbackQuantity,
            visualRecognition,
            finalQuantity
        );

        return finalQuantity;
    }

    private static MarketQuantityVisualRecognitionResult
        RecognizeMarketQuantityVisually(
            string imagePath)
    {
        using Mat source =
            Cv2.ImRead(
                imagePath,
                ImreadModes.Color
            );

        if (source.Empty())
        {
            return new MarketQuantityVisualRecognitionResult(
                null,
                "source image empty",
                [],
                null
            );
        }

        using Mat gray =
            new();

        Cv2.CvtColor(
            source,
            gray,
            ColorConversionCodes.BGR2GRAY
        );

        using Mat binary =
            new();

        Cv2.Threshold(
            gray,
            binary,
            0,
            255,
            ThresholdTypes.Binary |
            ThresholdTypes.Otsu
        );

        Cv2.FindContours(
            binary,
            out OpenCvSharp.Point[][] contours,
            out HierarchyIndex[] _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple
        );

        int minimumHeight =
            Math.Max(
                6,
                (int)Math.Round(
                    source.Height *
                    0.20
                )
            );

        int maximumHeight =
            Math.Max(
                minimumHeight,
                (int)Math.Round(
                    source.Height *
                    0.55
                )
            );

        int outerHorizontalMargin =
            Math.Max(
                2,
                (int)Math.Round(
                    source.Width *
                    0.05
                )
            );

        List<OpenCvSharp.Rect>
            components =
                [];

        foreach (
            OpenCvSharp.Point[] contour
            in contours)
        {
            OpenCvSharp.Rect rectangle =
                Cv2.BoundingRect(
                    contour
                );

            if (rectangle.Height <
                    minimumHeight ||
                rectangle.Height >
                    maximumHeight ||
                rectangle.Width < 2)
            {
                continue;
            }

            // Une bordure ou un fragment de panneau situé
            // contre le bord du crop ne doit jamais pouvoir
            // être interprété comme le chiffre "1".
            if (rectangle.X <=
                    outerHorizontalMargin ||
                rectangle.Right >=
                    source.Width -
                    outerHorizontalMargin)
            {
                continue;
            }

            using Mat componentRegion =
                new(
                    binary,
                    rectangle
                );

            int activePixels =
                Cv2.CountNonZero(
                    componentRegion
                );

            if (activePixels <
                Math.Max(
                    8,
                    (int)Math.Round(
                        rectangle.Height *
                        1.4
                    )
                ))
            {
                continue;
            }

            components.Add(
                rectangle
            );
        }

        components =
            components
                .OrderBy(
                    rectangle =>
                        rectangle.X
                )
                .ToList();

        string debugImagePath =
            WriteMarketQuantityVisualDebugImage(
                imagePath,
                source,
                components
            );

        if (components.Count is < 1 or > 4)
        {
            return new MarketQuantityVisualRecognitionResult(
                null,
                $"invalid component count: {components.Count}",
                components,
                debugImagePath
            );
        }

        int left =
            components.Min(
                rectangle =>
                    rectangle.Left
            );

        int right =
            components.Max(
                rectangle =>
                    rectangle.Right
            );

        int top =
            components.Min(
                rectangle =>
                    rectangle.Top
            );

        int bottom =
            components.Max(
                rectangle =>
                    rectangle.Bottom
            );

        double groupCenterX =
            (
                left +
                right
            ) /
            2.0;

        double groupCenterY =
            (
                top +
                bottom
            ) /
            2.0;

        // Les tailles de lot sont centrées dans leur case.
        // Cette vérification protège notamment contre les
        // bordures de panneau visibles dans un crop vide.
        if (groupCenterX <
                source.Width *
                0.25 ||
            groupCenterX >
                source.Width *
                0.75 ||
            groupCenterY <
                source.Height *
                0.20 ||
            groupCenterY >
                source.Height *
                0.80)
        {
            return new MarketQuantityVisualRecognitionResult(
                null,
                $"glyph group not centered: X={groupCenterX:0.0}, Y={groupCenterY:0.0}",
                components,
                debugImagePath
            );
        }

        int minimumComponentHeight =
            components.Min(
                rectangle =>
                    rectangle.Height
            );

        int maximumComponentHeight =
            components.Max(
                rectangle =>
                    rectangle.Height
            );

        if (minimumComponentHeight <= 0 ||
            maximumComponentHeight /
                (double)minimumComponentHeight >
                1.35)
        {
            return new MarketQuantityVisualRecognitionResult(
                null,
                "glyph heights are inconsistent",
                components,
                debugImagePath
            );
        }

        OpenCvSharp.Rect first =
            components[0];

        double firstRatio =
            first.Width /
            (double)first.Height;

        // Le premier glyphe doit avoir la silhouette étroite
        // du chiffre 1. Les glyphes suivants, s'ils existent,
        // doivent avoir la silhouette plus large du chiffre 0.
        if (firstRatio <
                0.25 ||
            firstRatio >
                0.62)
        {
            return new MarketQuantityVisualRecognitionResult(
                null,
                $"first glyph is not shaped like 1: ratio={firstRatio:0.00}",
                components,
                debugImagePath
            );
        }

        for (
            int index = 1;
            index < components.Count;
            index++)
        {
            OpenCvSharp.Rect current =
                components[index];

            double ratio =
                current.Width /
                (double)current.Height;

            if (ratio <
                    0.58 ||
                ratio >
                    1.05)
            {
                return new MarketQuantityVisualRecognitionResult(
                    null,
                    $"glyph {index + 1} is not shaped like 0: ratio={ratio:0.00}",
                    components,
                    debugImagePath
                );
            }

            OpenCvSharp.Rect previous =
                components[
                    index - 1
                ];

            int gap =
                current.Left -
                previous.Right;

            if (gap < 0 ||
                gap >
                    source.Width *
                    0.25)
            {
                return new MarketQuantityVisualRecognitionResult(
                    null,
                    $"invalid glyph gap before glyph {index + 1}: {gap}",
                    components,
                    debugImagePath
                );
            }
        }

        int quantity =
            components.Count switch
            {
                1 => 1,
                2 => 10,
                3 => 100,
                4 => 1000,
                _ => 0
            };

        return new MarketQuantityVisualRecognitionResult(
            quantity,
            "accepted by deterministic glyph analysis",
            components,
            debugImagePath
        );
    }

    private static string
        WriteMarketQuantityVisualDebugImage(
            string sourceImagePath,
            Mat source,
            IReadOnlyList<OpenCvSharp.Rect>
                components)
    {
        try
        {
            using Mat debug =
                source.Clone();

            foreach (
                OpenCvSharp.Rect rectangle
                in components)
            {
                Cv2.Rectangle(
                    debug,
                    rectangle,
                    new Scalar(
                        0,
                        255,
                        0
                    ),
                    1
                );
            }

            string directory =
                Path.GetDirectoryName(
                    sourceImagePath
                ) ??
                Path.GetTempPath();

            string outputPath =
                Path.Combine(
                    directory,
                    $"{Path.GetFileNameWithoutExtension(sourceImagePath)}-visual-ocr.png"
                );

            Cv2.ImWrite(
                outputPath,
                debug
            );

            return outputPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> RecognizeRawTextAsync(
        string imageFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        StorageFile file =
            await StorageFile
                .GetFileFromPathAsync(
                    imageFilePath
                );

        using IRandomAccessStream stream =
            await file.OpenAsync(
                FileAccessMode.Read
            );

        BitmapDecoder decoder =
            await BitmapDecoder
                .CreateAsync(
                    stream
                );

        using SoftwareBitmap bitmap =
            await decoder
                .GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied
                );

        OcrResult result =
            await _ocrEngine
                .RecognizeAsync(
                    bitmap
                );

        return result.Text
            ?? string.Empty;
    }

    private static async Task
        WriteMarketQuantityOcrDebugAsync(
            string sourceImagePath,
            string firstPreparedImage,
            string firstRawText,
            string firstNormalizedText,
            int? firstQuantity,
            string? fallbackPreparedImage,
            string? fallbackRawText,
            string? fallbackNormalizedText,
            int? fallbackQuantity,
            MarketQuantityVisualRecognitionResult?
                visualRecognition,
            int? finalQuantity)
    {
        try
        {
            string directory =
                Path.GetDirectoryName(
                    sourceImagePath
                ) ??
                Path.GetTempPath();

            string debugPath =
                Path.Combine(
                    directory,
                    $"{Path.GetFileNameWithoutExtension(sourceImagePath)}-ocr-debug.txt"
                );

            StringBuilder debug =
                new();

            debug.AppendLine(
                "BESTCRUSH - MARKET QUANTITY OCR DEBUG"
            );

            debug.AppendLine(
                $"Source: {Path.GetFileName(sourceImagePath)}"
            );

            debug.AppendLine();

            debug.AppendLine(
                "FIRST PASS (upscaled)"
            );

            debug.AppendLine(
                $"Image       : {Path.GetFileName(firstPreparedImage)}"
            );

            debug.AppendLine(
                $"Raw escaped : {EscapeOcrDebugText(firstRawText)}"
            );

            debug.AppendLine(
                $"Raw unicode : {DescribeOcrCharacters(firstRawText)}"
            );

            debug.AppendLine(
                $"Normalized  : {EscapeOcrDebugText(firstNormalizedText)}"
            );

            debug.AppendLine(
                $"Parsed      : {FormatNullableQuantity(firstQuantity)}"
            );

            debug.AppendLine();

            if (fallbackPreparedImage is not null)
            {
                debug.AppendLine(
                    "FALLBACK (number image)"
                );

                debug.AppendLine(
                    $"Image       : {Path.GetFileName(fallbackPreparedImage)}"
                );

                debug.AppendLine(
                    $"Raw escaped : {EscapeOcrDebugText(fallbackRawText ?? string.Empty)}"
                );

                debug.AppendLine(
                    $"Raw unicode : {DescribeOcrCharacters(fallbackRawText ?? string.Empty)}"
                );

                debug.AppendLine(
                    $"Normalized  : {EscapeOcrDebugText(fallbackNormalizedText ?? string.Empty)}"
                );

                debug.AppendLine(
                    $"Parsed      : {FormatNullableQuantity(fallbackQuantity)}"
                );

                debug.AppendLine();
            }
            else
            {
                debug.AppendLine(
                    "FALLBACK: not executed (first pass accepted)"
                );

                debug.AppendLine();
            }

            if (visualRecognition is not null)
            {
                debug.AppendLine(
                    "VISUAL FALLBACK"
                );

                debug.AppendLine(
                    $"Result      : {FormatNullableQuantity(visualRecognition.Quantity)}"
                );

                debug.AppendLine(
                    $"Reason      : {visualRecognition.Reason}"
                );

                debug.AppendLine(
                    $"Debug image : {Path.GetFileName(visualRecognition.DebugImagePath ?? string.Empty)}"
                );

                if (visualRecognition.Components.Count == 0)
                {
                    debug.AppendLine(
                        "Components  : <NONE>"
                    );
                }
                else
                {
                    debug.AppendLine(
                        "Components  :"
                    );

                    for (
                        int index = 0;
                        index <
                            visualRecognition
                                .Components
                                .Count;
                        index++)
                    {
                        OpenCvSharp.Rect component =
                            visualRecognition
                                .Components[
                                    index
                                ];

                        double ratio =
                            component.Width /
                            (double)component.Height;

                        debug.AppendLine(
                            $"  #{index + 1}: X={component.X}, Y={component.Y}, W={component.Width}, H={component.Height}, ratio={ratio:0.00}"
                        );
                    }
                }

                debug.AppendLine();
            }
            else
            {
                debug.AppendLine(
                    "VISUAL FALLBACK: not executed (Windows OCR accepted)"
                );

                debug.AppendLine();
            }

            debug.AppendLine(
                $"FINAL QUANTITY: {FormatNullableQuantity(finalQuantity)}"
            );

            await File.WriteAllTextAsync(
                debugPath,
                debug.ToString()
            );
        }
        catch
        {
            // Le debug ne doit jamais casser la lecture HDV.
        }
    }

    private static string EscapeOcrDebugText(
        string text)
    {
        if (text.Length == 0)
        {
            return "<EMPTY>";
        }

        return "\"" +
            text
                .Replace(
                    "\\",
                    "\\\\"
                )
                .Replace(
                    "\r",
                    "\\r"
                )
                .Replace(
                    "\n",
                    "\\n"
                )
                .Replace(
                    "\t",
                    "\\t"
                ) +
            "\"";
    }

    private static string DescribeOcrCharacters(
        string text)
    {
        if (text.Length == 0)
        {
            return "<NONE>";
        }

        return string.Join(
            " ",
            text.Select(
                character =>
                    $"'{EscapeOcrDebugCharacter(character)}'=U+{(int)character:X4}"
            )
        );
    }

    private static string EscapeOcrDebugCharacter(
        char character)
    {
        return character switch
        {
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            '\'' => "\\'",
            _ => character.ToString()
        };
    }

    private static string FormatNullableQuantity(
        int? quantity)
    {
        return quantity?.ToString()
            ?? "null";
    }
    private sealed record
        MarketQuantityVisualRecognitionResult(
            int? Quantity,
            string Reason,
            IReadOnlyList<OpenCvSharp.Rect>
                Components,
            string? DebugImagePath
        );

    private static int? TryParseMarketQuantity(
        string text)
    {
        if (string.IsNullOrWhiteSpace(
            text))
        {
            return null;
        }

        StringBuilder normalized =
            new();

        foreach (
            char character
            in text)
        {
            if (char.IsDigit(
                character))
            {
                normalized.Append(
                    character
                );

                continue;
            }

            switch (
                char.ToUpperInvariant(
                    character))
            {
                // Confusions OCR fréquentes avec 1.
                case 'I':
                case 'L':
                case '|':
                    normalized.Append(
                        '1'
                    );
                    break;

                // Confusion OCR fréquente avec 0.
                case 'O':
                    normalized.Append(
                        '0'
                    );
                    break;
            }
        }

        if (!int.TryParse(
            normalized.ToString(),
            out int quantity))
        {
            return null;
        }

        // IMPORTANT :
        // aucune approximation de quantité.
        // Une valeur comme 102 reste invalide.
        return quantity is
            1 or 10 or 100 or 1000
                ? quantity
                : null;
    }

    private static long? TryParseOcrNumber(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        StringBuilder normalized =
            new();

        foreach (char character in text)
        {
            if (char.IsDigit(character))
            {
                normalized.Append(character);

                continue;
            }

            switch (char.ToUpperInvariant(character))
            {
                // Confusions courantes OCR.
                case 'I':
                case 'L':
                case '|':
                    normalized.Append('1');
                    break;

                case 'O':
                    normalized.Append('0');
                    break;

                case 'S':
                    normalized.Append('5');
                    break;

                case 'G':
                    normalized.Append('6');
                    break;
            }
        }

        if (long.TryParse(
            normalized.ToString(),
            out long value) &&
            value > 0)
        {
            return value;
        }

        return null;
    }

    public async Task<long?> RecognizeNumberAsync(
        string imagePath)
    {
        // Premier essai :
        // image originale simplement agrandie.
        string text =
            await RecognizeUpscaledTextAsync(
                imagePath
            );

        long? value =
            TryParseOcrNumber(
                text
            );

        if (value is not null)
        {
            return value;
        }

        // Deuxième essai :
        // image spécialement préparée pour
        // les nombres.
        string preparedImage =
            PrepareNumberImageForOcr(
                imagePath
            );

        string fallbackText =
            await RecognizeTextAsync(
                preparedImage
            );

        return TryParseOcrNumber(
            fallbackText
        );
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

    public async Task<IReadOnlyList<DofusOcrLine>>
        RecognizeLinesAsync(
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

        List<DofusOcrLine> lines = [];

        foreach (OcrLine line in result.Lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            string text =
                string.Join(
                    " ",
                    line.Words.Select(
                        word => word.Text
                    )
                ).Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            double left =
                line.Words.Min(
                    word =>
                        word.BoundingRect.Left
                );

            double top =
                line.Words.Min(
                    word =>
                        word.BoundingRect.Top
                );

            double right =
                line.Words.Max(
                    word =>
                        word.BoundingRect.Right
                );

            double bottom =
                line.Words.Max(
                    word =>
                        word.BoundingRect.Bottom
                );

            lines.Add(
                new DofusOcrLine(
                    text,
                    left,
                    top,
                    right - left,
                    bottom - top
                )
            );
        }

        return lines;
    }

    public async Task<IReadOnlyList<DofusOcrLine>>
        RecognizeTooltipLinesAsync(
            string imageFilePath,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using Mat source =
            Cv2.ImRead(
                imageFilePath,
                ImreadModes.Color
            );

        if (source.Empty())
        {
            return await RecognizeLinesAsync(
                imageFilePath,
                cancellationToken
            );
        }

        const int tileWidth = 960;
        const int tileHeight = 540;
        const int overlap = 140;
        const double scale = 2.0;

        IReadOnlyList<int> xStarts =
            GetTileStarts(
                source.Width,
                tileWidth,
                overlap
            );

        IReadOnlyList<int> yStarts =
            GetTileStarts(
                source.Height,
                tileHeight,
                overlap
            );

        List<DofusOcrLine> allLines = [];

        string directory =
            Path.GetDirectoryName(
                imageFilePath
            )
            ?? Path.GetTempPath();

        int tileIndex = 0;

        foreach (int y in yStarts)
        {
            foreach (int x in xStarts)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                int width =
                    Math.Min(
                        tileWidth,
                        source.Width - x
                    );

                int height =
                    Math.Min(
                        tileHeight,
                        source.Height - y
                    );

                if (width <= 0 ||
                    height <= 0)
                {
                    continue;
                }

                using Mat tile =
                    new(
                        source,
                        new OpenCvSharp.Rect(
                            x,
                            y,
                            width,
                            height
                        )
                    );

                using Mat enlarged =
                    new();

                Cv2.Resize(
                    tile,
                    enlarged,
                    new CvSize(
                        (int)Math.Round(
                            width * scale
                        ),
                        (int)Math.Round(
                            height * scale
                        )
                    ),
                    0,
                    0,
                    InterpolationFlags.Cubic
                );

                string tilePath =
                    Path.Combine(
                        directory,
                        $"tooltip-ocr-tile-{tileIndex++}.png"
                    );

                Cv2.ImWrite(
                    tilePath,
                    enlarged
                );

                IReadOnlyList<DofusOcrLine>
                    tileLines =
                        await RecognizeLinesAsync(
                            tilePath,
                            cancellationToken
                        );

                foreach (
                    DofusOcrLine line
                    in tileLines)
                {
                    allLines.Add(
                        new DofusOcrLine(
                            line.Text,

                            x +
                            line.X /
                            scale,

                            y +
                            line.Y /
                            scale,

                            line.Width /
                            scale,

                            line.Height /
                            scale
                        )
                    );
                }
            }
        }

        return allLines;
    }

    private static IReadOnlyList<int>
        GetTileStarts(
            int totalSize,
            int tileSize,
            int overlap)
    {
        if (totalSize <= tileSize)
        {
            return [0];
        }

        int step =
            tileSize -
            overlap;

        List<int> starts = [];

        for (
            int position = 0;
            position < totalSize - tileSize;
            position += step)
        {
            starts.Add(
                position
            );
        }

        starts.Add(
            totalSize -
            tileSize
        );

        return starts
            .Distinct()
            .OrderBy(value => value)
            .ToList();
    }
}

public sealed record DofusOcrLine(
    string Text,
    double X,
    double Y,
    double Width,
    double Height
);