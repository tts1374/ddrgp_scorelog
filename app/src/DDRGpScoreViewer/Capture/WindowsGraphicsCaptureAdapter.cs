using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace DDRGpScoreViewer.Capture;

public sealed class WindowsGraphicsCaptureAdapter : IGraphicsCaptureAdapter
{
    private const uint D3D11SdkVersion = 7;
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const int D3DDriverTypeHardware = 1;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(10);
    private static readonly Guid IdxgiDeviceGuid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    public bool IsSupported => GraphicsCaptureSession.IsSupported();

    public async Task<CapturedFrame?> CaptureSingleFrameAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new GraphicsCapturePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindowHandle);
        var item = await picker.PickSingleItemAsync();
        if (item is null)
        {
            return null;
        }
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            throw new CaptureInvalidSizeException("Capture item has a zero-sized surface.");
        }

        return await CaptureItemAsync(item, cancellationToken);
    }

    private static async Task<CapturedFrame> CaptureItemAsync(
        GraphicsCaptureItem item,
        CancellationToken cancellationToken)
    {
        using var device = CreateDirect3DDevice();
        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        using var session = framePool.CreateCaptureSession(item);
        Direct3D11CaptureFrame? ownedFrame = null;
        var completion = new TaskCompletionSource<Direct3D11CaptureFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame = null;
            try
            {
                frame = sender.TryGetNextFrame();
                if (!completion.TrySetResult(frame))
                {
                    frame.Dispose();
                }
            }
            catch (Exception exception)
            {
                frame?.Dispose();
                completion.TrySetException(exception);
            }
        }

        void ItemClosed(GraphicsCaptureItem sender, object args) =>
            completion.TrySetException(
                new CaptureTargetClosedException("Capture target closed before a frame arrived."));

        framePool.FrameArrived += FrameArrived;
        item.Closed += ItemClosed;
        try
        {
            session.StartCapture();
            using var timeout = new CancellationTokenSource(FrameTimeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            try
            {
                ownedFrame = await completion.Task.WaitAsync(linkedCancellation.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new CaptureTargetClosedException("Timed out waiting for the selected window frame.");
            }

            if (ownedFrame.ContentSize.Width <= 0 || ownedFrame.ContentSize.Height <= 0)
            {
                throw new CaptureInvalidSizeException("Captured frame has a zero-sized surface.");
            }
            if (ownedFrame.ContentSize.Width != item.Size.Width ||
                ownedFrame.ContentSize.Height != item.Size.Height)
            {
                throw new CaptureResizedException("Capture item resized before the first frame completed.");
            }
            var pngBytes = await EncodePngAsync(ownedFrame.Surface, cancellationToken);
            return new CapturedFrame(
                pngBytes,
                ownedFrame.ContentSize.Width,
                ownedFrame.ContentSize.Height,
                Environment.TickCount64,
                DateTimeOffset.UtcNow,
                string.IsNullOrWhiteSpace(item.DisplayName) ? "selected_window" : item.DisplayName);
        }
        catch (COMException exception) when (IsDeviceLost(exception.HResult))
        {
            throw new CaptureDeviceLostException("Direct3D device was lost.", exception);
        }
        finally
        {
            framePool.FrameArrived -= FrameArrived;
            item.Closed -= ItemClosed;
            if (ownedFrame is not null)
            {
                ownedFrame.Dispose();
            }
            else
            {
                _ = completion.Task.ContinueWith(
                    completed => completed.Result.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously |
                        TaskContinuationOptions.OnlyOnRanToCompletion,
                    TaskScheduler.Default);
            }
        }
    }

    private static async Task<byte[]> EncodePngAsync(
        IDirect3DSurface surface,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
            surface,
            BitmapAlphaMode.Ignore);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        if (stream.Size > int.MaxValue)
        {
            throw new IOException("Captured PNG exceeds the supported in-memory size.");
        }
        var bytes = new byte[(int)stream.Size];
        using var input = stream.GetInputStreamAt(0);
        using var reader = new DataReader(input);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        nint d3dDevice = 0;
        nint immediateContext = 0;
        nint dxgiDevice = 0;
        nint inspectable = 0;
        try
        {
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(
                0,
                D3DDriverTypeHardware,
                0,
                D3D11CreateDeviceBgraSupport,
                0,
                0,
                D3D11SdkVersion,
                out d3dDevice,
                out _,
                out immediateContext));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(
                d3dDevice,
                in IdxgiDeviceGuid,
                out dxgiDevice));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(
                dxgiDevice,
                out inspectable));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0)
            {
                Marshal.Release(inspectable);
            }
            if (dxgiDevice != 0)
            {
                Marshal.Release(dxgiDevice);
            }
            if (immediateContext != 0)
            {
                Marshal.Release(immediateContext);
            }
            if (d3dDevice != 0)
            {
                Marshal.Release(d3dDevice);
            }
        }
    }

    private static bool IsDeviceLost(int hresult) =>
        hresult is unchecked((int)0x887A0005) or
            unchecked((int)0x887A0006) or
            unchecked((int)0x887A0007);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(
        nint adapter,
        int driverType,
        nint software,
        uint flags,
        nint featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out nint device,
        out int featureLevel,
        out nint immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);
}
