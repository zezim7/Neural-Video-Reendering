using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VideoNR.Models;
using VideoNR.Services;

namespace VideoNR;

public partial class Editor
{
    private const double TrackHeight = 48.0;
    private const double TrackPaddingY = 4.0;
    private const double TimelineLeftPadding = 12.0;
    private const double TimelineRightTailSeconds = 8.0;
    private const double MinPixelsPerSecond = 0.15;
    private const double MaxPixelsPerSecond = 240.0;
    private const double EdgeHitPixels = 10.0;
    private const double SnapThresholdPixels = 8.0;

    private static readonly double[] RulerSteps =
    [
        0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200, 1800, 3600
    ];

    // Restrained darkroom segment top-strip accents to distinguish different source videos
    private static readonly Color[] SourceAccentPalette =
    [
        Color.FromRgb(0xE0, 0x9A, 0x24), // Optical amber
        Color.FromRgb(0x6C, 0x8E, 0x9E), // Cool slate
        Color.FromRgb(0x7A, 0x99, 0x72), // Sage darkroom
        Color.FromRgb(0xB8, 0x7D, 0x62), // Warm copper
        Color.FromRgb(0x8A, 0x82, 0x9E), // Muted lavender-gray
        Color.FromRgb(0x9A, 0x92, 0x6E), // Brass olive
    ];

    private enum TrackDragMode { None, Pan, Scrub, TrimLeft, TrimRight, MoveSegment }

    private readonly List<(FrameworkElement Dock, double CardLeft, double CardWidth)> stickyClipEntries = [];

    private TrackDragMode trackDragMode = TrackDragMode.None;
    private Guid dragSegmentId;
    private Point trackDragOrigin;
    private double dragStartPanH;
    private double dragStartPanV;
    private double dragInitialTimelineStart;
    private double dragInitialSourceStart;
    private double dragInitialSourceEnd;
    private double dragInitialPlayhead;
    private int dragInitialTrackIndex;
    private bool dragUndoPushed;
    private bool dragMutated;
    private bool hasUserZoomed;
    private bool syncingZoomSlider;
    private double pixelsPerSecond = 24.0;
    private int activeTrackIndex;

    private double TimeToX(double seconds) =>
        TimelineLeftPadding + Math.Max(0, seconds) * pixelsPerSecond;

    private double XToTime(double x) =>
        Math.Max(0, (x - TimelineLeftPadding) / Math.Max(MinPixelsPerSecond, pixelsPerSecond));

    private double SnapThresholdSeconds() =>
        SnapThresholdPixels / Math.Max(MinPixelsPerSecond, pixelsPerSecond);

    private double TrackIndexToY(int trackIndex)
    {
        var clamped = Math.Clamp(trackIndex, 0, sequence.TrackCount - 1);
        return (sequence.TrackCount - 1 - clamped) * TrackHeight;
    }

    private int YToTrackIndex(double y)
    {
        var row = (int)Math.Floor(Math.Max(0, y) / TrackHeight);
        return Math.Clamp(sequence.TrackCount - 1 - row, 0, sequence.TrackCount - 1);
    }

    private void AppendClipToTimeline(ClipItem item, int? targetTrackIndex = null, double? atTimelineSeconds = null)
    {
        if (busy) return;
        var track = Math.Clamp(targetTrackIndex ?? activeTrackIndex, 0, TimelineSequence.MaxTracks - 1);
        var duration = item.Duration > 0 ? item.Duration : MediaFoundationFrameCodec.TryGetDuration(item.Path) ?? 0;
        if (duration > 0 && item.Duration <= 0)
            item.Duration = duration;

        var wasEmpty = sequence.IsEmpty;
        history.PushUndo(sequence, selectedSegmentId);
        var seg = atTimelineSeconds.HasValue
            ? sequence.AddClipAt(item.Path, duration, atTimelineSeconds.Value, track, colorIndex: GetColorIndexForSource(item.Path))
            : sequence.AppendClip(item.Path, duration, colorIndex: GetColorIndexForSource(item.Path), trackIndex: track);

        activeTrackIndex = seg.TrackIndex;
        selectedSegmentId = seg.Id;

        if (wasEmpty || !hasUserZoomed)
            FitTimelineToViewport(keepAutoFit: true);

        SeekTimeline(seg.TimelineStart, autoPlay: false);
        SyncInspectorToCurrentScope();
        RefreshTrimFields();
        UpdateControls();
        Status.Text = $"\"{item.Name}\" inserido em V{seg.TrackIndex + 1} ({TrimSelection.Format(seg.TimelineStart, true)}).";
    }

    private void SelectSegment(Guid segmentId, bool seekToStart)
    {
        var seg = sequence.FindById(segmentId);
        if (seg is null) return;

        selectedSegmentId = segmentId;
        activeTrackIndex = seg.TrackIndex;
        if (seekToStart || timelinePosition < seg.TimelineStart - 1e-4 || timelinePosition > seg.TimelineEnd + 1e-4)
            SeekTimeline(seg.TimelineStart, autoPlay: false);

        SyncInspectorToCurrentScope();
        RefreshTrimFields();
        UpdateControls();
    }

    private void EnsureValidSegmentSelection()
    {
        if (sequence.IsEmpty)
        {
            selectedSegmentId = null;
            return;
        }
        if (selectedSegmentId is not { } id || sequence.IndexOf(id) < 0)
            selectedSegmentId = sequence.Segments[0].Id;
    }

    private void SyncAfterSequenceMutation()
    {
        EnsureValidSegmentSelection();
        activeTrackIndex = Math.Clamp(activeTrackIndex, 0, sequence.TrackCount - 1);
        if (sequence.IsEmpty)
        {
            Pause();
            timelinePosition = 0;
            if (clip is null) UnloadMedia();
        }
        else
        {
            timelinePosition = Math.Clamp(timelinePosition, 0, Math.Max(0, sequence.TotalDuration));
            SeekTimeline(timelinePosition, autoPlay: false);
        }
        SyncInspectorToCurrentScope();
        RefreshTrimFields();
        UpdateControls();
    }

    private bool CanSplitAt(double timeSeconds)
    {
        if (SelectedSegment is { } sel
            && !sequence.IsTrackLocked(sel.TrackIndex)
            && timeSeconds - sel.TimelineStart >= TimelineSegment.MinDuration
            && sel.TimelineEnd - timeSeconds >= TimelineSegment.MinDuration)
        {
            return true;
        }

        return sequence.Segments.Any(s =>
            !sequence.IsTrackLocked(s.TrackIndex)
            && timeSeconds - s.TimelineStart >= TimelineSegment.MinDuration
            && s.TimelineEnd - timeSeconds >= TimelineSegment.MinDuration);
    }

    private void SplitSegmentClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;

