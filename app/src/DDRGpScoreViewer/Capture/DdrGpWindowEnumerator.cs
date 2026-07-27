using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DDRGpScoreViewer.Capture;

public sealed record DdrGpWindowCandidate(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    int ClientWidth,
    int ClientHeight)
{
    public string DisplayName => string.IsNullOrWhiteSpace(WindowTitle)
        ? $"{ProcessName} / client={ClientWidth} x {ClientHeight}"
        : $"{WindowTitle} / {ProcessName} / client={ClientWidth} x {ClientHeight}";

    public CaptureTargetInfo TargetInfo => new(
        DisplayName,
        ClientWidth,
        ClientHeight);
}

public interface IDdrGpWindowEnumerator
{
    Task<IReadOnlyList<DdrGpWindowCandidate>> EnumerateAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DdrGpWindowEnumerator : IDdrGpWindowEnumerator
{
    private const string TargetProcessName = "ddr-konaste";
    private const int TargetClientWidth = 1280;
    private const int TargetClientHeight = 720;

    public Task<IReadOnlyList<DdrGpWindowCandidate>> EnumerateAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DdrGpWindowCandidate>>(() =>
        {
            var windows = new List<DdrGpWindowCandidate>();
            EnumWindows((handle, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = TryGetSnapshot(handle);
                if (candidate is not null)
                {
                    windows.Add(candidate);
                }
                return true;
            }, 0);
            return windows.OrderBy(window => window.ProcessId)
                .ThenBy(window => window.Handle.ToInt64())
                .ToList();
        }, cancellationToken);

    internal static bool IsDdrGpTarget(DdrGpWindowCandidate candidate) =>
        candidate.ProcessName.Equals(TargetProcessName, StringComparison.OrdinalIgnoreCase)
        && candidate.ClientWidth == TargetClientWidth
        && candidate.ClientHeight == TargetClientHeight;

    private static DdrGpWindowCandidate? TryGetSnapshot(nint handle)
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
        if (!GetClientRect(handle, out var rect))
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processIdValue));
            return new DdrGpWindowCandidate(
                handle,
                process.Id,
                process.ProcessName,
                GetWindowTextValue(handle),
                Math.Max(0, rect.Right - rect.Left),
                Math.Max(0, rect.Bottom - rect.Top));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string GetWindowTextValue(nint handle)
    {
        var length = GetWindowTextLength(handle);
        var buffer = new StringBuilder(Math.Max(1, length + 1));
        GetWindowText(handle, buffer, buffer.Capacity);
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
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint handle, out Rect rect);
}
