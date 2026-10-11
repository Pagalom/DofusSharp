using BestCrush.Services;
using FluentAssertions;
using OpenCvSharp;
using Rect = OpenCvSharp.Rect;

namespace Tests.BestCrush;

public sealed class TooltipDetectionPaletteTest
{
    [Fact]
    public void CurrentDofusTooltipPaletteProducesOneHeader()
    {
        using Mat image = CreateImage();
        DrawTooltip(image, x: 50, useLegacyPalette: false);

        IReadOnlyList<Rect> headers =
            DofusItemTooltipDetectionService.DetectTooltipHeaders(image);

        headers.Should().ContainSingle()
            .Which.Should().Be(new Rect(50, 50, 400, 120));
    }

    [Fact]
    public void LegacyTooltipPaletteStillProducesOneHeader()
    {
        using Mat image = CreateImage();
        DrawTooltip(image, x: 50, useLegacyPalette: true);

        IReadOnlyList<Rect> headers =
            DofusItemTooltipDetectionService.DetectTooltipHeaders(image);

        headers.Should().ContainSingle()
            .Which.Should().Be(new Rect(50, 50, 400, 120));
    }

    [Fact]
    public void LongModernTooltipWithSeparatedBodyStillProducesOneHeader()
    {
        using Mat image = CreateImage();

        // A modern 1080p tooltip can contain several intervening dark
        // sub-sections: only its bottom panel forms a contiguous body mask.
        DrawTooltip(
            image,
            x: 50,
            useLegacyPalette: false,
            bodyY: 360,
            bodyHeight: 330);

        DofusItemTooltipDetectionService.DetectTooltipHeaders(image)
            .Should().ContainSingle()
            .Which.Should().Be(new Rect(50, 50, 400, 120));
    }

    [Fact]
    public void ShortSeparatedPanelDoesNotBecomeATooltipBody()
    {
        using Mat image = CreateImage();
        DrawTooltip(
            image,
            x: 50,
            useLegacyPalette: false,
            bodyY: 360,
            bodyHeight: 125);

        DofusItemTooltipDetectionService.DetectTooltipHeaders(image)
            .Should().BeEmpty();
    }

    [Fact]
    public void LongPanelTooFarFromHeaderDoesNotBecomeATooltipBody()
    {
        using Mat image = CreateImage();
        DrawTooltip(
            image,
            x: 50,
            useLegacyPalette: false,
            bodyY: 450,
            bodyHeight: 300);

        DofusItemTooltipDetectionService.DetectTooltipHeaders(image)
            .Should().BeEmpty();
    }

    [Fact]
    public void PlainImageDoesNotInventAnyTooltip()
    {
        using Mat image = CreateImage();

        DofusItemTooltipDetectionService.DetectTooltipHeaders(image)
            .Should().BeEmpty();
    }

    [Fact]
    public void TwoTooltipsRemainAmbiguousForF8()
    {
        using Mat image = CreateImage();
        DrawTooltip(image, x: 50, useLegacyPalette: false);
        DrawTooltip(image, x: 600, useLegacyPalette: false);

        IReadOnlyList<Rect> headers =
            DofusItemTooltipDetectionService.DetectTooltipHeaders(image);

        headers.Should().HaveCount(2);
    }

    private static Mat CreateImage() =>
        new(
            new OpenCvSharp.Size(1100, 750),
            MatType.CV_8UC3,
            new Scalar(130, 130, 130)
        );

    private static void DrawTooltip(
        Mat image,
        int x,
        bool useLegacyPalette,
        int bodyY = 171,
        int bodyHeight = 125)
    {
        // Synthetic BGR rectangles only; no user screenshot committed.
        // Slight header/body offset preserves the real tooltip geometry.
        Scalar header = useLegacyPalette
            ? new Scalar(37, 22, 20)
            : new Scalar(62, 30, 25);

        Scalar body = useLegacyPalette
            ? new Scalar(50, 29, 27)
            : new Scalar(52, 34, 30);

        Cv2.Rectangle(
            image,
            new Rect(x, 50, 400, 120),
            header,
            thickness: -1
        );

        Cv2.Rectangle(
            image,
            new Rect(x + 5, bodyY, 395, bodyHeight),
            body,
            thickness: -1
        );
    }
}
