using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VideoNR.Models;

namespace VideoNR;

public partial class Editor
{
    private void LoadMedia(ClipItem? target)
    {
        clock.Stop();
        loadingClip = target;
        ready = playing = false;
        position = 0;
        Player.Close();
        Player.Source = target is null ? null : new Uri(target.Path, UriKind.Absolute);
        VideoTag.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Visibility = Visibility.Visible;
        VideoMessage.Text = target is null ? "Arraste um vídeo ou imagem" : "Carregando vídeo…";
        VideoSubMessage.Text = target is null ? "MP4, MKV, MOV ou imagem PNG / JPG" : "Preparando visualização…";
        EmptyOpenButton.Visibility = target is null ? Visibility.Visible : Visibility.Collapsed;
        EmptyPrintButton.Visibility = target is null ? Visibility.Visible : Visibility.Collapsed;
        PlayLabel.Text = "Reproduzir";
        PlayIcon.Text = PlayGlyph;
        if (target is not null) { Player.Play(); Player.Pause(); }
        DrawPosition();
    }

    private void MediaReady(object sender, RoutedEventArgs e)
    {
        if (clip is null || !ReferenceEquals(clip, loadingClip)) return;
        ready = true;
        if (Player.NaturalDuration.HasTimeSpan)
            clip.Duration = Player.NaturalDuration.TimeSpan.TotalSeconds;
        if (clip.VideoWidth <= 0 && Player.NaturalVideoWidth > 0)
        {
            clip.VideoWidth = Player.NaturalVideoWidth;
            clip.VideoHeight = Player.NaturalVideoHeight;
        }
        Player.Pause();
        position = clip.Trim.Start > 0 ? Math.Min(clip.Trim.Start, clip.Duration) : Math.Min(.03, clip.Duration);
        if (position > 0) Player.Position = TimeSpan.FromSeconds(position);
        VideoPlaceholder.Visibility = Visibility.Collapsed;
        VideoTag.Visibility = Visibility.Visible;
        RefreshTrimFields();
        UpdateControls();
    }

    private void MediaFailure(object sender, ExceptionRoutedEventArgs e)
    {
        if (clip is null || !ReferenceEquals(clip, loadingClip)) return;
        clock.Stop();
        playing = ready = false;
        VideoTag.Visibility = Visibility.Collapsed;
        VideoPlaceholder.Visibility = Visibility.Visible;
        VideoMessage.Text = "O Windows não conseguiu reproduzir este vídeo.";
        VideoSubMessage.Text = "Você ainda pode exportá-lo normalmente pela fila.";
        EmptyOpenButton.Visibility = Visibility.Collapsed;
        EmptyPrintButton.Visibility = Visibility.Collapsed;
        UpdateControls();
    }

    private void MediaFinished(object sender, RoutedEventArgs e) => Pause();
    private void PlayClick(object sender, RoutedEventArgs e) => TogglePlayback();
    private void PreviewClick(object sender, MouseButtonEventArgs e) => TogglePlayback();
    private void MuteClick(object sender, RoutedEventArgs e) => ToggleMute();
    private void StepBackFrameClick(object sender, RoutedEventArgs e) => StepFrame(-1);
    private void StepForwardFrameClick(object sender, RoutedEventArgs e) => StepFrame(1);

    private void StepFrame(int deltaFrames)
    {
        if (!ready || busy || clip is null) return;
        Pause();
        var stepSeconds = clip.FrameRate is { } fps && fps > 0 ? 1.0 / fps : FrameStepSeconds;
        Seek(position + deltaFrames * stepSeconds);
    }

    private void ToggleMute()
    {
        if (!ready) return;
        Player.IsMuted = !Player.IsMuted;
        MuteIcon.Text = Player.IsMuted ? MuteGlyph : VolumeGlyph;
        MuteLabel.Text = Player.IsMuted ? "Mudo" : "Som";
    }

    private void TogglePlayback()
    {
        if (!ready) return;
        if (playing) Pause();
        else
        {
            if (clip is { Duration: > 0 } current &&
                (position < current.Trim.Start || position >= current.Trim.EndAt(current.Duration) - .005))
                Seek(current.Trim.Start);
            Player.Play();
            playing = true;
            clock.Start();
            PlayLabel.Text = "Pausar";
            PlayIcon.Text = PauseGlyph;
        }
    }

    private void Pause()
    {
        clock.Stop();
        if (ready) Player.Pause();
        playing = false;
        PlayLabel.Text = "Reproduzir";
        PlayIcon.Text = PlayGlyph;
    }

