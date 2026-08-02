using Windows.Security.Authorization.AppCapabilityAccess;
using DDRGpScoreViewer.Capture;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class BorderlessCaptureTests
{
    [Fact]
    public async Task Allowed_borderless_access_is_forwarded_to_session_setup()
    {
        var requestCount = 0;
        var result = await ContinuousWindowsGraphicsCaptureAdapter.TryRequestBorderlessAccessAsync(
            CancellationToken.None,
            () => true,
            _ =>
            {
                requestCount++;
                return Task.FromResult(AppCapabilityAccessStatus.Allowed);
            });

        Assert.True(result);
        Assert.Equal(1, requestCount);
    }

    [Theory]
    [InlineData(AppCapabilityAccessStatus.DeniedBySystem)]
    [InlineData(AppCapabilityAccessStatus.NotDeclaredByApp)]
    [InlineData(AppCapabilityAccessStatus.DeniedByUser)]
    public async Task Denied_or_undeclared_borderless_access_keeps_the_default_border(
        AppCapabilityAccessStatus status)
    {
        var result = await ContinuousWindowsGraphicsCaptureAdapter.TryRequestBorderlessAccessAsync(
            CancellationToken.None,
            () => true,
            _ => Task.FromResult(status));
        var setterCalled = false;

        var applied = ContinuousWindowsGraphicsCaptureAdapter.TryApplyBorderlessCapture(
            result,
            () => setterCalled = true);

        Assert.False(result);
        Assert.False(applied);
        Assert.False(setterCalled);
    }

    [Fact]
    public async Task Unsupported_api_does_not_request_access_or_fail_capture_start()
    {
        var requestCalled = false;
        var result = await ContinuousWindowsGraphicsCaptureAdapter.TryRequestBorderlessAccessAsync(
            CancellationToken.None,
            () => false,
            _ =>
            {
                requestCalled = true;
                return Task.FromResult(AppCapabilityAccessStatus.Allowed);
            });

        Assert.False(result);
        Assert.False(requestCalled);
    }

    [Fact]
    public async Task Borderless_api_exception_falls_back_without_changing_capture_status()
    {
        var result = await ContinuousWindowsGraphicsCaptureAdapter.TryRequestBorderlessAccessAsync(
            CancellationToken.None,
            () => true,
            _ => Task.FromException<AppCapabilityAccessStatus>(
                new InvalidOperationException("capability unavailable")));
        var setterCalled = false;

        var applied = ContinuousWindowsGraphicsCaptureAdapter.TryApplyBorderlessCapture(
            result,
            () => setterCalled = true);

        Assert.False(result);
        Assert.False(applied);
        Assert.False(setterCalled);
    }

    [Fact]
    public void Allowed_access_applies_borderless_setting_and_setter_failure_falls_back()
    {
        var setterCalled = false;
        var applied = ContinuousWindowsGraphicsCaptureAdapter.TryApplyBorderlessCapture(
            borderlessAccessGranted: true,
            () => setterCalled = true);

        Assert.True(applied);
        Assert.True(setterCalled);

        var failed = ContinuousWindowsGraphicsCaptureAdapter.TryApplyBorderlessCapture(
            borderlessAccessGranted: true,
            () => throw new InvalidOperationException("session API unavailable"));

        Assert.False(failed);
    }
}
