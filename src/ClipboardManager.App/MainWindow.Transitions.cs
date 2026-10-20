using System.Numerics;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace ClipboardManager.App;

public sealed partial class MainWindow
{
    private static readonly TimeSpan DismissDuration = TimeSpan.FromMilliseconds(180);
    private Task? _dismissTask;
    private CancellationTokenSource? _dismissCts;

    public Task HideWindowAsync()
    {
        if (_dismissTask is not null) return _dismissTask;
        if (!IsVisible) return Task.CompletedTask;
        if (!new UISettings().AnimationsEnabled)
        {
            HideWindow();
            return Task.CompletedTask;
        }

        _dismissCts = new CancellationTokenSource();
        return _dismissTask = DismissAsync(_dismissCts);
    }

    private async Task DismissAsync(CancellationTokenSource cancellation)
    {
        _outsideClickDetector.Stop();
        _copyNoticeTimer?.Stop();
        RootGrid.IsHitTestVisible = false;
        var visual = ElementCompositionPreview.GetElementVisual(RootGrid);
        try
        {
            var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0), new Vector2(1, 1));
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 1);
            fade.InsertKeyFrame(1, 0, easing);
            fade.Duration = DismissDuration;
            var slide = visual.Compositor.CreateScalarKeyFrameAnimation();
            slide.InsertKeyFrame(0, 0);
            slide.InsertKeyFrame(1, 20, easing);
            slide.Duration = DismissDuration;
            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Offset.Y", slide);
            await Task.Delay(DismissDuration, cancellation.Token);
            _appWindow.Hide();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Launcher dismiss animation failed; hiding immediately");
            _appWindow.Hide();
        }
        finally
        {
            if (ReferenceEquals(_dismissCts, cancellation))
            {
                ResetDismissAnimation();
                _dismissTask = null;
                _dismissCts = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelDismissAnimation()
    {
        _dismissCts?.Cancel();
        _dismissCts = null;
        _dismissTask = null;
        ResetDismissAnimation();
    }

    private void ResetDismissAnimation()
    {
        var visual = ElementCompositionPreview.GetElementVisual(RootGrid);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Offset.Y");
        visual.Opacity = 1;
        visual.Offset = Vector3.Zero;
        RootGrid.IsHitTestVisible = true;
    }
}