        if (playing && ready && expectedSeekSourcePos is null && activePlaybackSlice is { } liveSlice)
        {
            var localOffset = Math.Max(0, Player.Position.TotalSeconds - liveSlice.SourceStart);
            timelinePosition = Math.Clamp(liveSlice.TimelineStart + localOffset, liveSlice.TimelineStart, liveSlice.TimelineEnd);
        }

        // If selected segment covers timelinePosition and can split, split it; else split all unlocked under playhead
        Guid? onlyId = null;
        if (SelectedSegment is { } sel
            && !sequence.IsTrackLocked(sel.TrackIndex)
            && timelinePosition - sel.TimelineStart >= TimelineSegment.MinDuration
            && sel.TimelineEnd - timelinePosition >= TimelineSegment.MinDuration)
        {
            onlyId = sel.Id;
        }

        Pause();
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        var splits = sequence.SplitAllAt(timelinePosition, onlyId);
        if (splits.Count == 0)
        {
            Status.Text = "Posicione a agulha dentro de um trecho desbloqueado para dividir.";
            return;
        }

        history.PushSnapshot(before, beforeSelected);
        selectedSegmentId = splits[0].Right.Id;
        SyncInspectorToCurrentScope();
        RefreshTrimFields();
        UpdateControls();
        Status.Text = splits.Count == 1
            ? $"Trecho dividido em {TrimSelection.Format(timelinePosition, true)}."
            : $"{splits.Count} faixas divididas em {TrimSelection.Format(timelinePosition, true)}.";
    }

    private void DuplicateSegmentClick(object sender, RoutedEventArgs e)
    {
        if (busy || SelectedSegment is not { } seg) return;
        if (sequence.IsTrackLocked(seg.TrackIndex))
        {
            Status.Text = $"Faixa V{seg.TrackIndex + 1} está bloqueada.";
            return;
        }

        Pause();
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        var copy = sequence.DuplicateSegment(seg.Id);
        if (copy is null) return;

        history.PushSnapshot(before, beforeSelected);
        selectedSegmentId = copy.Id;
        SeekTimeline(copy.TimelineStart, autoPlay: false);
        SyncInspectorToCurrentScope();
        RefreshTrimFields();
        UpdateControls();
        Status.Text = $"Clipe \"{copy.SourceName}\" duplicado em V{copy.TrackIndex + 1}.";
    }

    private void ToggleSegmentEnabledClick(object sender, RoutedEventArgs e)
    {
        if (busy || SelectedSegment is not { } seg) return;
        history.PushUndo(sequence, selectedSegmentId);
        seg.Enabled = !seg.Enabled;
        SyncTimelinePlaybackToPlayhead();
        UpdateControls();
        Status.Text = seg.Enabled
            ? $"Clipe \"{seg.SourceName}\" reativado."
            : $"Clipe \"{seg.SourceName}\" será pulado na composição.";
    }

    private void CloseGapsClick(object sender, RoutedEventArgs e)
    {
        if (busy || sequence.IsEmpty) return;
        Pause();
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        var moved = sequence.CloseAllGaps();
        if (moved == 0)
        {
            Status.Text = "Não há espaços vazios para fechar.";
            return;
        }

        history.PushSnapshot(before, beforeSelected);
        SyncAfterSequenceMutation();
        Status.Text = $"Espaços fechados ({moved} clipe(s) alinhado(s)).";
    }

    private void DeleteSegmentClick(object sender, RoutedEventArgs e) =>
        DeleteSelectedSegment(ripple: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);

    private void DeleteSelectedSegment(bool ripple)
    {
        if (busy || SelectedSegment is not { } seg) return;
        if (sequence.IsTrackLocked(seg.TrackIndex))
        {
            Status.Text = $"Faixa V{seg.TrackIndex + 1} está bloqueada.";
            return;
        }

        Pause();
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        var removedName = seg.SourceName;
        if (!sequence.RemoveSegment(seg.Id, ripple)) return;

        history.PushSnapshot(before, beforeSelected);
        selectedSegmentId = null;

        SyncAfterSequenceMutation();
        Status.Text = sequence.IsEmpty
            ? "Timeline vazia."
            : ripple
                ? $"\"{removedName}\" removido e espaço fechado."
                : $"\"{removedName}\" removido da timeline.";
    }

    private void AddTrackClick(object sender, RoutedEventArgs e)
    {
        if (busy || sequence.TrackCount >= TimelineSequence.MaxTracks) return;
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        var added = sequence.AddTrack();
        if (added is null) return;
        history.PushSnapshot(before, beforeSelected);
        activeTrackIndex = added.Index;
        UpdateControls();
        Status.Text = $"Camada {added.Name} adicionada.";
    }

    private void RemoveTrackClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        if (!sequence.RemoveTopEmptyTrack())
        {
            Status.Text = "Só é possível remover a faixa superior quando está vazia (mínimo V1 e V2).";
            return;
        }
        history.PushSnapshot(before, beforeSelected);
        activeTrackIndex = Math.Clamp(activeTrackIndex, 0, sequence.TrackCount - 1);
        UpdateControls();
        Status.Text = "Faixa superior removida.";
    }

    private void UndoClick(object sender, RoutedEventArgs e)
    {
        if (busy || !history.CanUndo) return;
        Pause();
        if (!history.TryUndo(sequence, selectedSegmentId, out var restoredId)) return;
        selectedSegmentId = restoredId;
        SyncAfterSequenceMutation();
        Status.Text = "Ação desfeita.";
    }

    private void RedoClick(object sender, RoutedEventArgs e)
    {
        if (busy || !history.CanRedo) return;
        Pause();
        if (!history.TryRedo(sequence, selectedSegmentId, out var restoredId)) return;
        selectedSegmentId = restoredId;
        SyncAfterSequenceMutation();
        Status.Text = "Ação refeita.";
    }

    private void ToggleSnapClick(object sender, RoutedEventArgs e)
    {
        snapEnabled = !snapEnabled;
        UpdateControls();
        Status.Text = snapEnabled ? "Snap ativado." : "Snap desativado.";
    }

    private void FitTimelineClick(object sender, RoutedEventArgs e) => FitTimelineToViewport(keepAutoFit: false);

    private void FitTimelineToViewport(bool keepAutoFit = false)
    {
        if (!IsInitialized) return;
        var viewportW = TimelineScroll.ViewportWidth > 40 ? TimelineScroll.ViewportWidth : TimelineScroll.ActualWidth;
        if (viewportW <= 40) return;

        var total = Math.Max(1.0, sequence.TotalDuration);
        var usableW = Math.Max(120.0, viewportW - TimelineLeftPadding - 36.0);
        pixelsPerSecond = Math.Clamp(usableW / total, MinPixelsPerSecond, MaxPixelsPerSecond);
        if (!keepAutoFit)
            hasUserZoomed = false;

        SyncZoomSlider();
        DrawTimeline();
        TimelineScroll.ScrollToHorizontalOffset(0);
    }

    private void TimelineZoomInClick(object sender, RoutedEventArgs e)
    {
        hasUserZoomed = true;
        SetPixelsPerSecond(pixelsPerSecond * 1.45, GetPlayheadAnchorRatio());
    }

    private void TimelineZoomOutClick(object sender, RoutedEventArgs e)
    {
        hasUserZoomed = true;
        SetPixelsPerSecond(pixelsPerSecond / 1.45, GetPlayheadAnchorRatio());
    }

    private void TimelineZoomSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || syncingZoomSlider) return;
        hasUserZoomed = true;
        SetPixelsPerSecond(SliderToPps(e.NewValue), GetPlayheadAnchorRatio());
    }

    private double GetPlayheadAnchorRatio()
    {
        var viewportW = Math.Max(1, TimelineScroll.ViewportWidth);
        var playheadScreenX = TimeToX(timelinePosition) - TimelineScroll.HorizontalOffset;
        if (playheadScreenX >= 0 && playheadScreenX <= viewportW)
            return Math.Clamp(playheadScreenX / viewportW, 0.05, 0.95);
        return 0.5;
    }

    private void SetPixelsPerSecond(double newPps, double anchorViewportRatio)
    {
        var clamped = Math.Clamp(newPps, MinPixelsPerSecond, MaxPixelsPerSecond);
        if (Math.Abs(clamped - pixelsPerSecond) < 1e-4) return;

        var viewportW = Math.Max(1, TimelineScroll.ViewportWidth);
        var ratio = Math.Clamp(anchorViewportRatio, 0, 1);
        var anchorScreenX = viewportW * ratio;
        var anchorTime = XToTime(TimelineScroll.HorizontalOffset + anchorScreenX);

        pixelsPerSecond = clamped;
        SyncZoomSlider();
        DrawTimeline();

        var newAnchorX = TimeToX(anchorTime);
        var targetOffset = Math.Clamp(newAnchorX - anchorScreenX, 0, Math.Max(0, TimelineContentHost.Width - viewportW));
        TimelineScroll.ScrollToHorizontalOffset(targetOffset);
        RulerScroll.ScrollToHorizontalOffset(targetOffset);
        DrawRuler(GetContentSpanSeconds());
        UpdateStickyClipLabels();
    }

    private void SyncZoomSlider()
    {
        syncingZoomSlider = true;
        try
        {
            var ratio = Math.Log(Math.Clamp(pixelsPerSecond, MinPixelsPerSecond, MaxPixelsPerSecond) / MinPixelsPerSecond)
                / Math.Log(MaxPixelsPerSecond / MinPixelsPerSecond);
            TimelineZoomSlider.Value = Math.Clamp(ratio * 100.0, 0, 100);
            TimelineZoomSlider.ToolTip = FormattableString.Invariant($"Escala: {pixelsPerSecond:0.##} px/s (Ctrl+Scroll)");
        }
        finally
        {
            syncingZoomSlider = false;
        }
    }

    private static double SliderToPps(double value)
    {
        var t = Math.Clamp(value, 0, 100) / 100.0;
        return MinPixelsPerSecond * Math.Pow(MaxPixelsPerSecond / MinPixelsPerSecond, t);
    }

    private void TimelineMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            hasUserZoomed = true;
            var mouseX = e.GetPosition(TimelineScroll).X;
            var ratio = TimelineScroll.ViewportWidth > 0 ? Math.Clamp(mouseX / TimelineScroll.ViewportWidth, 0, 1) : 0.5;
            var factor = e.Delta > 0 ? 1.25 : 0.8;
            SetPixelsPerSecond(pixelsPerSecond * factor, ratio);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Shift || TimelineScroll.ComputedVerticalScrollBarVisibility != Visibility.Visible)
        {
            var step = e.Delta > 0 ? -96.0 : 96.0;
            TimelineScroll.ScrollToHorizontalOffset(Math.Clamp(TimelineScroll.HorizontalOffset + step, 0, TimelineScroll.ScrollableWidth));
            e.Handled = true;
        }
    }

    private void TimelineScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!IsInitialized) return;
        if (Math.Abs(e.HorizontalChange) > 0.1 || Math.Abs(e.ViewportWidthChange) > 0.1)
        {
            RulerScroll.ScrollToHorizontalOffset(TimelineScroll.HorizontalOffset);
            DrawRuler(GetContentSpanSeconds());
            UpdateStickyClipLabels();
        }
        if (Math.Abs(e.VerticalChange) > 0.1 || Math.Abs(e.ViewportHeightChange) > 0.1)
            TrackHeadersScroll.ScrollToVerticalOffset(TimelineScroll.VerticalOffset);
    }

    private void ResizeTimeline(object sender, SizeChangedEventArgs e) => DrawTimeline();

    private double GetContentSpanSeconds()
    {
        var viewportW = Math.Max(300, TimelineScroll.ViewportWidth > 0 ? TimelineScroll.ViewportWidth : TimelineScroll.ActualWidth);
        var minViewportSeconds = Math.Max(0, (viewportW - TimelineLeftPadding) / Math.Max(MinPixelsPerSecond, pixelsPerSecond));
        return Math.Max(minViewportSeconds, Math.Max(sequence.TotalDuration, timelinePosition) + TimelineRightTailSeconds);
    }

    private void RebuildTrackHeaders()
    {
        TrackHeadersPanel.Children.Clear();
        var borderSubtle = (Brush)FindResource("BorderSubtle");
        var accentBrush = (Brush)FindResource("Accent");
        var textPrimary = (Brush)FindResource("TextPrimary");
        var textSecondary = (Brush)FindResource("TextSecondary");
        var textMuted = (Brush)FindResource("TextMuted");
        var monoFont = (FontFamily)FindResource("MonoFont");
        var iconFont = (FontFamily)FindResource("IconFont");
        var iconButtonStyle = (Style)FindResource("IconButton");
        var activeRowBg = new SolidColorBrush(Color.FromRgb(0x22, 0x20, 0x1B));

        for (var i = sequence.TrackCount - 1; i >= 0; i--)
        {
            var track = sequence.Tracks[i];
            var isTarget = track.Index == activeTrackIndex;

            var row = new Border
            {
                Height = TrackHeight,
                Background = isTarget ? activeRowBg : Brushes.Transparent,
                BorderBrush = borderSubtle,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Cursor = Cursors.Hand,
                ToolTip = $"Faixa {track.Name} (clique para definir como faixa de destino)"
            };
            var capturedIndex = track.Index;
            row.MouseLeftButtonDown += (_, args) =>
            {
                if (args.Source is Button) return;
                activeTrackIndex = capturedIndex;
                UpdateControls();
            };

            var dock = new DockPanel { LastChildFill = true };

            var activeStrip = new Border
            {
                Width = 2.5,
                Background = isTarget ? accentBrush : Brushes.Transparent
            };
            DockPanel.SetDock(activeStrip, Dock.Left);
            dock.Children.Add(activeStrip);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            DockPanel.SetDock(actions, Dock.Right);

            var visBtn = new Button
            {
                Style = iconButtonStyle,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 0, 1, 0),
                IsEnabled = !busy,
                ToolTip = track.Visible ? $"Ocultar {track.Name}" : $"Mostrar {track.Name}",
                Content = new TextBlock
                {
                    FontFamily = iconFont,
                    FontSize = 11,
                    Text = track.Visible ? EyeGlyph : EyeOffGlyph,
                    Foreground = track.Visible ? textSecondary : textMuted
                }
            };
            visBtn.Click += (_, args) =>
            {
                args.Handled = true;
                if (busy) return;
                history.PushUndo(sequence, selectedSegmentId);
                track.Visible = !track.Visible;
                SyncTimelinePlaybackToPlayhead();
                UpdateControls();
                Status.Text = track.Visible ? $"Faixa {track.Name} visível." : $"Faixa {track.Name} oculta.";
            };

            var lockBtn = new Button
            {
                Style = iconButtonStyle,
                Padding = new Thickness(4, 3, 4, 3),
                IsEnabled = !busy,
                ToolTip = track.Locked ? $"Desbloquear {track.Name}" : $"Bloquear edições em {track.Name}",
                Content = new TextBlock
                {
                    FontFamily = iconFont,
                    FontSize = 10.5,
                    Text = track.Locked ? "\uE72E" : "\uE785",
                    Foreground = track.Locked ? accentBrush : textMuted
                }
            };
            lockBtn.Click += (_, args) =>
            {
                args.Handled = true;
                if (busy) return;
                history.PushUndo(sequence, selectedSegmentId);
                track.Locked = !track.Locked;
                UpdateControls();
                Status.Text = track.Locked ? $"Faixa {track.Name} bloqueada." : $"Faixa {track.Name} desbloqueada.";
            };

            actions.Children.Add(visBtn);
            actions.Children.Add(lockBtn);
            dock.Children.Add(actions);

            var label = new TextBlock
            {
                Text = track.Name,
                FontFamily = monoFont,
                FontSize = 11.5,
                FontWeight = isTarget ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = !track.Visible ? textMuted : (isTarget ? accentBrush : textPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            dock.Children.Add(label);

            row.Child = dock;
            TrackHeadersPanel.Children.Add(row);
        }
    }

    private void DrawTimeline()
    {
        if (!IsInitialized) return;

        RebuildTrackHeaders();

        var viewportW = Math.Max(300, TimelineScroll.ViewportWidth > 0 ? TimelineScroll.ViewportWidth : TimelineScroll.ActualWidth);
        var viewportH = Math.Max(TrackHeight * 2, TimelineScroll.ViewportHeight > 0 ? TimelineScroll.ViewportHeight : TimelineScroll.ActualHeight);
        var spanSeconds = GetContentSpanSeconds();
        var contentW = Math.Max(viewportW, Math.Ceiling(TimelineLeftPadding + spanSeconds * pixelsPerSecond));
        var contentH = Math.Max(viewportH, sequence.TrackCount * TrackHeight);

        TimelineContentHost.Width = contentW;
        TimelineContentHost.Height = contentH;
        TimelineTrack.Width = contentW;
        TimelineTrack.Height = contentH;
        RulerCanvas.Width = contentW;

        TrackLanesLayer.Children.Clear();
        SegmentsLayer.Children.Clear();
        stickyClipEntries.Clear();

        var borderSubtle = (Brush)FindResource("BorderSubtle");
        var laneActiveBg = new SolidColorBrush(Color.FromArgb(0x28, 0xE0, 0x9A, 0x24));
        var laneHiddenBg = new SolidColorBrush(Color.FromArgb(0x40, 0x06, 0x06, 0x06));

        for (var i = 0; i < sequence.TrackCount; i++)
        {
            var track = sequence.Tracks[i];
            var y = TrackIndexToY(track.Index);
            var lane = new Border
            {
                Width = contentW,
                Height = TrackHeight,
                BorderBrush = borderSubtle,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = !track.Visible
                    ? laneHiddenBg
                    : track.Index == activeTrackIndex
                        ? laneActiveBg
                        : Brushes.Transparent
            };
            Canvas.SetLeft(lane, 0);
            Canvas.SetTop(lane, y);
            TrackLanesLayer.Children.Add(lane);
        }

        Playhead.Height = contentH;
        DrawRuler(spanSeconds);

        if (sequence.IsEmpty)
        {
            Playhead.Visibility = Visibility.Hidden;
            RulerPlayheadCap.Visibility = Visibility.Hidden;
            SequenceSummary.Text = "Sequência vazia";
            DrawPosition();
            return;
        }

        Playhead.Visibility = Visibility.Visible;
        RulerPlayheadCap.Visibility = Visibility.Visible;

        var surfaceBrush = (Brush)FindResource("SurfaceBg");
        var selectedBg = new SolidColorBrush(Color.FromRgb(0x28, 0x25, 0x1F));
        var borderDefault = (Brush)FindResource("BorderControl");
        var accentBrush = (Brush)FindResource("Accent");
        var textPrimary = (Brush)FindResource("TextPrimary");
        var textSecondary = (Brush)FindResource("TextSecondary");
        var monoFont = (FontFamily)FindResource("MonoFont");
        var cardHeight = TrackHeight - TrackPaddingY * 2;

        for (var i = 0; i < sequence.Count; i++)
        {
            var seg = sequence.Segments[i];
            var track = sequence.Tracks[Math.Clamp(seg.TrackIndex, 0, sequence.TrackCount - 1)];
            var x1 = TimeToX(seg.TimelineStart);
            var x2 = TimeToX(seg.TimelineEnd);
            var blockWidth = Math.Max(6, x2 - x1);
            var y = TrackIndexToY(seg.TrackIndex) + TrackPaddingY;
            var isSelected = selectedSegmentId == seg.Id;
            var isVisuallyActive = seg.Enabled && track.Visible;

            var color = SourceAccentPalette[Math.Abs(seg.ColorIndex) % SourceAccentPalette.Length];
            var stripeBrush = new SolidColorBrush(color);

            var card = new Border
            {
                Width = blockWidth,
                Height = cardHeight,
                CornerRadius = new CornerRadius(3.5),
                Background = isSelected ? selectedBg : surfaceBrush,
                BorderBrush = isSelected ? accentBrush : borderDefault,
                BorderThickness = new Thickness(isSelected ? 1.5 : 1),
                Opacity = isVisuallyActive ? (track.Locked ? 0.8 : 1.0) : 0.35,
                ClipToBounds = true,
                ToolTip = $"{track.Name} · {seg.SourceName}\n"
                    + $"Timeline: {TrimSelection.Format(seg.TimelineStart, true)} – {TrimSelection.Format(seg.TimelineEnd, true)}\n"
                    + $"Fonte: {TrimSelection.Format(seg.SourceStart, true)} – {TrimSelection.Format(seg.SourceEnd, true)} ({TrimSelection.Format(seg.Duration, true)})"
                    + (track.Locked ? "\n[Faixa bloqueada]" : "")
                    + (!track.Visible ? "\n[Faixa oculta]" : "")
                    + (!seg.Enabled ? "\n[Trecho pulado]" : "")
                    + (string.IsNullOrEmpty(seg.NrBadge) ? "\nNR: Global" : $"\nNR personalizado: {seg.NrBadge}")
            };

            var cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var colorBar = new Border { Background = stripeBrush };
            Grid.SetRow(colorBar, 0);
            cardGrid.Children.Add(colorBar);

            if (blockWidth >= 24)
            {
                var contentDock = new DockPanel
                {
                    Margin = new Thickness(isSelected && blockWidth >= 46 ? 10 : 6, 2, isSelected && blockWidth >= 46 ? 10 : 6, 2),
                    LastChildFill = true
                };
                Grid.SetRow(contentDock, 1);

                if (!string.IsNullOrEmpty(seg.NrBadge) && blockWidth >= 82)
                {
                    var badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0x9A, 0x24)),
                        BorderBrush = accentBrush,
                        BorderThickness = new Thickness(0.8),
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(4, 0.5, 4, 0.5),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 0, 0),
                        Child = new TextBlock
                        {
                            Text = seg.NrBadge,
                            FontFamily = monoFont,
                            FontSize = 8.5,
                            Foreground = accentBrush
                        }
                    };
                    DockPanel.SetDock(badge, Dock.Right);
                    contentDock.Children.Add(badge);
                }

                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                labels.Children.Add(new TextBlock
                {
                    Text = seg.SourceName,
                    FontSize = 11,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = textPrimary,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                if (blockWidth >= 52)
                {
                    var sub = !seg.Enabled
                        ? $"Pulado · {TrimSelection.Format(seg.Duration)}"
                        : TrimSelection.Format(seg.Duration, true);
                    labels.Children.Add(new TextBlock
                    {
                        Text = sub,
                        FontFamily = monoFont,
                        FontSize = 9.5,
                        Foreground = textSecondary,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(0, 0.5, 0, 0)
                    });
                }

                contentDock.Children.Add(labels);
                cardGrid.Children.Add(contentDock);
                stickyClipEntries.Add((labels, x1, blockWidth));
            }

            // Visible trim edge grips
            if (!busy && !track.Locked && blockWidth >= 20)
            {
                var gripOpacity = isSelected ? 0.95 : 0.35;
                var leftGrip = CreateEdgeGrip(HorizontalAlignment.Left, isSelected ? accentBrush : textSecondary, gripOpacity);
                var rightGrip = CreateEdgeGrip(HorizontalAlignment.Right, isSelected ? accentBrush : textSecondary, gripOpacity);
                Grid.SetRowSpan(leftGrip, 2);
                Grid.SetRowSpan(rightGrip, 2);
                cardGrid.Children.Add(leftGrip);
                cardGrid.Children.Add(rightGrip);
            }

            card.Child = cardGrid;
            Canvas.SetLeft(card, x1);
            Canvas.SetTop(card, y);
            SegmentsLayer.Children.Add(card);
        }

        var activeSegs = sequence.EnabledCount;
        SequenceSummary.Text = activeSegs == sequence.Count
            ? $"{sequence.Count} {(sequence.Count == 1 ? "clipe" : "clipes")} · {sequence.TrackCount} faixas · {TrimSelection.Format(sequence.ActiveDuration, true)}"
            : $"{activeSegs}/{sequence.Count} ativos · {sequence.TrackCount} faixas · {TrimSelection.Format(sequence.ActiveDuration, true)}";

        UpdateStickyClipLabels();
        DrawPosition();
    }

    private void UpdateStickyClipLabels()
    {
        if (stickyClipEntries.Count == 0) return;
        var scrollX = TimelineScroll.HorizontalOffset;
        foreach (var (element, cardLeft, cardWidth) in stickyClipEntries)
        {
            if (scrollX > cardLeft + 6 && cardWidth >= 90)
            {
                var maxShift = Math.Max(0, cardWidth - 88);
                var shift = Math.Clamp(scrollX - cardLeft + 4, 0, maxShift);
                element.Margin = new Thickness(shift, 0, 0, 0);
            }
            else if (element.Margin.Left > 0)
            {
                element.Margin = new Thickness(0);
            }
        }
    }

    private static Border CreateEdgeGrip(HorizontalAlignment alignment, Brush fill, double opacity) => new()
    {
        Width = 4.5,
        Opacity = opacity,
        HorizontalAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Stretch,
        Background = fill,
        IsHitTestVisible = false,
        Child = new Border
        {
            Width = 1,
            Height = 12,
            CornerRadius = new CornerRadius(0.5),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private void DrawRuler(double spanSeconds)
    {
        RulerTicksLayer.Children.Clear();
        var viewportW = Math.Max(300, TimelineScroll.ViewportWidth > 0 ? TimelineScroll.ViewportWidth : TimelineScroll.ActualWidth);
        if (spanSeconds <= 0 || pixelsPerSecond <= 0) return;

        var step = RulerSteps[^1];
        foreach (var candidate in RulerSteps)
        {
            if (candidate * pixelsPerSecond >= 84.0)
            {
                step = candidate;
                break;
            }
        }

        var tickBrush = (Brush)FindResource("BorderControl");
        var labelBrush = (Brush)FindResource("TextMuted");
        var monoFont = (FontFamily)FindResource("MonoFont");
        var minorStep = step / 4.0;

        var scrollX = TimelineScroll.HorizontalOffset;
        var visMin = Math.Max(0.0, XToTime(scrollX - 100));
        var visMax = Math.Min(spanSeconds, XToTime(scrollX + viewportW + 160));
        var kStart = Math.Max(0L, (long)Math.Floor(visMin / minorStep));
        var kEnd = Math.Max(kStart, (long)Math.Ceiling(visMax / minorStep));

        for (var k = kStart; k <= kEnd; k++)
        {
            var t = k * minorStep;
            var x = TimeToX(t);
            var isMajor = k % 4 == 0;

            var tick = new Border
            {
                Width = 1,
                Height = isMajor ? 7 : 3,
                Background = tickBrush
            };
            Canvas.SetLeft(tick, x);
            Canvas.SetBottom(tick, 0);
            RulerTicksLayer.Children.Add(tick);

            if (isMajor && x + 56 <= RulerCanvas.Width)
            {
                var label = new TextBlock
                {
                    Text = TrimSelection.Format(t),
                    FontFamily = monoFont,
                    FontSize = 9.5,
                    Foreground = labelBrush
                };
                Canvas.SetLeft(label, x + 3);
                Canvas.SetTop(label, 2);
                RulerTicksLayer.Children.Add(label);
            }
        }
    }

    private void DrawPosition()
    {
        var total = sequence.TotalDuration;
        var x = TimeToX(timelinePosition);
        Canvas.SetLeft(Playhead, x - 5.5);
        Canvas.SetLeft(RulerPlayheadCap, x - 5.5);
        TimeLabel.Text = $"{TrimSelection.Format(timelinePosition, true)} / {TrimSelection.Format(total, true)}";
        if (IsInitialized)
        {
            SplitButton.IsEnabled = !busy && CanSplitAt(timelinePosition);
            var hasEngine = !busy && adapterPath is not null && EnginePath is not null;
            FramePrintButton.IsEnabled = hasEngine && (sequence.LocateCompositionAt(timelinePosition) is not null || clip is not null);
        }
    }

    private void RefreshTrimFields()
    {
        var seg = SelectedSegment;
        refreshingFields = true;
        if (seg is not null)
        {
            StartTime.Text = TrimSelection.Format(seg.SourceStart, true);
            EndTime.Text = TrimSelection.Format(seg.SourceEnd, true);
            TrimLabel.Text = $"V{seg.TrackIndex + 1} · {TrimSelection.Format(seg.Duration, true)}";
        }
        else
        {
            StartTime.Text = TrimSelection.Format(0, true);
            EndTime.Text = TrimSelection.Format(0, true);
            TrimLabel.Text = "";
        }
        refreshingFields = false;
    }

    // Ruler scrub
    private void ScrubDown(object sender, MouseButtonEventArgs e)
    {
        if (sequence.TotalDuration <= 0 || !RulerCanvas.CaptureMouse()) return;
        scrubbing = true;
        Pause();
        var raw = XToTime(e.GetPosition(RulerCanvas).X);
        var snapped = snapEnabled ? sequence.SnapTime(raw, SnapThresholdSeconds()) : raw;
        SeekTimeline(snapped, autoPlay: false);
        e.Handled = true;
    }

    private void ScrubMove(object sender, MouseEventArgs e)
    {
        if (!scrubbing || e.LeftButton != MouseButtonState.Pressed) return;
        var raw = XToTime(e.GetPosition(RulerCanvas).X);
        var snapped = snapEnabled ? sequence.SnapTime(raw, SnapThresholdSeconds()) : raw;
        SeekTimeline(snapped, autoPlay: false);
    }

    private void ScrubUp(object sender, MouseButtonEventArgs e)
    {
        if (!scrubbing) return;
        var raw = XToTime(e.GetPosition(RulerCanvas).X);
        var snapped = snapEnabled ? sequence.SnapTime(raw, SnapThresholdSeconds()) : raw;
        SeekTimeline(snapped, autoPlay: false);
        RulerCanvas.ReleaseMouseCapture();
        scrubbing = false;
        e.Handled = true;
    }

    private void ScrubLost(object sender, MouseEventArgs e) => scrubbing = false;

    // Middle mouse pan in PreviewMouse handlers
    private void TrackPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        trackDragMode = TrackDragMode.Pan;
        trackDragOrigin = e.GetPosition(TimelineScroll);
        dragStartPanH = TimelineScroll.HorizontalOffset;
        dragStartPanV = TimelineScroll.VerticalOffset;
        TimelineTrack.Cursor = Cursors.ScrollAll;
        TimelineTrack.CaptureMouse();
        e.Handled = true;
    }

    private void TrackPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (trackDragMode != TrackDragMode.Pan) return;
        var cur = e.GetPosition(TimelineScroll);
        var delta = cur - trackDragOrigin;
        TimelineScroll.ScrollToHorizontalOffset(Math.Clamp(dragStartPanH - delta.X, 0, TimelineScroll.ScrollableWidth));
        TimelineScroll.ScrollToVerticalOffset(Math.Clamp(dragStartPanV - delta.Y, 0, TimelineScroll.ScrollableHeight));
        e.Handled = true;
    }

    private void TrackPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (trackDragMode != TrackDragMode.Pan || e.ChangedButton != MouseButton.Middle) return;
        trackDragMode = TrackDragMode.None;
        TimelineTrack.Cursor = Cursors.Arrow;
        if (TimelineTrack.IsMouseCaptured)
            TimelineTrack.ReleaseMouseCapture();
        e.Handled = true;
    }

    private (TimelineSegment? Segment, TrackDragMode HitKind) HitTestTrackPoint(Point pt)
    {
        var trackIndex = YToTrackIndex(pt.Y);
        var locked = sequence.IsTrackLocked(trackIndex);

        for (var i = sequence.Count - 1; i >= 0; i--)
        {
            var seg = sequence.Segments[i];
            if (seg.TrackIndex != trackIndex) continue;

            var x1 = TimeToX(seg.TimelineStart);
            var x2 = Math.Max(x1 + 6, TimeToX(seg.TimelineEnd));
            var width = x2 - x1;

            if (pt.X < x1 - EdgeHitPixels * 0.5 || pt.X > x2 + EdgeHitPixels * 0.5)
                continue;

            if (!locked && !busy)
            {
                var innerEdge = width >= 22 ? 9.0 : Math.Min(4.0, width * 0.25);
                if (Math.Abs(pt.X - x1) <= innerEdge || (pt.X < x1 && x1 - pt.X <= 6))
                    return (seg, TrackDragMode.TrimLeft);
                if (Math.Abs(pt.X - x2) <= innerEdge || (pt.X > x2 && pt.X - x2 <= 6))
                    return (seg, TrackDragMode.TrimRight);
            }

            if (pt.X >= x1 && pt.X <= x2)
                return (seg, TrackDragMode.MoveSegment);
        }

        return (null, TrackDragMode.Scrub);
    }

    private void TrackMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (trackDragMode == TrackDragMode.Pan || sequence.IsEmpty) return;

        var pt = e.GetPosition(TimelineTrack);
        var trackIndex = YToTrackIndex(pt.Y);
        activeTrackIndex = trackIndex;

        Pause();
        trackDragOrigin = pt;
        dragUndoPushed = false;
        dragMutated = false;

        var (seg, hitKind) = HitTestTrackPoint(pt);
        if (seg is not null)
        {
            selectedSegmentId = seg.Id;
            dragSegmentId = seg.Id;
            dragInitialTimelineStart = seg.TimelineStart;
            dragInitialSourceStart = seg.SourceStart;
            dragInitialSourceEnd = seg.SourceEnd;
            dragInitialPlayhead = timelinePosition;
            dragInitialTrackIndex = seg.TrackIndex;
            trackDragMode = hitKind == TrackDragMode.MoveSegment && (busy || sequence.IsTrackLocked(seg.TrackIndex))
                ? TrackDragMode.Scrub
                : hitKind;

            if (trackDragMode == TrackDragMode.TrimLeft)
                SeekTimeline(seg.TimelineStart, autoPlay: false);
            else if (trackDragMode == TrackDragMode.TrimRight)
                SeekTimeline(seg.TimelineEnd, autoPlay: false);

            SyncInspectorToCurrentScope();
            RefreshTrimFields();
            UpdateControls();
        }
        else
        {
            dragSegmentId = Guid.Empty;
            trackDragMode = TrackDragMode.Scrub;
            var rawT = XToTime(pt.X);
            var snapped = snapEnabled ? sequence.SnapTime(rawT, SnapThresholdSeconds()) : rawT;
            SeekTimeline(snapped, autoPlay: false);
            UpdateControls();
        }

        TimelineTrack.CaptureMouse();
        e.Handled = true;
    }

    private void TrackMouseMove(object sender, MouseEventArgs e)
    {
        if (trackDragMode == TrackDragMode.Pan) return;

        var pt = e.GetPosition(TimelineTrack);

        if (trackDragMode == TrackDragMode.None)
        {
            UpdateTrackHoverFeedback(pt);
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        if (trackDragMode == TrackDragMode.Scrub)
        {
            var rawT = XToTime(pt.X);
            var snapped = snapEnabled ? sequence.SnapTime(rawT, SnapThresholdSeconds()) : rawT;
            ShowSnapGuideIfMatched(rawT, snapped);
            SeekTimeline(snapped, autoPlay: false);
            return;
        }

        var seg = sequence.FindById(dragSegmentId);
        if (seg is null) return;

        var deltaX = pt.X - trackDragOrigin.X;
        var rawDeltaSeconds = deltaX / Math.Max(MinPixelsPerSecond, pixelsPerSecond);

        if (trackDragMode is TrackDragMode.TrimLeft or TrackDragMode.TrimRight)
        {
            var isStart = trackDragMode == TrackDragMode.TrimLeft;
            if (!dragUndoPushed && Math.Abs(deltaX) <= 2)
                return;

            var targetTimeRaw = isStart
                ? dragInitialTimelineStart + rawDeltaSeconds
                : dragInitialTimelineStart + (dragInitialSourceEnd - dragInitialSourceStart) + rawDeltaSeconds;
            var targetTime = snapEnabled
                ? sequence.SnapTime(targetTimeRaw, SnapThresholdSeconds(), dragInitialPlayhead, seg.Id)
                : targetTimeRaw;
            ShowSnapGuideIfMatched(targetTimeRaw, targetTime);

            var currentEdgeTimeline = isStart ? seg.TimelineStart : seg.TimelineEnd;
            var neededDelta = targetTime - currentEdgeTimeline;
            var beforeSnap = !dragUndoPushed ? sequence.CaptureSnapshot() : null;
            if (sequence.TrimEdge(seg.Id, isStart, neededDelta))
            {
                if (!dragUndoPushed && beforeSnap is not null)
                {
                    history.PushSnapshot(beforeSnap, selectedSegmentId);
                    dragUndoPushed = true;
                }
                dragMutated = true;
                var newPlayhead = isStart ? seg.TimelineStart : seg.TimelineEnd;
                timelinePosition = newPlayhead;
                LoadMediaForPath(seg.SourcePath, isStart ? seg.SourceStart : seg.SourceEnd, autoPlay: false);
                RefreshTrimFields();
                UpdateControls();
            }

            ShowDragTooltip(
                pt,
                isStart
                    ? $"In {TrimSelection.Format(seg.SourceStart, true)} · Dur {TrimSelection.Format(seg.Duration, true)}"
                    : $"Out {TrimSelection.Format(seg.SourceEnd, true)} · Dur {TrimSelection.Format(seg.Duration, true)}");
            return;
        }

        if (trackDragMode == TrackDragMode.MoveSegment)
        {
            if (Math.Abs(deltaX) < 4 && Math.Abs(pt.Y - trackDragOrigin.Y) < 4 && !dragUndoPushed)
                return;

            var desiredStartRaw = Math.Max(0, dragInitialTimelineStart + rawDeltaSeconds);
            var desiredStart = desiredStartRaw;
            if (snapEnabled)
            {
                var snapStart = sequence.SnapTime(desiredStartRaw, SnapThresholdSeconds(), dragInitialPlayhead, seg.Id);
                var snapEnd = sequence.SnapTime(desiredStartRaw + seg.Duration, SnapThresholdSeconds(), dragInitialPlayhead, seg.Id);
                if (Math.Abs(snapStart - desiredStartRaw) <= Math.Abs(snapEnd - (desiredStartRaw + seg.Duration)))
                {
                    desiredStart = snapStart;
                    ShowSnapGuideIfMatched(desiredStartRaw, snapStart);
                }
                else
                {
                    desiredStart = Math.Max(0, snapEnd - seg.Duration);
                    ShowSnapGuideIfMatched(desiredStartRaw + seg.Duration, snapEnd);
                }
            }
            else
            {
                SnapGuideLine.Visibility = Visibility.Collapsed;
            }

            var targetTrack = YToTrackIndex(pt.Y);
            var beforeSnap = !dragUndoPushed ? sequence.CaptureSnapshot() : null;
            if (sequence.MoveSegmentTo(seg.Id, desiredStart, targetTrack))
            {
                if (!dragUndoPushed && beforeSnap is not null)
                {
                    history.PushSnapshot(beforeSnap, selectedSegmentId);
                    dragUndoPushed = true;
                }
                dragMutated = true;
                activeTrackIndex = seg.TrackIndex;
                RefreshTrimFields();
                UpdateControls();
            }

            ShowDragTooltip(pt, $"V{seg.TrackIndex + 1} · {TrimSelection.Format(seg.TimelineStart, true)}");
        }
    }

    private void TrackMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (trackDragMode == TrackDragMode.Pan || trackDragMode == TrackDragMode.None) return;

        // Pure click on a clip body without dragging seeks playhead cleanly
        if (trackDragMode == TrackDragMode.MoveSegment && !dragMutated)
        {
            var rawT = XToTime(e.GetPosition(TimelineTrack).X);
            var snapped = snapEnabled ? sequence.SnapTime(rawT, SnapThresholdSeconds()) : rawT;
            SeekTimeline(snapped, autoPlay: false);
        }
        else if (dragMutated && trackDragMode is TrackDragMode.TrimLeft or TrackDragMode.TrimRight or TrackDragMode.MoveSegment)
        {
            SyncTimelinePlaybackToPlayhead();
        }

        ClearDragVisuals();
        trackDragMode = TrackDragMode.None;
        dragSegmentId = Guid.Empty;
        dragUndoPushed = false;
        dragMutated = false;
        if (TimelineTrack.IsMouseCaptured)
            TimelineTrack.ReleaseMouseCapture();
        UpdateControls();
        e.Handled = true;
    }

    private void TrackLostCapture(object sender, MouseEventArgs e)
    {
        ClearDragVisuals();
        trackDragMode = TrackDragMode.None;
        dragSegmentId = Guid.Empty;
        dragUndoPushed = false;
        dragMutated = false;
    }

    private void ClearDragVisuals()
    {
        SnapGuideLine.Visibility = Visibility.Collapsed;
        DragTooltip.Visibility = Visibility.Collapsed;
        DropSlotHighlight.Visibility = Visibility.Collapsed;
    }

    private void UpdateTrackHoverFeedback(Point pt)
    {
        var (seg, hitKind) = HitTestTrackPoint(pt);
        if (seg is null || busy || sequence.IsTrackLocked(seg.TrackIndex))
        {
            TimelineTrack.Cursor = Cursors.Arrow;
            return;
        }

        TimelineTrack.Cursor = hitKind switch
        {
            TrackDragMode.TrimLeft or TrackDragMode.TrimRight => Cursors.SizeWE,
            TrackDragMode.MoveSegment => Cursors.Hand,
            _ => Cursors.Arrow
        };
    }

    private void ShowSnapGuideIfMatched(double rawSeconds, double snappedSeconds)
    {
        if (snapEnabled && Math.Abs(rawSeconds - snappedSeconds) > 1e-5)
        {
            SnapGuideLine.Visibility = Visibility.Visible;
            SnapGuideLine.Height = TimelineTrack.Height;
            Canvas.SetLeft(SnapGuideLine, TimeToX(snappedSeconds));
            Canvas.SetTop(SnapGuideLine, 0);
        }
        else
        {
            SnapGuideLine.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowDragTooltip(Point pt, string text)
    {
        DragTooltipText.Text = text;
        DragTooltip.Visibility = Visibility.Visible;
        var left = Math.Max(4, pt.X + 12);
        var top = Math.Max(4, pt.Y - 26);
        Canvas.SetLeft(DragTooltip, left);
        Canvas.SetTop(DragTooltip, top);
    }

    // Drag & Drop from Media Library or Explorer onto specific Track & Time
    private void TimelineDragOver(object sender, DragEventArgs e)
    {
        if (busy)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent(DragClipDataFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            var pt = e.GetPosition(TimelineTrack);
            var targetTrack = YToTrackIndex(pt.Y);
            var rawT = XToTime(pt.X);
            var dropT = snapEnabled ? sequence.SnapTime(rawT, SnapThresholdSeconds(), timelinePosition) : rawT;

            var previewWidth = 64.0;
            if (e.Data.GetData(DragClipDataFormat) is string clipPath
                && FindClipByPath(clipPath) is { Duration: > 0 } c)
            {
                previewWidth = Math.Max(16, c.Duration * pixelsPerSecond);
            }

            DropSlotHighlight.Visibility = Visibility.Visible;
            DropSlotHighlight.Width = previewWidth;
            DropSlotHighlight.Height = TrackHeight - TrackPaddingY * 2;
            Canvas.SetLeft(DropSlotHighlight, TimeToX(dropT));
            Canvas.SetTop(DropSlotHighlight, TrackIndexToY(targetTrack) + TrackPaddingY);
            e.Handled = true;
        }
    }

    private void TimelineDragLeave(object sender, DragEventArgs e)
    {
        DropSlotHighlight.Visibility = Visibility.Collapsed;
        SnapGuideLine.Visibility = Visibility.Collapsed;
    }

    private void TimelineDrop(object sender, DragEventArgs e)
    {
        DropSlotHighlight.Visibility = Visibility.Collapsed;
        if (busy) return;

        var pt = e.GetPosition(TimelineTrack);
        var targetTrack = YToTrackIndex(pt.Y);
        var rawT = XToTime(pt.X);
        var dropT = snapEnabled ? sequence.SnapTime(rawT, SnapThresholdSeconds(), timelinePosition) : rawT;

        if (e.Data.GetData(DragClipDataFormat) is string clipPath)
        {
            if (FindClipByPath(clipPath) is { } item)
                AppendClipToTimeline(item, targetTrack, dropT);
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddPaths(files, filterExtensions: true, targetTrackIndex: targetTrack, dropTimelineSeconds: dropT);
            e.Handled = true;
        }
    }

    // Precision In / Out timecode fields for selected segment (respects fixed scale)
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
        if (refreshingFields || busy || SelectedSegment is not { } seg) return;

        var isStart = Equals(field.Tag, "start");
        var priorValue = isStart ? seg.SourceStart : seg.SourceEnd;
        if (field.Text == TrimSelection.Format(priorValue, true)) return;
        if (!TrimSelection.TryParse(field.Text, out var time))
        {
            Status.Text = "Digite um tempo no formato HH:MM:SS.mmm.";
            RefreshTrimFields();
            return;
        }

        Pause();
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        if (sequence.SetSourceEdge(seg.Id, isStart, time))
        {
            history.PushSnapshot(before, beforeSelected);
            SeekTimeline(isStart ? seg.TimelineStart : seg.TimelineEnd, autoPlay: false);
            RefreshTrimFields();
            UpdateControls();
        }
        else
        {
            RefreshTrimFields();
        }
    }

    private void MarkStartClick(object sender, RoutedEventArgs e)
    {
        if (!ready || busy || SelectedSegment is not { } seg) return;
        var delta = timelinePosition - seg.TimelineStart;
        if (Math.Abs(delta) < 1e-4) return;

        Pause();
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        if (sequence.TrimEdge(seg.Id, isStart: true, delta))
        {
            history.PushSnapshot(before, beforeSelected);
            SeekTimeline(seg.TimelineStart, autoPlay: false);
            RefreshTrimFields();
            UpdateControls();
        }
    }

    private void MarkEndClick(object sender, RoutedEventArgs e)
    {
        if (!ready || busy || SelectedSegment is not { } seg) return;
        var delta = timelinePosition - seg.TimelineEnd;
        if (Math.Abs(delta) < 1e-4) return;

        Pause();
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        if (sequence.TrimEdge(seg.Id, isStart: false, delta))
        {
            history.PushSnapshot(before, beforeSelected);
            SeekTimeline(seg.TimelineEnd, autoPlay: false);
            RefreshTrimFields();
            UpdateControls();
        }
    }

    private void ResetTrim(object sender, RoutedEventArgs e)
    {
        if (busy || SelectedSegment is not { SourceDuration: > 0 } seg) return;
        if (sequence.IsTrackLocked(seg.TrackIndex))
        {
            Status.Text = $"Faixa V{seg.TrackIndex + 1} está bloqueada.";
            return;
        }

        Pause();
        var before = sequence.CaptureSnapshot();
        var beforeSelected = selectedSegmentId;
        var changed = false;
        changed |= sequence.SetSourceEdge(seg.Id, isStart: true, 0);
        changed |= sequence.SetSourceEdge(seg.Id, isStart: false, seg.SourceDuration);
        if (!changed)
            return;

        history.PushSnapshot(before, beforeSelected);
        SeekTimeline(seg.TimelineStart, autoPlay: false);
        RefreshTrimFields();
        UpdateControls();
        Status.Text = $"Duração máxima disponível restaurada para \"{seg.SourceName}\".";
    }
}
