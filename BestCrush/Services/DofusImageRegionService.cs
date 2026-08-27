using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BestCrush.Services;

public sealed class DofusImageRegionService
{
    public async Task<string> ExtractRegionAsync(
        string sourceFilePath,
        RelativeImageRegion region,
        string suffix)
    {
        StorageFile sourceFile =
            await StorageFile.GetFileFromPathAsync(
                sourceFilePath
            );

        using IRandomAccessStream sourceStream =
            await sourceFile.OpenAsync(
                FileAccessMode.Read
            );

        BitmapDecoder decoder =
            await BitmapDecoder.CreateAsync(
                sourceStream
            );

        uint x =
            (uint)Math.Round(
                decoder.PixelWidth * region.X
            );

        uint y =
            (uint)Math.Round(
                decoder.PixelHeight * region.Y
            );

        uint width =
            (uint)Math.Round(
                decoder.PixelWidth * region.Width
            );

        uint height =
            (uint)Math.Round(
                decoder.PixelHeight * region.Height
            );

        if (x + width > decoder.PixelWidth)
        {
            width = decoder.PixelWidth - x;
        }

        if (y + height > decoder.PixelHeight)
        {
            height = decoder.PixelHeight - y;
        }

        BitmapTransform transform = new()
        {
            Bounds = new BitmapBounds
            {
                X = x,
                Y = y,
                Width = width,
                Height = height
            }
        };

        SoftwareBitmap bitmap =
            await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage
            );

        string directory =
            Path.GetDirectoryName(
                sourceFilePath
            )!;

        string fileName =
            $"{Path.GetFileNameWithoutExtension(sourceFilePath)}-{suffix}.png";

        string outputPath =
            Path.Combine(
                directory,
                fileName
            );

        StorageFolder folder =
            await StorageFolder.GetFolderFromPathAsync(
                directory
            );

        StorageFile outputFile =
            await folder.CreateFileAsync(
                fileName,
                CreationCollisionOption.ReplaceExisting
            );

        using IRandomAccessStream outputStream =
            await outputFile.OpenAsync(
                FileAccessMode.ReadWrite
            );

        BitmapEncoder encoder =
            await BitmapEncoder.CreateAsync(
                BitmapEncoder.PngEncoderId,
                outputStream
            );

        encoder.SetSoftwareBitmap(
            bitmap
        );

        await encoder.FlushAsync();

        bitmap.Dispose();

        return outputPath;
    }
}

public readonly record struct RelativeImageRegion(
    double X,
    double Y,
    double Width,
    double Height
);