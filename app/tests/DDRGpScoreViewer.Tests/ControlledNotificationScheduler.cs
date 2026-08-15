namespace DDRGpScoreViewer.Tests;

internal sealed class ControlledNotificationScheduler
{
    public List<ScheduledNotification> Scheduled { get; } = [];

    public Task ScheduleAsync(TimeSpan delay, Func<Task> clearNotification)
    {
        Scheduled.Add(new ScheduledNotification(delay, clearNotification));
        return Task.CompletedTask;
    }

    internal sealed record ScheduledNotification(
        TimeSpan Delay,
        Func<Task> ExpireAsync);
}
