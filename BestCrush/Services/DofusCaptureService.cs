using System.Runtime.InteropServices;

using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace BestCrush.Services;

public sealed class DofusCaptureService(
    BestCrushSettingsService bestCrushSettingsService)
{
    public async Task<DofusCaptureResult> CaptureAsync(
        DofusWindowInfo window,
        CancellationToken cancellationToken = default)
    {
        if (window.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Handle Dofus invalide."
            );
        }

        if (IsIconic(window.Handle))
        {
            throw new InvalidOperationException(
                "Dofus est minimisé."
            );
        }

        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException(
                "Windows Graphics Capture n'est pas pris en charge sur ce système."
            );
        }

        GraphicsCaptureItem captureItem =
            WindowsGraphicsCaptureHelper
                .CreateItemForWindow(
                    window.Handle
                );

        using IDirect3DDevice device =
            WindowsGraphicsCaptureHelper
                .CreateDirect3DDevice();

        using Direct3D11CaptureFramePool framePool =
            Direct3D11CaptureFramePool
                .CreateFreeThreaded(
                    device,
                    DirectXPixelFormat
                        .B8G8R8A8UIntNormalized,
                    1,
                    captureItem.Size
                );

        using GraphicsCaptureSession session =
            framePool.CreateCaptureSession(
                captureItem
            );

        TaskCompletionSource<Direct3D11CaptureFrame>
            frameReceived =
                new(
                    TaskCreationOptions
                        .RunContinuationsAsynchronously
                );

        void FrameArrived(
            Direct3D11CaptureFramePool sender,
            object args)
        {
            Direct3D11CaptureFrame? frame =
                sender.TryGetNextFrame();

            if (frame is null)
            {
                return;
            }

            if (!frameReceived.TrySetResult(frame))
            {
                frame.Dispose();
            }
        }

        framePool.FrameArrived +=
            FrameArrived;

        string? captureDirectory =
            null;

        bool captureCompleted =
            false;

        try
        {
            session.StartCapture();

            using Direct3D11CaptureFrame frame =
                await frameReceived.Task.WaitAsync(
                    TimeSpan.FromSeconds(3),
                    cancellationToken
                );

            using SoftwareBitmap softwareBitmap =
                await SoftwareBitmap
                    .CreateCopyFromSurfaceAsync(
                        frame.Surface,
                        BitmapAlphaMode.Premultiplied
                    );

            using SoftwareBitmap convertedBitmap =
                SoftwareBitmap.Convert(
                    softwareBitmap,
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied
                );

                string capturesDirectory =
                    GetCapturesDirectory();

                Directory.CreateDirectory(
                    capturesDirectory
                );

                string captureId =
                    $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-" +
                    $"{Guid.NewGuid():N}";

                string directory =
                    Path.Combine(
                        capturesDirectory,
                        captureId
                    );

                captureDirectory =
                    directory;

                Directory.CreateDirectory(
                    directory
                );

                string fileName =
                    "dofus.png";

            StorageFolder folder =
                await StorageFolder
                    .GetFolderFromPathAsync(
                        directory
                    );

            StorageFile file =
                await folder.CreateFileAsync(
                    fileName,
                    CreationCollisionOption
                        .ReplaceExisting
                );

            using IRandomAccessStream stream =
                await file.OpenAsync(
                    FileAccessMode.ReadWrite
                );

            BitmapEncoder encoder =
                await BitmapEncoder.CreateAsync(
                    BitmapEncoder.PngEncoderId,
                    stream
                );

            encoder.SetSoftwareBitmap(
                convertedBitmap
            );

            await encoder.FlushAsync();

            captureCompleted =
                true;

            return new DofusCaptureResult(
                file.Path,
                frame.ContentSize.Width,
                frame.ContentSize.Height,
                DateTime.UtcNow
            );
        }
        finally
        {
            framePool.FrameArrived -=
                FrameArrived;

            if (!captureCompleted)
            {
                TryDeleteCaptureDirectory(
                    captureDirectory
                );
            }
        }
    }

    public void DeleteCaptureArtifacts(
        string captureFilePath)
    {
        if (string.IsNullOrWhiteSpace(
            captureFilePath))
        {
            return;
        }

        string? captureDirectory =
            Path.GetDirectoryName(
                captureFilePath
            );

        TryDeleteCaptureDirectory(
            captureDirectory
        );
    }

    private static string
        GetCapturesDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment
                    .SpecialFolder
                    .LocalApplicationData
            ),
            "BestCrush",
            "DebugCaptures"
        );
    }

    private void
        TryDeleteCaptureDirectory(
            string? captureDirectory)
    {
        if (!bestCrushSettingsService
            .DevTool_RemoveScreenshotsByDefault)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
            captureDirectory))
        {
            return;
        }

        try
        {
            string fullCaptureDirectory =
                Path.GetFullPath(
                    captureDirectory
                );

            string fullCapturesDirectory =
                Path.GetFullPath(
                    GetCapturesDirectory()
                );

            DirectoryInfo? parentDirectory =
                Directory.GetParent(
                    fullCaptureDirectory
                );

            if (parentDirectory is null)
            {
                return;
            }

            string fullParentDirectory =
                Path.GetFullPath(
                    parentDirectory.FullName
                );

            // Sécurité : BestCrush ne supprime que
            // les dossiers de capture directement
            // créés sous DebugCaptures.
            if (!string.Equals(
                fullParentDirectory,
                fullCapturesDirectory,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return;
            }

            if (Directory.Exists(
                fullCaptureDirectory))
            {
                Directory.Delete(
                    fullCaptureDirectory,
                    recursive: true
                );
            }
        }
        catch
        {
            // Le nettoyage d'un fichier temporaire
            // ne doit jamais interrompre BestCrush.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(
        UnmanagedType.Bool)]
    private static extern bool IsIconic(
        IntPtr hwnd
    );
}

public sealed record DofusCaptureResult(
    string FilePath,
    int Width,
    int Height,
    DateTime CapturedAtUtc
);