using System.Windows;
using System.Windows.Input;
using VideoNR.Models;

namespace VideoNR;

public partial class Editor
{
    private void UnloadMedia()
    {
        clock.Stop();
        loadedMediaPath = null;
        loadingMediaPath = null;
        pendingAutoPlay = false;
        pendingSourceSeek = null;
        ready = playing = false;
        Player.Close();
        Player.Source = null;
        VideoTag.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Visibility = Visibility.Visible;
        VideoMessage.Text = "Arraste vídeos ou imagens";
        VideoSubMessage.Text = "Monte camadas V1, V2… · V Mover/Aparar · C Tesoura · F Ajustar";
        EmptyOpenButton.Visibility = Visibility.Visible;
        EmptyPrintButton.Visibility = Visibility.Visible;
        PlayLabel.Text = "Reproduzir";
        PlayIcon.Text = PlayGlyph;
        DrawPosition();
    }

    private void LoadMediaForPath(string path, double seekToSeconds, bool autoPlay)
    {
        if (ready && loadedMediaPath is not null
            && loadedMediaPath.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            var safeSeek = Math.Max(0, seekToSeconds);
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

        var seek = pendingSourceSeek ?? Math.Min(0.03, naturalDuration);
        pendingSourceSeek = null;
        if (naturalDuration > 0)
            seek = Math.Clamp(seek, 0, naturalDuration);
        if (seek > 0)
            Player.Position = TimeSpan.FromSeconds(seek);

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
        AdvanceToNextSliceOrStop(timelinePosition + 0.03);
    }

    private void PlayClick(object sender, RoutedEventArgs e) => TogglePlayback();
    private void PreviewClick(object sender, MouseButtonEventArgs e) => TogglePlayback();
    private void MuteClick(object sender, RoutedEventArgs e) => ToggleMute();
    private void StepBackFrameClick(object sender, RoutedEventArgs e) => StepFrame(-1);
    private void StepForwardFrameClick(object sender, RoutedEventArgs e) => StepFrame(1);

    private void StepFrame(int deltaFrames)
    {
        if (busy || sequence.TotalDuration <= 0) return;
        Pause();

        var slice = sequence.LocateCompositionAt(timelinePosition);
        var fps = slice is { } s && FindClipByPath(s.Segment.SourcePath) is { FrameRate: > 0 } c
            ? c.FrameRate.Value
            : clip is { FrameRate: > 0 } fallback
                ? fallback.FrameRate.Value
                : 1.0 / FrameStepSeconds;

        SeekTimeline(timelinePosition + deltaFrames * (1.0 / fps), autoPlay: false);
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
        if (timelinePosition >= lastEnd - 0.02)
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
                    Pause();
                    DrawPosition();
                    UpdateVideoTag();
                    return;
                }
            }
            else
            {
                if (ready) Player.Pause();
                VideoTag.Visibility = Visibility.Visible;
                VideoTagText.Text = "Espaço vazio";
                UpdateHeaderClipInfo();
                DrawPosition();
                return;
            }
        }

        var activeSlice = slice.Value;
        var seg = activeSlice.Segment;
        var sourceOffset = Math.Clamp(timelinePosition - activeSlice.TimelineStart, 0, activeSlice.Duration);
        var sourcePos = activeSlice.SourceStart + sourceOffset;

        LoadMediaForPath(seg.SourcePath, sourcePos, autoPlay);
        UpdateHeaderClipInfo();
        UpdateVideoTag();
        DrawPosition();
    }

    private void UpdateClock()
    {
        if (!ready || !playing || sequence.IsEmpty) return;

        var slice = sequence.LocateCompositionAt(timelinePosition);
        if (slice is not { } current)
        {
            AdvanceToNextSliceOrStop(timelinePosition);
            return;
        }

        var playerSourceTime = Player.Position.TotalSeconds;
        var localOffset = Math.Max(0, playerSourceTime - current.SourceStart);
        timelinePosition = Math.Min(current.TimelineEnd, current.TimelineStart + localOffset);

        EnsurePlayheadVisibleDuringPlayback();

        if (playerSourceTime >= current.SourceEnd - 0.025)
        {
            AdvanceToNextSliceOrStop(current.TimelineEnd + 0.002);
            return;
        }

        DrawPosition();
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
