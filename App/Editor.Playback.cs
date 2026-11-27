using System.Windows;
using System.Windows.Input;
using VideoNR.Models;
using VideoNR.Services;
using System.IO;
using System.Runtime.InteropServices;

namespace VideoNR;

public partial class Editor
{
    private CompositionSlice? activePlaybackSlice;
    private DateTime suppressEndCheckUntilUtc;
    private double? expectedSeekSourcePos;

    private void UnloadMedia()
    {
        clock.Stop();
        activePlaybackSlice = null;
        expectedSeekSourcePos = null;
        loadedMediaPath = null;
        loadingMediaPath = null;
        pendingAutoPlay = false;
        pendingSourceSeek = null;
        ready = playing = false;
        Player.Close();
        Player.Source = null;
        Player.Visibility = Visibility.Visible;
        VideoTag.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Visibility = Visibility.Visible;
        VideoMessage.Text = "Arraste vídeos ou imagens";
        VideoSubMessage.Text = "Monte camadas V1, V2… · C ou S Cortar na agulha · F Ajustar";
        EmptyOpenButton.Visibility = Visibility.Visible;
        EmptyPrintButton.Visibility = Visibility.Visible;
        PlayLabel.Text = "Reproduzir";
        PlayIcon.Text = PlayGlyph;
        DrawPosition();
    }

    private void LoadMediaForPath(string path, double seekToSeconds, bool autoPlay)
    {
        Player.Visibility = Visibility.Visible;

        if (ready && loadedMediaPath is not null
            && loadedMediaPath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            var safeSeek = Math.Max(0, seekToSeconds);
            expectedSeekSourcePos = safeSeek;
            suppressEndCheckUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
            Player.Position = TimeSpan.FromSeconds(safeSeek);
            if (autoPlay && !playing)
                StartPlayerClock();
            else if (!autoPlay && playing)
                Pause();
            DrawPosition();
            return;
        }

        clock.Stop();
        loadingMediaPath = path;
        loadedMediaPath = null;
        pendingAutoPlay = autoPlay;
        pendingSourceSeek = Math.Max(0, seekToSeconds);
        ready = false;
        playing = false;

        Player.Close();
        Player.Source = new Uri(path, UriKind.Absolute);

        if (!autoPlay)
        {
            VideoTag.Visibility = Visibility.Collapsed;
            VideoPlaceholder.Visibility = Visibility.Visible;
            VideoMessage.Text = "Carregando vídeo…";
            VideoSubMessage.Text = System.IO.Path.GetFileName(path);
            EmptyOpenButton.Visibility = Visibility.Collapsed;
            EmptyPrintButton.Visibility = Visibility.Collapsed;
            PlayLabel.Text = "Reproduzir";
            PlayIcon.Text = PlayGlyph;
        }

        Player.Play();
        if (!autoPlay)
            Player.Pause();
    }

    private void MediaReady(object sender, RoutedEventArgs e)
    {
        if (loadingMediaPath is null) return;
        loadedMediaPath = loadingMediaPath;
        loadingMediaPath = null;
        ready = true;

        var naturalDuration = Player.NaturalDuration.HasTimeSpan
            ? Player.NaturalDuration.TimeSpan.TotalSeconds
            : 0;

        if (naturalDuration > 0)
        {
            var hadZero = false;
            if (FindClipByPath(loadedMediaPath) is { } matchingClip)
            {
                hadZero = matchingClip.Duration <= 0;
                matchingClip.Duration = naturalDuration;
                if (matchingClip.VideoWidth <= 0 && Player.NaturalVideoWidth > 0)
                {
                    matchingClip.VideoWidth = Player.NaturalVideoWidth;
                    matchingClip.VideoHeight = Player.NaturalVideoHeight;
                }
            }
            sequence.UpdateSourceDuration(loadedMediaPath, naturalDuration);
            if (hadZero && !hasUserZoomed)
                FitTimelineToViewport(keepAutoFit: true);
        }

        var seek = pendingSourceSeek ?? 0;
        pendingSourceSeek = null;
        if (naturalDuration > 0)
            seek = Math.Clamp(seek, 0, naturalDuration);
        if (seek > 0)
        {
            expectedSeekSourcePos = seek;
            suppressEndCheckUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
            Player.Position = TimeSpan.FromSeconds(seek);
        }
        else
        {
            expectedSeekSourcePos = 0;
            suppressEndCheckUntilUtc = DateTime.UtcNow.AddMilliseconds(200);
        }

        Player.Visibility = Visibility.Visible;
        VideoPlaceholder.Visibility = Visibility.Collapsed;
        VideoTag.Visibility = Visibility.Visible;

        var shouldPlay = pendingAutoPlay;
        pendingAutoPlay = false;
        if (shouldPlay)
            StartPlayerClock();
        else
            Player.Pause();

        RefreshTrimFields();
        UpdateControls();
    }

