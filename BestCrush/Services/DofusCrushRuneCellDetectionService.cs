using System.Numerics;

using OpenCvSharp;

using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace BestCrush.Services;

public sealed record
    DofusCrushRuneCellDetectionResult(
        ulong RowFingerprint,
        int RowHeight,
        int RowOccurrence,
        int ColumnIndex,
        int RuneLineIndex,
        int CellX,
        int CellY,
        string DebugCellImagePath
    );

public sealed class
    DofusCrushRuneCellDetectionService(
        DofusCrushRowDetectionService
            rowDetectionService)
{
    private const double
        ReferencePanelWidth = 720.0;

    private const double
        ReferenceRuneGridX = 439.0;

    private const double
        ReferenceCellPitchX = 44.0;

    private const double
        ReferenceCellWidth = 36.0;

    private const double
        ReferenceCellHeight = 36.0;

    private const double
        ReferenceCellPitchY = 43.0;

    private const double
        ReferenceGridTopOffset = 12.0;

    private const int
        RowFingerprintMaximumDistance = 8;

    public async Task<
        DofusCrushRuneCellDetectionResult?>
        DetectAsync(
            string panelFilePath,
            int cursorX,
            int cursorY,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        using Mat source =
            Cv2.ImRead(
                panelFilePath,
                ImreadModes.Color
            );

        if (source.Empty())
        {
            return null;
        }

        IReadOnlyList<
            CrushRowDetectionResult>
            rows =
                await rowDetectionService
                    .DetectRowsAsync(
                        panelFilePath,
                        cancellationToken
                    );

        if (rows.Count == 0)
        {
            return null;
        }

        int hoveredRowIndex = -1;

        for (
            int index = 0;
            index < rows.Count;
            index++)
        {
            CrushRowDetectionResult row =
                rows[index];

            if (cursorY >=
                    row.Y - 4 &&
                cursorY <
                    row.Y +
                    row.Height +
                    4)
            {
                hoveredRowIndex =
                    index;

                break;
            }
        }

        if (hoveredRowIndex < 0)
        {
            return null;
        }

        CrushRowDetectionResult
            hoveredRow =
                rows[
                    hoveredRowIndex
                ];

        double scale =
            source.Width /
            ReferencePanelWidth;

        int gridStartX =
            (int)Math.Round(
                ReferenceRuneGridX *
                scale
            );

        int cellPitchX =
            Math.Max(
                1,
                (int)Math.Round(
                    ReferenceCellPitchX *
                    scale
                )
            );

        int cellWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    ReferenceCellWidth *
                    scale
                )
            );

        int cellHeight =
            Math.Max(
                1,
                (int)Math.Round(
                    ReferenceCellHeight *
                    scale
                )
            );

        int cellPitchY =
            Math.Max(
                1,
                (int)Math.Round(
                    ReferenceCellPitchY *
                    scale
                )
            );

        int gridStartY =
            hoveredRow.Y +
            (int)Math.Round(
                ReferenceGridTopOffset *
                scale
            );

        if (cursorX <
                gridStartX - 4 ||
            cursorY <
                gridStartY - 4)
        {
            return null;
        }

        int columnIndex =
            (int)Math.Floor(
                (
                    cursorX -
                    gridStartX
                ) /
                (double)cellPitchX
            );

        if (columnIndex < 0)
        {
            return null;
        }

        int cellX =
            gridStartX +
            columnIndex *
            cellPitchX;

        if (cursorX <
                cellX - 4 ||
            cursorX >
                cellX +
                cellWidth +
                4)
        {
            return null;
        }

        int runeLineIndex =
            (int)Math.Floor(
                (
                    cursorY -
                    gridStartY
                ) /
                (double)cellPitchY
            );

        if (runeLineIndex < 0)
        {
            return null;
        }

        int cellY =
            gridStartY +
            runeLineIndex *
            cellPitchY;

        if (cursorY <
                cellY - 4 ||
            cursorY >
                cellY +
                cellHeight +
                4)
        {
            return null;
        }

        if (cellX < 0 ||
            cellY < 0 ||
            cellX + cellWidth >
                source.Width ||
            cellY + cellHeight >
                source.Height)
        {
            return null;
        }

        if (cellY >
            hoveredRow.Y +
            hoveredRow.Height +
            4)
        {
            return null;
        }

        CvRect cellDebugRect =
            ClampRectangle(
                cellX - 8,
                cellY - 8,
                cellWidth + 16,
                cellHeight + 16,
                source.Width,
                source.Height
            );

        if (cellDebugRect.Width <= 0 ||
            cellDebugRect.Height <= 0)
        {
            return null;
        }

        string directory =
            Path.GetDirectoryName(
                panelFilePath
            )
            ?? Path.GetTempPath();

        string baseName =
            Path.GetFileNameWithoutExtension(
                panelFilePath
            );

        string cellDebugPath =
            Path.Combine(
                directory,
                $"{baseName}-f9-hovered-rune-cell.png"
            );

        using (
            Mat cellImage =
                new(
                    source,
                    cellDebugRect
                ))
        {
            Cv2.ImWrite(
                cellDebugPath,
                cellImage
            );
        }

        List<ulong> fingerprints = [];

        foreach (
            CrushRowDetectionResult row
            in rows)
        {
            fingerprints.Add(
                ComputeRowFingerprint(
                    source,
                    row
                )
            );
        }

        ulong hoveredFingerprint =
            fingerprints[
                hoveredRowIndex
            ];

        int occurrence = 0;

        for (
            int index = 0;
            index < hoveredRowIndex;
            index++)
        {
            if (FingerprintDistance(
                    fingerprints[index],
                    hoveredFingerprint
                ) <=
                RowFingerprintMaximumDistance)
            {
                occurrence++;
            }
        }

        return
            new DofusCrushRuneCellDetectionResult(
                hoveredFingerprint,
                hoveredRow.Height,
                occurrence,
                columnIndex,
                runeLineIndex,
                cellX,
                cellY,
                cellDebugPath
            );
    }

    private static ulong
        ComputeRowFingerprint(
            Mat panel,
            CrushRowDetectionResult row)
    {
        int fingerprintWidth =
            Math.Min(
                row.Width,
                (int)Math.Round(
                    panel.Width *
                    0.56
                )
            );

        CvRect identityRect =
            ClampRectangle(
                row.X,
                row.Y,
                fingerprintWidth,
                row.Height,
                panel.Width,
                panel.Height
            );

        if (identityRect.Width <= 0 ||
            identityRect.Height <= 0)
        {
            return 0;
        }

        using Mat identity =
            new(
                panel,
                identityRect
            );

        using Mat gray = new();

        Cv2.CvtColor(
            identity,
            gray,
            ColorConversionCodes.BGR2GRAY
        );

        using Mat resized = new();

        Cv2.Resize(
            gray,
            resized,
            new CvSize(
                9,
                8
            ),
            0,
            0,
            InterpolationFlags.Area
        );

        ulong hash = 0;

        for (
            int y = 0;
            y < 8;
            y++)
        {
            for (
                int x = 0;
                x < 8;
                x++)
            {
                hash <<= 1;

                byte left =
                    resized.At<byte>(
                        y,
                        x
                    );

                byte right =
                    resized.At<byte>(
                        y,
                        x + 1
                    );

                if (left > right)
                {
                    hash |= 1;
                }
            }
        }

        return hash;
    }

    private static int
        FingerprintDistance(
            ulong first,
            ulong second)
    {
        return BitOperations.PopCount(
            first ^
            second
        );
    }

    private static CvRect
        ClampRectangle(
            int x,
            int y,
            int width,
            int height,
            int imageWidth,
            int imageHeight)
    {
        int left =
            Math.Max(
                0,
                x
            );

        int top =
            Math.Max(
                0,
                y
            );

        int right =
            Math.Min(
                imageWidth,
                x + width
            );

        int bottom =
            Math.Min(
                imageHeight,
                y + height
            );

        return new CvRect(
            left,
            top,
            Math.Max(
                0,
                right - left
            ),
            Math.Max(
                0,
                bottom - top
            )
        );
    }
}
