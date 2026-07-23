using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace JacketCatalogCollector;

public sealed class NativeWindowEnumerator : IWindowEnumerator
{
    private const uint PrintWindowClientOnly = 0x00000001;
    private const string TargetProcessName = "ddr-konaste";
    private const int TargetClientWidth = 1280;
    private const int TargetClientHeight = 720;

    public Task<IReadOnlyList<WindowCandidate>> EnumerateAsync(
        CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<WindowCandidate>>(() =>
    {
        var candidates = new List<WindowCandidate>();
        EnumWindows((handle, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = TryGetSnapshot(handle);
            if (snapshot is null)
            {
                return true;
            }
            var reasons = CandidateReasons(snapshot);
            if (reasons.Count == 0)
            {
                return true;
            }
            candidates.Add(new WindowCandidate(
                snapshot,
                string.Join(", ", reasons),
                TryCapturePreview(snapshot)));
            return true;
        }, 0);
        return candidates.OrderBy(candidate => candidate.Identity.ProcessId).ThenBy(
            candidate => candidate.Identity.Handle).ToList();
    }, cancellationToken);

    public WindowIdentitySnapshot? TryGetSnapshot(nint handle)
    {
        if (handle == 0 || !IsWindow(handle))
        {
            return null;
        }
        GetWindowThreadProcessId(handle, out var processIdValue);
        if (processIdValue == 0)
        {
            return null;
        }
        var title = GetWindowTextValue(handle);
        var className = GetClassNameValue(handle);
        if (!GetClientRect(handle, out var rect))
        {
            return null;
        }
        try
        {
            using var process = Process.GetProcessById(checked((int)processIdValue));
            return new WindowIdentitySnapshot(
                handle,
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                process.ProcessName,
                title,
                className,
                Math.Max(0, rect.Right - rect.Left),
                Math.Max(0, rect.Bottom - rect.Top),
                IsWindowVisible(handle),
                IsIconic(handle));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    internal static List<string> CandidateReasons(WindowIdentitySnapshot snapshot)
    {
        return IsDdrGpTarget(snapshot)
            ? ["process name is ddr-konaste and client size is 1280x720"]
            : [];
    }

    internal static bool IsDdrGpTarget(WindowIdentitySnapshot snapshot) =>
        snapshot.ProcessName.Equals(TargetProcessName, StringComparison.OrdinalIgnoreCase)
        && snapshot.ClientWidth == TargetClientWidth
        && snapshot.ClientHeight == TargetClientHeight;

    private static byte[]? TryCapturePreview(WindowIdentitySnapshot snapshot)
    {
        if (!ShouldAttemptPreview(snapshot))
        {
            return null;
        }
        nint windowDc = 0;
        nint memoryDc = 0;
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            windowDc = GetDC(snapshot.Handle);
            if (windowDc == 0)
            {
                return null;
            }
            memoryDc = CreateCompatibleDC(windowDc);
            bitmap = CreateCompatibleBitmap(windowDc, snapshot.ClientWidth, snapshot.ClientHeight);
            if (memoryDc == 0 || bitmap == 0)
            {
                return null;
            }
            previous = SelectObject(memoryDc, bitmap);
            if (!PrintWindow(snapshot.Handle, memoryDc, PrintWindowClientOnly))
            {
                return null;
            }
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(
                    Math.Min(snapshot.ClientWidth, 640),
                    Math.Max(1, Math.Min(snapshot.ClientHeight, 360))));
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (previous != 0 && memoryDc != 0)
            {
                SelectObject(memoryDc, previous);
            }
            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }
            if (memoryDc != 0)
            {
                DeleteDC(memoryDc);
            }
            if (windowDc != 0)
            {
                ReleaseDC(snapshot.Handle, windowDc);
            }
        }
    }

    internal static bool ShouldAttemptPreview(WindowIdentitySnapshot snapshot) =>
        !snapshot.ProcessName.Equals("ddr-konaste", StringComparison.OrdinalIgnoreCase)
        && snapshot.IsVisible
        && !snapshot.IsMinimized
        && snapshot.ClientWidth > 0
        && snapshot.ClientHeight > 0;

    private static string GetWindowTextValue(nint handle)
    {
        var length = GetWindowTextLength(handle);
        var buffer = new StringBuilder(Math.Max(1, length + 1));
        GetWindowText(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetClassNameValue(nint handle)
    {
        var buffer = new StringBuilder(256);
        GetClassName(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint handle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint handle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint handle, nint deviceContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint handle, nint deviceContext, uint flags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);
}
