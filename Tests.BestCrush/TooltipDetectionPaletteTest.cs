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
    public void BlendedModernHeaderIsRecoveredFromBodyAtActualTitlePosition()
    {
        using Mat image = CreateImage();

        // The title palette merges horizontally with the HDV interface;
        // the contour is too wide for the classic header detector.
        Cv2.Rectangle(
            image,
            new Rect(20, 50, 900, 115),
            new Scalar(62, 30, 25),
            thickness: -1);

        // The body has its own distinct contiguous palette region.
        Cv2.Rectangle(
            image,
            new Rect(55, 170, 390, 340),
            new Scalar(52, 34, 30),
            thickness: -1);

        DofusItemTooltipDetectionService.DetectTooltipHeaders(image)
            .Should().ContainSingle()
            .Which.Should().Be(new Rect(55, 50, 390, 115));
    }

    [Fact]
    public void DarkBodyWithoutTitleBandIsNotMistakenForATooltip()
    {
        using Mat image = CreateImage();

        Cv2.Rectangle(
            image,
            new Rect(55, 170, 390, 340),
            new Scalar(52, 34, 30),
            thickness: -1);

        DofusItemTooltipDetectionService.DetectTooltipHeaders(image)
            .Should().BeEmpty();
    }

    [Fact]
    public void BlendedHeaderAlsoRecognizesMediumLengthTooltip()
    {
        using Mat image = CreateImage();

        Cv2.Rectangle(
            image,
            new Rect(20, 50, 900, 115),
            new Scalar(62, 30, 25),
            thickness: -1);

        Cv2.Rectangle(
            image,
            new Rect(55, 170, 390, 150),
            new Scalar(52, 34, 30),
            thickness: -1);

        DofusItemTooltipDetectionService.DetectTooltipHeaders(image)
            .Should().ContainSingle()
            .Which.Should().Be(new Rect(55, 50, 390, 115));
    }

    [Fact]
    public void MarketBehindTooltipWithMixedPalettesDetectsTheActualTitleHeader()
    {
        using Mat image = new(
            new OpenCvSharp.Size(1920, 1080),
            MatType.CV_8UC3,
            new Scalar(130, 130, 130));

        // Approximate the marketplace window to the left of the tooltip.
        // The HDV has regions of the modern body palette, which must not
        // steal the header/body association from the tooltip.
        Cv2.Rectangle(
            image,
            new Rect(485, 50, 575, 750),
            new Scalar(52, 34, 30),
            thickness: -1);

        // Regression geometry based on the user-reported HDV screenshot:
        // modern header (1079,26,387,112), legacy body immediately after
        // it (1079,139,387,116), while distant lower sections can use
        // the modern palette.
        Cv2.Rectangle(
            image,
            new Rect(1079, 26, 387, 112),
            new Scalar(62, 30, 25),
            thickness: -1);

        Cv2.Rectangle(
            image,
            new Rect(1079, 139, 387, 116),
            new Scalar(50, 29, 27),
            thickness: -1);

        Cv2.Rectangle(
            image,
            new Rect(1079, 346, 387, 102),
            new Scalar(52, 34, 30),
            thickness: -1);

        DofusItemTooltipDetectionService.DetectTooltipHeaders(image)
            .Should().ContainSingle()
            .Which.Should().Be(new Rect(1079, 26, 387, 112));
    }

    [Fact]
    public void MixedPaletteBodyMustStillBeAdjacentAndAligned()
    {
        using Mat image = CreateImage();

        Cv2.Rectangle(
            image,
            new Rect(50, 50, 400, 120),
            new Scalar(62, 30, 25),
            thickness: -1);

        // The distant lower panel is legacy-colored but must not be
        // treated as the adjacent body of the title.
        Cv2.Rectangle(
            image,
            new Rect(50, 230, 400, 125),
            new Scalar(50, 29, 27),
            thickness: -1);

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