    private void UpdateClock()
    {
        if (!ready || !playing || clip is null) return;
        position = Player.Position.TotalSeconds;
        var end = clip.Trim.EndAt(clip.Duration);
        if (clip.Duration > 0 && position >= end)
        {
            Pause();
            Seek(end);
            return;
        }
        DrawPosition();
    }

    private double TrackWidth => Math.Max(1, TrimTrack.ActualWidth - 2 * StartHandle.Width);

    private double TimeToTrack(double seconds, double duration) =>
        StartHandle.Width + TrackWidth * Math.Clamp(seconds / duration, 0, 1);

    private double MouseTime() => clip is { Duration: > 0 } current
        ? Math.Clamp((Mouse.GetPosition(TrimTrack).X - StartHandle.Width) / TrackWidth, 0, 1) * current.Duration : 0;

    private double MouseTimeRaw(double duration) =>
        (Mouse.GetPosition(TrimTrack).X - StartHandle.Width) / TrackWidth * duration;

    private void Seek(double seconds)
    {
        if (!ready || clip is null) return;
        position = Math.Clamp(seconds, 0, clip.Duration);
        Player.Position = TimeSpan.FromSeconds(position);
        DrawPosition();
    }

    private void ResizeTrim(object sender, SizeChangedEventArgs e) => DrawTrim();