    private void MediaFailure(object sender, ExceptionRoutedEventArgs e)
    {
        clock.Stop();
        playing = ready = false;
        pendingAutoPlay = false;
        loadingMediaPath = null;
        VideoTag.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Visibility = Visibility.Visible;
        VideoMessage.Text = "O Windows não conseguiu reproduzir este vídeo.";
        VideoSubMessage.Text = "Você ainda pode exportá-lo normalmente pela sequência ou lote.";
        EmptyOpenButton.Visibility = Visibility.Collapsed;
        EmptyPrintButton.Visibility = Visibility.Collapsed;
        UpdateControls();
    }

    private void MediaFinished(object sender, RoutedEventArgs e)
    {
        if (!playing) return;
        if (activePlaybackSlice is { } cur)
            AdvanceFromSlice(cur);
        else
            AdvanceToNextSliceOrStop(timelinePosition + 0.002);
    }

    private void PlayClick(object sender, RoutedEventArgs e) => TogglePlayback();
    private void PreviewClick(object sender, MouseButtonEventArgs e) => TogglePlayback();
    private void MuteClick(object sender, RoutedEventArgs e) => ToggleMute();
    private void StepBackFrameClick(object sender, RoutedEventArgs e) => StepFrame(-1);
    private void StepForwardFrameClick(object sender, RoutedEventArgs e) => StepFrame(1);

    private string? indexedPath;
    private Task<FrameTimes>? frameTimesTask;
    private CancellationTokenSource? indexingCancellation;
    private bool stepping;

    private Task<FrameTimes> GetFrameTimesAsync(string path)
    {
        if (frameTimesTask is null || frameTimesTask.IsFaulted || frameTimesTask.IsCanceled
            || !string.Equals(indexedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            indexingCancellation?.Cancel();
            indexingCancellation?.Dispose();
            indexingCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCts.Token);
            var token = indexingCancellation.Token;
            indexedPath = path;
            frameTimesTask = Task.Run(() => MediaFoundationFrameCodec.ReadFrameTimes(path, token), token);
        }
        return frameTimesTask;
    }

    private async void StepFrame(int direction)
    {
        if (busy || stepping || sequence.TotalDuration <= 0) return;
        Pause();
        var position = timelinePosition;
        var slices = sequence.ResolveComposition();
        if (direction < 0) slices.Reverse();
        stepping = true;
        try
        {
            foreach (var s in slices)
            {
                if (direction > 0 && position >= s.TimelineEnd || direction < 0 && position <= s.TimelineStart)
                    continue;
                var next = s.TimelineStart;
                if (direction < 0 || position >= s.TimelineStart)
                {
                    var frames = await GetFrameTimesAsync(s.Segment.SourcePath);
                    var offset = Math.Clamp(position - s.TimelineStart, 0, s.Duration);
                    var source = offset >= s.Duration ? s.SourceEnd : s.SourceStart + offset;
                    var pts = frames.Adjacent(source, direction);
                    if (direction > 0 && (pts is null || pts >= s.SourceEnd)) continue;
                    if (pts is { } t && t > s.SourceStart) next += t - s.SourceStart;
                }
                if (IsLoaded && !busy && !playing && timelinePosition == position
                    && sequence.LocateCompositionAt(next)?.Segment.Id == s.Segment.Id)
                    SeekTimeline(next, autoPlay: false);
                break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status.Text = $"Não foi possível localizar o quadro: {error.Message}";
        }
        finally { stepping = false; }
    }

    private void ToggleMute()
    {
        if (!ready) return;
        Player.IsMuted = !Player.IsMuted;
        MuteIcon.Text = Player.IsMuted ? MuteGlyph : VolumeGlyph;
    }

    private void StartPlayerClock()
    {
        Player.Play();
        playing = true;
        clock.Start();
        PlayLabel.Text = "Pausar";
        PlayIcon.Text = PauseGlyph;
    }

    private void TogglePlayback()
    {
        if (sequence.TotalDuration <= 0) return;
        if (playing)
        {
            Pause();
            return;
        }

        var composition = sequence.ResolveComposition();
        if (composition.Count == 0)
        {
            Status.Text = "Nenhum clipe ativo ou visível nas faixas para reproduzir.";
            return;
        }

        var lastEnd = composition[^1].TimelineEnd;
        if (timelinePosition >= lastEnd)
            timelinePosition = composition[0].TimelineStart;

        if (sequence.LocateCompositionAt(timelinePosition) is null)
        {
            var next = sequence.FindNextCompositionSliceAfter(timelinePosition);
            timelinePosition = next?.TimelineStart ?? composition[0].TimelineStart;
        }

        SeekTimeline(timelinePosition, autoPlay: true);
    }

    private void Pause()
    {
        clock.Stop();
        activePlaybackSlice = null;
        pendingAutoPlay = false;
        if (ready) Player.Pause();
        playing = false;
        PlayLabel.Text = "Reproduzir";
        PlayIcon.Text = PlayGlyph;
    }

    private void SyncTimelinePlaybackToPlayhead()
    {
        if (sequence.IsEmpty) return;
        SeekTimeline(timelinePosition, autoPlay: playing);
    }

    private void SeekTimeline(double seconds, bool autoPlay)
    {
        var total = sequence.TotalDuration;
        if (total <= 0)
        {
            activePlaybackSlice = null;
            timelinePosition = 0;
            DrawPosition();
            return;
        }

        timelinePosition = Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, total);
        var slice = sequence.LocateCompositionAt(timelinePosition);

        if (slice is null)
        {
            if (autoPlay)
            {
                var next = sequence.FindNextCompositionSliceAfter(timelinePosition);
                if (next is { } nextSlice)
                {
                    timelinePosition = nextSlice.TimelineStart;
                    slice = nextSlice;
                }
                else
                {
                    activePlaybackSlice = null;
                    expectedSeekSourcePos = null;
                    Pause();
                    Player.Visibility = Visibility.Hidden;
                    DrawPosition();
                    UpdateVideoTag();
                    return;
                }
            }
            else
            {
                activePlaybackSlice = null;
                expectedSeekSourcePos = null;
                if (ready) Player.Pause();
                Player.Visibility = Visibility.Hidden;
                VideoTag.Visibility = Visibility.Visible;
                VideoTagText.Text = "Espaço vazio";
                UpdateHeaderClipInfo();
                DrawPosition();
                return;
            }
        }

        var activeSlice = slice.Value;
        activePlaybackSlice = activeSlice;
        var seg = activeSlice.Segment;
        var sourceOffset = Math.Clamp(timelinePosition - activeSlice.TimelineStart, 0, activeSlice.Duration);
        var sourcePos = activeSlice.SourceStart + sourceOffset;
        expectedSeekSourcePos = sourcePos;
        suppressEndCheckUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
        Player.Visibility = Visibility.Visible;

        LoadMediaForPath(seg.SourcePath, sourcePos, autoPlay);
        UpdateHeaderClipInfo();
        UpdateVideoTag();
        DrawPosition();
    }

