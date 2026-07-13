using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace DDRGpScoreViewer.Capture;

public sealed class ContinuousWindowsGraphicsCaptureAdapter : IContinuousGraphicsCaptureAdapter
{
    private const uint D3D11SdkVersion = 7;
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const int D3DDriverTypeHardware = 1;
    private static readonly Guid IdxgiDeviceGuid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    public bool IsSupported => GraphicsCaptureSession.IsSupported();

    public async Task<IContinuousFrameSource?> StartSessionAsync(
        nint ownerWindowHandle,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new GraphicsCapturePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindowHandle);
        var item = await picker.PickSingleItemAsync().AsTask(cancellationToken);
        if (item is null)
        {
            return null;
        }
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            throw new CaptureInvalidSizeException("Capture item has a zero-sized surface.");
        }

        return CaptureFrameSource.Start(item);
    }

    private sealed class CaptureFrameSource : IContinuousFrameSource
    {
        private readonly GraphicsCaptureItem item;
        private readonly IDirect3DDevice device;
        private readonly Direct3D11CaptureFramePool framePool;
        private readonly GraphicsCaptureSession session;
        private readonly Channel<QueuedFrame> frames;
        private readonly TaskCompletionSource<CaptureSessionEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int width;
        private readonly int height;
        private readonly string captureSource;
        private int terminalSet;
        private int disposed;
        private long lastTimestamp = -1;

        private CaptureFrameSource(
            GraphicsCaptureItem item,
            IDirect3DDevice device,
            Direct3D11CaptureFramePool framePool,
            GraphicsCaptureSession session)
        {
            this.item = item;
            this.device = device;
            this.framePool = framePool;
            this.session = session;
            width = item.Size.Width;
            height = item.Size.Height;
            captureSource = string.IsNullOrWhiteSpace(item.DisplayName)
                ? "selected_window"
                : item.DisplayName;
            frames = Channel.CreateBounded<QueuedFrame>(
                new BoundedChannelOptions(2)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });
        }

        public Task<CaptureSessionEndReason> Completion => completion.Task;

        public static CaptureFrameSource Start(GraphicsCaptureItem item)
        {
            IDirect3DDevice? device = null;
            Direct3D11CaptureFramePool? framePool = null;
            GraphicsCaptureSession? session = null;
            CaptureFrameSource? source = null;
            try
            {
                device = CreateDirect3DDevice();
                framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    item.Size);
                session = framePool.CreateCaptureSession(item);
                source = new CaptureFrameSource(item, device, framePool, session);
                source.framePool.FrameArrived += source.FrameArrived;
                source.item.Closed += source.ItemClosed;
                source.session.StartCapture();
                return source;
            }
            catch (COMException exception) when (IsDeviceLost(exception.HResult))
            {
                DisposeFailedStart(source, session, framePool, device);
                throw new CaptureDeviceLostException("Direct3D device was lost.", exception);
            }
            catch
            {
                DisposeFailedStart(source, session, framePool, device);
                throw;
            }
        }

        public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (await frames.Reader.WaitToReadAsync(cancellationToken))
            {
                while (frames.Reader.TryRead(out var queuedFrame))
                {
                    var frame = queuedFrame.Frame;
                    try
                    {
                        byte[] pngBytes;
                        try
                        {
                            pngBytes = await EncodePngAsync(frame.Surface, cancellationToken);
                        }
                        catch (COMException exception) when (IsDeviceLost(exception.HResult))
                        {
                            Signal(CaptureSessionEndReason.DeviceLost);
                            throw new CaptureDeviceLostException("Direct3D device was lost.", exception);
                        }

                        yield return new CapturedFrame(
                            pngBytes,
                            frame.ContentSize.Width,
                            frame.ContentSize.Height,
                            queuedFrame.TimestampMs,
                            queuedFrame.CapturedAtUtc,
                            captureSource);
                    }
                    finally
                    {
                        DisposeSafely(frame);
                    }
                }
            }
        }

        public Task StopAsync()
        {
            Signal(CaptureSessionEndReason.Stopped);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            Signal(CaptureSessionEndReason.Stopped);
            RunSafely(() => framePool.FrameArrived -= FrameArrived);
            RunSafely(() => item.Closed -= ItemClosed);
            while (frames.Reader.TryRead(out var queuedFrame))
            {
                DisposeSafely(queuedFrame.Frame);
            }
            DisposeSafely(session);
            DisposeSafely(framePool);
            DisposeSafely(device);
            return ValueTask.CompletedTask;
        }

        private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            Direct3D11CaptureFrame? frame = null;
            try
            {
                frame = sender.TryGetNextFrame();
                if (Volatile.Read(ref terminalSet) != 0)
                {
                    DisposeSafely(frame);
                    return;
                }
                if (frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0 ||
                    frame.ContentSize.Width != width || frame.ContentSize.Height != height)
                {
                    DisposeSafely(frame);
                    Signal(CaptureSessionEndReason.Resized);
                    return;
                }
                var queuedFrame = new QueuedFrame(
                    frame,
                    NextTimestamp(),
                    DateTimeOffset.UtcNow);
                if (!frames.Writer.TryWrite(queuedFrame))
                {
                    DisposeSafely(frame);
                }
            }
            catch (COMException exception) when (IsDeviceLost(exception.HResult))
            {
                DisposeSafely(frame);
                Signal(CaptureSessionEndReason.DeviceLost);
            }
            catch
            {
                DisposeSafely(frame);
                Signal(CaptureSessionEndReason.Failed);
            }
        }

        private void ItemClosed(GraphicsCaptureItem sender, object args) =>
            Signal(CaptureSessionEndReason.TargetClosed);

        private void Signal(CaptureSessionEndReason reason)
        {
            if (Interlocked.CompareExchange(ref terminalSet, 1, 0) != 0)
            {
                return;
            }
            frames.Writer.TryComplete();
            completion.TrySetResult(reason);
        }

        private long NextTimestamp()
        {
            while (true)
            {
                var previous = Volatile.Read(ref lastTimestamp);
                var candidate = Math.Max(Environment.TickCount64, previous + 1);
                if (Interlocked.CompareExchange(ref lastTimestamp, candidate, previous) == previous)
                {
                    return candidate;
                }
            }
        }

        private sealed record QueuedFrame(
            Direct3D11CaptureFrame Frame,
            long TimestampMs,
            DateTimeOffset CapturedAtUtc);
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

    private static void DisposeSafely(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch
        {
            // Continue releasing the remaining WinRT/D3D resources.
        }
    }

    private static void DisposeFailedStart(
        CaptureFrameSource? source,
        GraphicsCaptureSession? session,
        Direct3D11CaptureFramePool? framePool,
        IDirect3DDevice? device)
    {
        if (source is not null)
        {
            source.DisposeAsync().GetAwaiter().GetResult();
            return;
        }
        DisposeSafely(session);
        DisposeSafely(framePool);
        DisposeSafely(device);
    }

    private static void RunSafely(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Event removal can race with target or device shutdown.
        }
    }

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