    private void TransportBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 680;
        Grid.SetRow(TrimControlsPanel, compact ? 1 : 0);
        Grid.SetColumn(TrimControlsPanel, compact ? 0 : 1);
        Grid.SetColumnSpan(TrimControlsPanel, compact ? 2 : 1);
        TrimControlsPanel.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        TrimControlsPanel.Margin = compact ? new Thickness(0, 7, 0, 0) : new Thickness(10, 0, 0, 0);
    }

    private void DrawTrim()
    {
        if (clip is not { Duration: > 0 } current)
        {
            TrimRange.Width = 0;
            TrimMaskLeft.Visibility = TrimMaskRight.Visibility = Visibility.Collapsed;
            StartHandle.Visibility = EndHandle.Visibility = Playhead.Visibility = Visibility.Hidden;
            RulerCanvas.Children.Clear();
            DrawPosition();
            return;
        }

        StartHandle.Visibility = EndHandle.Visibility = Playhead.Visibility = Visibility.Visible;
        var x0 = StartHandle.Width;
        var xMax = x0 + TrackWidth;
        var x1 = TimeToTrack(current.Trim.Start, current.Duration);
        var x2 = TimeToTrack(current.Trim.EndAt(current.Duration), current.Duration);

        Canvas.SetLeft(TrimRange, x1);
        Canvas.SetLeft(StartHandle, x1 - StartHandle.Width);
        Canvas.SetLeft(EndHandle, x2);
        TrimRange.Width = Math.Max(0, x2 - x1);

        if (x1 > x0 + 0.5)
        {
            TrimMaskLeft.Visibility = Visibility.Visible;
            Canvas.SetLeft(TrimMaskLeft, x0);
            TrimMaskLeft.Width = Math.Max(0, x1 - x0);
        }
        else
        {
            TrimMaskLeft.Visibility = Visibility.Collapsed;
        }

        if (xMax > x2 + 0.5)
        {
            TrimMaskRight.Visibility = Visibility.Visible;
            Canvas.SetLeft(TrimMaskRight, x2);
            TrimMaskRight.Width = Math.Max(0, xMax - x2);
        }
        else
        {
            TrimMaskRight.Visibility = Visibility.Collapsed;
        }

        DrawRuler(current.Duration);
        DrawPosition();
    }

    private void DrawRuler(double duration)
    {
        RulerCanvas.Children.Clear();
        var width = TrackWidth;
        if (duration <= 0 || width <= 24) return;

        var step = RulerSteps[^1];
        foreach (var candidate in RulerSteps)
        {
            if (candidate * width / duration >= 76)
            {
                step = candidate;
                break;
            }
        }

        var tickBrush = (Brush)FindResource("BorderControl");
        var labelBrush = (Brush)FindResource("TextMuted");
        var monoFont = (FontFamily)FindResource("MonoFont");
        var minorStep = step / 4.0;

        for (var t = 0.0; t <= duration + 1e-6; t += minorStep)
        {
            var x = StartHandle.Width + width * Math.Clamp(t / duration, 0, 1);
            var ratio = t / step;
            var isMajor = Math.Abs(ratio - Math.Round(ratio)) < 1e-3;

            var tick = new Border
            {
                Width = 1,
                Height = isMajor ? 5 : 3,
                Background = tickBrush
            };
            Canvas.SetLeft(tick, x);
            Canvas.SetBottom(tick, 0);
            RulerCanvas.Children.Add(tick);

            if (isMajor && x + 52 <= RulerCanvas.ActualWidth)
            {
                var label = new TextBlock
                {
                    Text = TrimSelection.Format(t),
                    FontFamily = monoFont,
                    FontSize = 9.5,
                    Foreground = labelBrush
                };
                Canvas.SetLeft(label, x + 3);
                Canvas.SetTop(label, 0);
                RulerCanvas.Children.Add(label);
            }
        }
    }

    private void DrawPosition()
    {
        var duration = clip?.Duration ?? 0;
        Canvas.SetLeft(Playhead, duration > 0 ? TimeToTrack(position, duration) - 4.5 : 0);
        TimeLabel.Text = $"{TrimSelection.Format(position, true)} / {TrimSelection.Format(duration, true)}";
    }

    private void RefreshTrimFields()
    {
        var duration = clip?.Duration ?? 0;
        var trim = clip?.Trim ?? default;
        refreshingFields = true;
        StartTime.Text = TrimSelection.Format(trim.Start, true);
        EndTime.Text = TrimSelection.Format(trim.EndAt(duration), true);
        refreshingFields = false;
        TrimLabel.Text = duration <= 0 ? "" : trim == default
            ? "Vídeo inteiro"
            : $"Duração do corte · {TrimSelection.Format(trim.EndAt(duration) - trim.Start, true)}";
        DrawTrim();
    }

    private void ScrubDown(object sender, MouseButtonEventArgs e)
    {
        if (!TrimTrack.IsEnabled || !TrimTrack.CaptureMouse()) return;
        scrubbing = true;
        Pause();
        Seek(MouseTime());
        e.Handled = true;
    }

    private void ScrubMove(object sender, MouseEventArgs e)
    {
        if (scrubbing && e.LeftButton == MouseButtonState.Pressed) Seek(MouseTime());
    }

    private void ScrubUp(object sender, MouseButtonEventArgs e)
    {
        if (!scrubbing) return;
        Seek(MouseTime());
        TrimTrack.ReleaseMouseCapture();
        scrubbing = false;
        e.Handled = true;
    }

    private void ScrubLost(object sender, MouseEventArgs e) => scrubbing = false;

    private void TrimDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb handle || clip is not { Duration: > 0 } current) return;
        var edge = Equals(handle.Tag, "start") ? current.Trim.Start : current.Trim.EndAt(current.Duration);
        trimPointerOffset = edge - MouseTimeRaw(current.Duration);
        Pause();
    }

    private void TrimDrag(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb handle && clip is { Duration: > 0 } current)
            MoveTrimEdge(Equals(handle.Tag, "start"), MouseTimeRaw(current.Duration) + trimPointerOffset);
    }

    private void MoveTrimEdge(bool start, double time)
    {
        if (busy || clip is not { Duration: > 0 } current) return;
        current.Trim = current.Trim.MoveEdge(start, time, current.Duration);
        RefreshTrimFields();
        Seek(start ? current.Trim.Start : current.Trim.EndAt(current.Duration));
    }

    private void TimecodeLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox field) ApplyTimecode(field);
    }

    private void TimecodeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox field) return;
        ApplyTimecode(field);
        e.Handled = true;
    }

    private void ApplyTimecode(TextBox field)
    {
        if (refreshingFields || busy || clip is not { Duration: > 0 } current) return;
        var isStart = Equals(field.Tag, "start");
        var priorValue = isStart ? current.Trim.Start : current.Trim.EndAt(current.Duration);
        if (field.Text == TrimSelection.Format(priorValue, true)) return;
        if (!TrimSelection.TryParse(field.Text, out var time))
        {
            Status.Text = "Digite um tempo no formato HH:MM:SS.mmm.";
            RefreshTrimFields();
            return;
        }
        Pause();
        MoveTrimEdge(isStart, time);
    }

    private void MarkStartClick(object sender, RoutedEventArgs e)
    {
        if (ready && !busy) MoveTrimEdge(true, position);
    }

    private void MarkEndClick(object sender, RoutedEventArgs e)
    {
        if (ready && !busy) MoveTrimEdge(false, position);
    }

    private void ResetTrim(object sender, RoutedEventArgs e)
    {
        if (clip is null) return;
        clip.Trim = default;
        RefreshTrimFields();
    }
}
