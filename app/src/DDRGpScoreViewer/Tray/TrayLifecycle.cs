using DDRGpScoreViewer.Models;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DDRGpScoreViewer.Tray;

public enum TrayNotificationKind
{
    Information,
    Error,
}

public interface ITrayIconService : IDisposable
{
    event EventHandler? StartRequested;
    event EventHandler? StopRequested;
    event EventHandler? ShowRequested;
    event EventHandler? ExitRequested;

    void UpdateMenu(TrayMenuState state, string statusText);
    void ShowNotification(string title, string message, TrayNotificationKind kind);
}

public sealed class WindowsTrayIconService : ITrayIconService
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ContextMenuStrip contextMenu;
    private readonly Forms.ToolStripMenuItem startItem;
    private readonly Forms.ToolStripMenuItem stopItem;
    private int disposed;

    public WindowsTrayIconService()
    {
        startItem = new Forms.ToolStripMenuItem("監視開始");
        stopItem = new Forms.ToolStripMenuItem("監視停止");
        var showItem = new Forms.ToolStripMenuItem("GP Score Logを開く");
        var exitItem = new Forms.ToolStripMenuItem("終了");
        startItem.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        stopItem.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        showItem.Click += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.AddRange(
            [startItem, stopItem, new Forms.ToolStripSeparator(), showItem, exitItem]);
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "GP Score Log — 待機中",
            ContextMenuStrip = contextMenu,
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        UpdateMenu(TrayMenuState.FromMonitoringState(MonitoringState.Idle), "待機中");
    }

    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public void UpdateMenu(TrayMenuState state, string statusText)
    {
        startItem.Enabled = state.CanStart;
        stopItem.Enabled = state.CanStop;
        var text = $"GP Score Log — {statusText}";
        notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void ShowNotification(string title, string message, TrayNotificationKind kind)
    {
        notifyIcon.ShowBalloonTip(
            4000,
            title,
            message,
            kind == TrayNotificationKind.Error
                ? Forms.ToolTipIcon.Error
                : Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        contextMenu.Dispose();
    }
}

public sealed class ApplicationLifecycleCoordinator : IDisposable
{
    private readonly ITrayIconService trayIcon;
    private readonly Func<Task> startMonitoring;
    private readonly Func<Task> stopMonitoring;
    private readonly Action showWindow;
    private readonly Action shutdown;
    private readonly Action beginExit;
    private readonly object exitLock = new();
    private readonly HashSet<string> notifiedUnresolvedCaptureEvents =
        new(StringComparer.Ordinal);
    private Task? exitTask;
    private Task? exitPreparationTask;
    private MonitoringState? lastNotifiedState;
    private bool exitRequested;
    private int disposed;

    public ApplicationLifecycleCoordinator(
        ITrayIconService trayIcon,
        Func<Task> startMonitoring,
        Func<Task> stopMonitoring,
        Action showWindow,
        Action shutdown,
        Action? beginExit = null)
    {
        this.trayIcon = trayIcon;
        this.startMonitoring = startMonitoring;
        this.stopMonitoring = stopMonitoring;
        this.showWindow = showWindow;
        this.shutdown = shutdown;
        this.beginExit = beginExit ?? (static () => { });
        trayIcon.StartRequested += TrayStartRequested;
        trayIcon.StopRequested += TrayStopRequested;
        trayIcon.ShowRequested += TrayShowRequested;
        trayIcon.ExitRequested += TrayExitRequested;
    }

    public Task StartAsync()
    {
        lock (exitLock)
        {
            if (exitRequested || Volatile.Read(ref disposed) != 0)
            {
                return Task.CompletedTask;
            }
        }
        return startMonitoring();
    }

    public Task StopAsync() => stopMonitoring();

    public void Show()
    {
        lock (exitLock)
        {
            if (exitRequested || Volatile.Read(ref disposed) != 0)
            {
                return;
            }
        }
        showWindow();
    }

    public Task ExitAsync()
    {
        lock (exitLock)
        {
            if (exitTask is not null)
            {
                return exitTask;
            }
            exitRequested = true;
            return exitTask = ExitCoreAsync();
        }
    }

    public Task PrepareForApplicationUpdateAsync()
    {
        lock (exitLock)
        {
            if (exitTask is not null)
            {
                return exitTask;
            }
            if (exitPreparationTask is not null)
            {
                return exitPreparationTask;
            }
            exitRequested = true;
            return exitPreparationTask = PrepareExitCoreAsync();
        }
    }

    public void UpdateMonitoringState(
        MonitoringState state,
        string stateDisplay,
        MonitoringResultSummary results,
        string reason,
        TrayMenuState? menuState = null)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }
        trayIcon.UpdateMenu(menuState ?? TrayMenuState.FromMonitoringState(state), stateDisplay);
        if (lastNotifiedState == state)
        {
            return;
        }
        lastNotifiedState = state;
        if (state == MonitoringState.Stopped && results.Saved > 0)
        {
            trayIcon.ShowNotification(
                "保存が完了しました",
                $"{results.Saved}件のプレーを正式DBへ保存しました。",
                TrayNotificationKind.Information);
        }
        else if (state is MonitoringState.TargetClosed or MonitoringState.Resized or
                 MonitoringState.DeviceLost or MonitoringState.CaptureFailed or
                 MonitoringState.WorkflowFailed)
        {
            trayIcon.ShowNotification(
                "監視を停止しました",
                reason,
                TrayNotificationKind.Error);
        }
    }

    public void NotifyUnresolvedCapture(UnresolvedCaptureNotification notification)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var eventKey = string.IsNullOrWhiteSpace(notification.EventId)
            ? notification.Message
            : notification.EventId;
        lock (exitLock)
        {
            if (exitRequested || Volatile.Read(ref disposed) != 0 ||
                !notifiedUnresolvedCaptureEvents.Add(eventKey))
            {
                return;
            }
        }

        trayIcon.ShowNotification(
            "自動保存できないプレーが発生しました",
            notification.Message,
            TrayNotificationKind.Information);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        trayIcon.StartRequested -= TrayStartRequested;
        trayIcon.StopRequested -= TrayStopRequested;
        trayIcon.ShowRequested -= TrayShowRequested;
        trayIcon.ExitRequested -= TrayExitRequested;
        trayIcon.Dispose();
    }

    private async Task PrepareExitCoreAsync()
    {
        try
        {
            beginExit();
            await stopMonitoring();
        }
        catch
        {
            // A failed preflight must not leave the app running with its exit gate set.
            await ExitCoreAsync(preparationAlreadyRunning: true);
            throw;
        }
    }

    private async Task ExitCoreAsync(bool preparationAlreadyRunning = false)
    {
        try
        {
            if (preparationAlreadyRunning)
            {
                beginExit();
                await stopMonitoring();
            }
            else if (exitPreparationTask is not null)
            {
                await exitPreparationTask;
            }
            else
            {
                beginExit();
                await stopMonitoring();
            }
        }
        catch (Exception exception)
        {
            trayIcon.ShowNotification(
                "監視停止に失敗しました",
                $"resource解放を完了できませんでした。processを終了します。{exception.Message}",
                TrayNotificationKind.Error);
        }
        finally
        {
            Dispose();
            shutdown();
        }
    }

    private async void TrayStartRequested(object? sender, EventArgs e) =>
        await RunTrayOperationAsync(StartAsync, "監視開始に失敗しました");

    private async void TrayStopRequested(object? sender, EventArgs e) =>
        await RunTrayOperationAsync(StopAsync, "監視停止に失敗しました");

    private void TrayShowRequested(object? sender, EventArgs e) => Show();
    private async void TrayExitRequested(object? sender, EventArgs e) => await ExitAsync();

    private async Task RunTrayOperationAsync(Func<Task> operation, string failureTitle)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            trayIcon.ShowNotification(failureTitle, exception.Message, TrayNotificationKind.Error);
        }
    }
}

public static class WindowLifecyclePolicy
{
    public static bool HideOnClose(bool applicationExitRequested) => !applicationExitRequested;

    public static bool HideOnMinimize(bool applicationExitRequested) => false;
}
