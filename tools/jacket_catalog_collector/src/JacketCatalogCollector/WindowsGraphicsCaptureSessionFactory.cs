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

namespace JacketCatalogCollector;

public sealed class WindowsGraphicsCaptureSessionFactory : IWindowCaptureSessionFactory
{
    private const uint D3D11SdkVersion = 7;
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const int D3DDriverTypeHardware = 1;
    private static readonly Guid IdxgiDeviceGuid = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public bool IsSupported => GraphicsCaptureSession.IsSupported();

    public Task<IWindowCaptureFrameSource> StartAsync(
        WindowIdentitySnapshot target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = CreateItemForWindow(target.Handle);
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            throw new InvalidOperationException("Capture item has a zero-sized surface.");
        }
        return Task.FromResult<IWindowCaptureFrameSource>(CaptureFrameSource.Start(item));
    }

    private static GraphicsCaptureItem CreateItemForWindow(nint handle)
    {
        using var factory = ActivationFactory.Get(
            "Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
        nint pointer = 0;
        try
        {
            Marshal.ThrowExceptionForHR(interop.CreateForWindow(
                handle, in GraphicsCaptureItemGuid, out pointer));
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer);
        }
        finally
        {
            if (pointer != 0)
            {
                Marshal.Release(pointer);
            }
            if (Marshal.IsComObject(interop))
            {
                Marshal.FinalReleaseComObject(interop);
            }
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(nint window, in Guid iid, out nint result);
    }

    private sealed class CaptureFrameSource : IWindowCaptureFrameSource
    {
        private readonly GraphicsCaptureItem item;
        private readonly IDirect3DDevice device;
        private readonly Direct3D11CaptureFramePool framePool;
        private readonly GraphicsCaptureSession session;
        private readonly Channel<Direct3D11CaptureFrame> frames;
        private readonly TaskCompletionSource<CaptureEndReason> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource disposalCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly InFlightCallbackDrain callbackDrain = new();
        private readonly int width;
        private readonly int height;
        private int terminalSet;
        private int disposed;
        private long sequence;
        private long droppedCount;

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
            frames = Channel.CreateBounded<Direct3D11CaptureFrame>(
                new BoundedChannelOptions(2)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });
        }

        public Task<CaptureEndReason> Completion => completion.Task;
        public long DroppedCount => Interlocked.Read(ref droppedCount);

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
            catch
            {
                if (source is not null)
                {
                    source.DisposeAsync().GetAwaiter().GetResult();
                }
                else
                {
                    DisposeSafely(session);
                    DisposeSafely(framePool);
                    DisposeSafely(device);
                }
                throw;
            }
        }

        public async IAsyncEnumerable<RawCaptureFrame> ReadFramesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (await frames.Reader.WaitToReadAsync(cancellationToken))
            {
                while (frames.Reader.TryRead(out var frame))
                {
                    try
                    {
                        byte[] pngBytes;
                        try
                        {
                            pngBytes = await EncodePngAsync(frame.Surface, cancellationToken);
                        }
                        catch (COMException exception) when (IsDeviceLost(exception.HResult))
                        {
                            Signal(CaptureEndReason.DeviceLost);
                            throw;
                        }
                        yield return new RawCaptureFrame(
                            pngBytes,
                            frame.ContentSize.Width,
                            frame.ContentSize.Height,
                            Interlocked.Increment(ref sequence),
                            DateTimeOffset.UtcNow);
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
            Signal(CaptureEndReason.ExplicitStop);
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                await disposalCompletion.Task;
                return;
            }
            try
            {
                Signal(CaptureEndReason.ExplicitStop);
                RunSafely(() => framePool.FrameArrived -= FrameArrived);
                RunSafely(() => item.Closed -= ItemClosed);
                await callbackDrain.CloseAndWaitAsync();
                while (frames.Reader.TryRead(out var frame))
                {
                    DisposeSafely(frame);
                }
                DisposeSafely(session);
                DisposeSafely(framePool);
                DisposeSafely(device);
            }
            finally
            {
                disposalCompletion.TrySetResult();
            }
        }

        private void FrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            if (!callbackDrain.TryEnter())
            {
                return;
            }
            Direct3D11CaptureFrame? frame = null;
            try
            {
                frame = sender.TryGetNextFrame();
                if (Volatile.Read(ref terminalSet) != 0)
                {
                    DisposeSafely(frame);
                    return;
                }
                if (frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0
                    || frame.ContentSize.Width != width || frame.ContentSize.Height != height)
                {
                    DisposeSafely(frame);
                    Signal(CaptureEndReason.Resized);
                    return;
                }
                if (!frames.Writer.TryWrite(frame))
                {
                    Interlocked.Increment(ref droppedCount);
                    DisposeSafely(frame);
                }
            }
            catch (COMException exception) when (IsDeviceLost(exception.HResult))
            {
                DisposeSafely(frame);
                Signal(CaptureEndReason.DeviceLost);
            }
            catch
            {
                DisposeSafely(frame);
                Signal(CaptureEndReason.CaptureFailed);
            }
            finally
            {
                callbackDrain.Exit();
            }
        }

        private void ItemClosed(GraphicsCaptureItem sender, object args) =>
            Signal(CaptureEndReason.TargetClosed);

        private void Signal(CaptureEndReason reason)
        {
            if (Interlocked.CompareExchange(ref terminalSet, 1, 0) != 0)
            {
                return;
            }
            frames.Writer.TryComplete();
            completion.TrySetResult(reason);
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
                0, D3DDriverTypeHardware, 0, D3D11CreateDeviceBgraSupport,
                0, 0, D3D11SdkVersion,
                out d3dDevice, out _, out immediateContext));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(
                d3dDevice, in IdxgiDeviceGuid, out dxgiDevice));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(
                dxgiDevice, out inspectable));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            if (inspectable != 0) Marshal.Release(inspectable);
            if (dxgiDevice != 0) Marshal.Release(dxgiDevice);
            if (immediateContext != 0) Marshal.Release(immediateContext);
            if (d3dDevice != 0) Marshal.Release(d3dDevice);
        }
    }

    private static bool IsDeviceLost(int hresult) =>
        hresult is unchecked((int)0x887A0005)
            or unchecked((int)0x887A0006)
            or unchecked((int)0x887A0007);

    private static void DisposeSafely(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch
        {
            // Continue releasing remaining WinRT/D3D resources.
        }
    }

    private static void RunSafely(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Event removal can race target or device shutdown.
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
