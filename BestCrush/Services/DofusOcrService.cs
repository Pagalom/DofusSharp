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

    public async Task<double?> RecognizeCoefficientAsync(
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

        // La zone ne contient que le coefficient.
        // On ne dépend donc pas de la bonne
        // reconnaissance du symbole %.
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
            match.Value
                .Replace(',', '.');

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