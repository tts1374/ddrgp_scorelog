using Windows.Security.Authorization.AppCapabilityAccess;
using DDRGpScoreViewer.Capture;
using Xunit;

namespace DDRGpScoreViewer.Tests;

public sealed class BorderlessCaptureTests
{
    [Theory]
    [InlineData(1282, 754, 1, 33)]
    [InlineData(1280, 720, 0, 0)]
    public void Targeted_capture_uses_client_area_without_scaling(
        int captureWidth, int captureHeight, int x, int y)
    {
        var bounds = ContinuousWindowsGraphicsCaptureAdapter.CreateClientCaptureBounds(
            captureWidth, captureHeight, x, y, 1280, 720);
        Assert.Equal((uint)x, bounds.X);
        Assert.Equal((uint)y, bounds.Y);
        Assert.Equal(1280u, bounds.Width);
        Assert.Equal(720u, bounds.Height);
    }

    [Theory]
    [InlineData(-1, 33, 1280, 720)]
    [InlineData(1, -1, 1280, 720)]
    [InlineData(3, 33, 1280, 720)]
    [InlineData(1, 35, 1280, 720)]
    [InlineData(1, 33, 0, 720)]
    public void Invalid_client_area_is_rejected_without_guessing(int x, int y, int width, int height)
    {
        Assert.Throws<CaptureInvalidSizeException>(() =>
            ContinuousWindowsGraphicsCaptureAdapter.CreateClientCaptureBounds(1282, 754, x, y, width, height));
    }

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