    private void UpdateClock()
    {
        if (!ready || !playing || sequence.IsEmpty) return;

        var slice = activePlaybackSlice ?? sequence.LocateCompositionAt(timelinePosition);
        if (slice is not { } current)
        {
            AdvanceToNextSliceOrStop(timelinePosition);
            return;
        }

        var playerSourceTime = Player.Position.TotalSeconds;
        if (expectedSeekSourcePos is { } expectedSeek)
        {
            if (Math.Abs(playerSourceTime - expectedSeek) <= 0.5 || DateTime.UtcNow >= suppressEndCheckUntilUtc)
            {
                expectedSeekSourcePos = null;
            }
            else
            {
                DrawPosition();
                return;
            }
        }

        var localOffset = Math.Max(0, playerSourceTime - current.SourceStart);
        timelinePosition = Math.Min(current.TimelineEnd, current.TimelineStart + localOffset);

        EnsurePlayheadVisibleDuringPlayback();

        if (playerSourceTime >= current.SourceEnd)
        {
            AdvanceFromSlice(current);
            return;
        }

        DrawPosition();
    }

    private void AdvanceFromSlice(CompositionSlice current)
    {
        var next = sequence.FindNextCompositionSlice(current);
        if (next is not { } nextSlice)
        {
            Pause();
            var slices = sequence.ResolveComposition();
            timelinePosition = slices.Count > 0 ? slices[^1].TimelineEnd : sequence.TotalDuration;
            DrawPosition();
            return;
        }

        SeekTimeline(nextSlice.TimelineStart, autoPlay: true);
    }

    private void AdvanceToNextSliceOrStop(double afterTimelineSeconds)
    {
        var next = sequence.FindNextCompositionSliceAfter(afterTimelineSeconds);
        if (next is not { } nextSlice)
        {
            Pause();
            var slices = sequence.ResolveComposition();
            timelinePosition = slices.Count > 0 ? slices[^1].TimelineEnd : sequence.TotalDuration;
            DrawPosition();
            return;
        }

        SeekTimeline(nextSlice.TimelineStart, autoPlay: true);
    }

    private void EnsurePlayheadVisibleDuringPlayback()
    {
        var viewportW = TimelineScroll.ViewportWidth;
        if (viewportW <= 40 || TimelineScroll.ScrollableWidth <= 0) return;

        var x = TimeToX(timelinePosition);
        var left = TimelineScroll.HorizontalOffset;
        if (x > left + viewportW - 28 || x < left)
        {
            var target = Math.Clamp(x - viewportW * 0.25, 0, TimelineScroll.ScrollableWidth);
            TimelineScroll.ScrollToHorizontalOffset(target);
        }
    }
}
