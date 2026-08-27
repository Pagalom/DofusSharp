using System.Runtime.InteropServices;

using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

using WinRT;

namespace BestCrush.Services;

internal static class WindowsGraphicsCaptureHelper
{
    private static readonly Guid GraphicsCaptureItemGuid =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem CreateItemForWindow(
        IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException(
                "Handle de fenêtre invalide.",
                nameof(hwnd)
            );
        }

        IGraphicsCaptureItemInterop interop =
            GraphicsCaptureItem
                .As<IGraphicsCaptureItemInterop>();

        IntPtr itemPointer =
            interop.CreateForWindow(
                hwnd,
                GraphicsCaptureItemGuid
            );

        if (itemPointer == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Impossible de créer la cible Windows Graphics Capture."
            );
        }

        try
        {
            return WinRT.MarshalInterface<GraphicsCaptureItem>
                .FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    public static IDirect3DDevice CreateDirect3DDevice()
    {
        using ID3D11Device nativeDevice =
            D3D11.D3D11CreateDevice(
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport
            );

        using IDXGIDevice dxgiDevice =
            nativeDevice.QueryInterface<IDXGIDevice>();

        uint result =
            CreateDirect3D11DeviceFromDXGIDevice(
                dxgiDevice.NativePointer,
                out IntPtr graphicsDevicePointer
            );

        if (result != 0 ||
            graphicsDevicePointer == IntPtr.Zero)
        {
            Marshal.ThrowExceptionForHR(
                unchecked((int)result)
            );
        }

        try
        {
            return WinRT.MarshalInterface<IDirect3DDevice>
                .FromAbi(graphicsDevicePointer);
        }
        finally
        {
            Marshal.Release(
                graphicsDevicePointer
            );
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(
        ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(
            [In] IntPtr window,
            in Guid iid
        );

        IntPtr CreateForMonitor(
            [In] IntPtr monitor,
            in Guid iid
        );
    }

    [DllImport(
        "d3d11.dll",
        EntryPoint =
            "CreateDirect3D11DeviceFromDXGIDevice",
        ExactSpelling = true
    )]
    private static extern uint
        CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice,
            out IntPtr graphicsDevice
        );
}