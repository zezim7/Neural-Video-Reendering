namespace VideoNR.Models;

internal readonly record struct CompositionSlice(
    TimelineSegment Segment,
    double TimelineStart,
    double TimelineEnd,
    double SourceStart,
    double SourceEnd)
{
    public double Duration => Math.Max(0, TimelineEnd - TimelineStart);
}

internal readonly record struct TimelineHit(
    TimelineSegment Segment,
    int Index,
    double SegmentTimelineStart,
    double SegmentTimelineEnd,
    double SourceTime);

internal sealed record TimelineStateSnapshot(
    IReadOnlyList<TimelineSegment> Segments,
    IReadOnlyList<TimelineTrackInfo> Tracks);

internal sealed class TimelineSequence
{
    internal const int MaxTracks = 6;

    private readonly List<TimelineSegment> segments = [];
    private readonly List<TimelineTrackInfo> tracks =
    [
        new TimelineTrackInfo(0),
        new TimelineTrackInfo(1)
    ];

    public IReadOnlyList<TimelineSegment> Segments => segments;
    public IReadOnlyList<TimelineTrackInfo> Tracks => tracks;
    public int Count => segments.Count;
    public int TrackCount => tracks.Count;
    public bool IsEmpty => segments.Count == 0;

    public double TotalDuration
    {
        get
        {
            var maxEnd = 0.0;
            for (var i = 0; i < segments.Count; i++)
            {
                if (segments[i].TimelineEnd > maxEnd)
                    maxEnd = segments[i].TimelineEnd;
            }
            return maxEnd;
        }
    }

    public double ActiveDuration
    {
        get
        {
            var slices = ResolveComposition();
            var sum = 0.0;
            for (var i = 0; i < slices.Count; i++)
                sum += slices[i].Duration;
            return sum;
        }
    }

    public int EnabledCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < segments.Count; i++)
            {
                if (IsSegmentActive(segments[i])) count++;
            }
            return count;
        }
    }

    public bool IsTrackVisible(int trackIndex) =>
        trackIndex >= 0 && trackIndex < tracks.Count && tracks[trackIndex].Visible;

    public bool IsTrackLocked(int trackIndex) =>
        trackIndex >= 0 && trackIndex < tracks.Count && tracks[trackIndex].Locked;

    public bool IsSegmentActive(TimelineSegment segment) =>
        segment.Enabled && segment.Duration > 0 && IsTrackVisible(segment.TrackIndex);

    public TimelineTrackInfo EnsureTrack(int trackIndex)
    {
        var target = Math.Clamp(trackIndex, 0, MaxTracks - 1);
        while (tracks.Count <= target)
            tracks.Add(new TimelineTrackInfo(tracks.Count));
        return tracks[target];
    }

    public TimelineTrackInfo? AddTrack()
    {
        if (tracks.Count >= MaxTracks) return null;
        var added = new TimelineTrackInfo(tracks.Count);
        tracks.Add(added);
        return added;
    }

    public bool RemoveTopEmptyTrack()
    {
        if (tracks.Count <= 2) return false;
        var topIdx = tracks.Count - 1;
        if (segments.Any(s => s.TrackIndex == topIdx)) return false;
        tracks.RemoveAt(topIdx);
        return true;
    }

    public int IndexOf(Guid id)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Id == id) return i;
        }
        return -1;
    }

    public TimelineSegment? FindById(Guid id)
    {
        var idx = IndexOf(id);
        return idx >= 0 ? segments[idx] : null;
    }

    public double GetTrackEnd(int trackIndex)
    {
        var maxEnd = 0.0;
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].TrackIndex == trackIndex && segments[i].TimelineEnd > maxEnd)
                maxEnd = segments[i].TimelineEnd;
        }
        return maxEnd;
    }

    public TimelineSegment AppendClip(
        string sourcePath,
        double sourceDuration,
        TrimSelection? initialTrim = null,
        int colorIndex = 0,
        int trackIndex = 0)
    {
        var clampedTrack = Math.Clamp(trackIndex, 0, MaxTracks - 1);
        EnsureTrack(clampedTrack);
        var startTime = GetTrackEnd(clampedTrack);
        return AddClipAt(sourcePath, sourceDuration, startTime, clampedTrack, initialTrim, colorIndex);
    }

    public TimelineSegment AddClipAt(
        string sourcePath,
        double sourceDuration,
        double timelineStart,
        int trackIndex,
        TrimSelection? initialTrim = null,
        int colorIndex = 0)
    {
        var clampedTrack = Math.Clamp(trackIndex, 0, MaxTracks - 1);
        EnsureTrack(clampedTrack);

        var srcStart = initialTrim is { } t ? t.Start : 0;
        var srcEnd = initialTrim is { } t2 && sourceDuration > 0 ? t2.EndAt(sourceDuration) : sourceDuration;
        var safeTimelineStart = double.IsFinite(timelineStart) && timelineStart > 0 ? timelineStart : 0;
        var provisionalDur = Math.Max(0, srcEnd - srcStart);
        var resolvedStart = FindNonOverlappingStart(clampedTrack, safeTimelineStart, provisionalDur, ignoreSegmentId: null);

        var segment = new TimelineSegment(
            sourcePath,
            sourceDuration,
            srcStart,
            srcEnd,
            colorIndex,
            enabled: true,
            useCustomNr: false,
            customNr: null,
            id: null,
            timelineStart: resolvedStart,
            trackIndex: clampedTrack);

        segments.Add(segment);
        return segment;
    }

    public TimelineSegment? FindSegmentAt(double timelineSeconds, int trackIndex)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (s.TrackIndex == trackIndex && timelineSeconds >= s.TimelineStart - 1e-4 && timelineSeconds <= s.TimelineEnd + 1e-4)
                return s;
        }
        return null;
    }

    /// <summary>
    /// Resolves all visible, enabled segments across tracks (higher TrackIndex overrides lower TrackIndex)
    /// into an ordered list of non-overlapping playback/export slices.
    /// </summary>
    public List<CompositionSlice> ResolveComposition()
    {
        var active = new List<TimelineSegment>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            if (IsSegmentActive(segments[i]))
                active.Add(segments[i]);
        }

        if (active.Count == 0) return [];

        var cuts = new List<double>(active.Count * 2);
        for (var i = 0; i < active.Count; i++)
        {
            cuts.Add(active[i].TimelineStart);
            cuts.Add(active[i].TimelineEnd);
        }
        cuts.Sort();

        var uniqueCuts = new List<double>(cuts.Count);
        for (var i = 0; i < cuts.Count; i++)
        {
            if (uniqueCuts.Count == 0 || cuts[i] - uniqueCuts[^1] > 1e-5)
                uniqueCuts.Add(cuts[i]);
        }

        var result = new List<CompositionSlice>();
        for (var i = 0; i < uniqueCuts.Count - 1; i++)
        {
            var t0 = uniqueCuts[i];
            var t1 = uniqueCuts[i + 1];
            if (t1 - t0 < 1e-4) continue;
            var mid = (t0 + t1) * 0.5;

            TimelineSegment? winner = null;
            for (var j = 0; j < active.Count; j++)
            {
                var candidate = active[j];
                if (mid >= candidate.TimelineStart && mid <= candidate.TimelineEnd)
                {
                    if (winner is null || candidate.TrackIndex > winner.TrackIndex)
                        winner = candidate;
                }
            }

            if (winner is null) continue;

            var srcStart = winner.SourceStart + (t0 - winner.TimelineStart);
            var srcEnd = winner.SourceStart + (t1 - winner.TimelineStart);

            if (result.Count > 0
                && result[^1].Segment.Id == winner.Id
                && Math.Abs(result[^1].TimelineEnd - t0) < 1e-4
                && Math.Abs(result[^1].SourceEnd - srcStart) < 1e-4)
            {
                var prev = result[^1];
                result[^1] = prev with { TimelineEnd = t1, SourceEnd = srcEnd };
            }
            else
            {
                result.Add(new CompositionSlice(winner, t0, t1, srcStart, srcEnd));
            }
        }

        return result;
    }

    public CompositionSlice? LocateCompositionAt(double timelineSeconds)
    {
        var slices = ResolveComposition();
        if (slices.Count == 0) return null;
        for (var i = 0; i < slices.Count; i++)
        {
            var s = slices[i];
            if (timelineSeconds >= s.TimelineStart - 1e-4 && timelineSeconds < s.TimelineEnd - 1e-5)
                return s;
        }

        var last = slices[^1];
        if (Math.Abs(timelineSeconds - last.TimelineEnd) <= 1e-3)
            return last;
        return null;
    }

    public CompositionSlice? FindNextCompositionSliceAfter(double timelineSeconds)
    {
        var slices = ResolveComposition();
        for (var i = 0; i < slices.Count; i++)
        {
            if (slices[i].TimelineEnd > timelineSeconds + 1e-4)
                return slices[i];
        }
        return null;
    }

    public CompositionSlice? FindNextCompositionSlice(CompositionSlice current)
    {
        var slices = ResolveComposition();
        for (var i = 0; i < slices.Count; i++)
        {
            var candidate = slices[i];
            if (candidate.TimelineStart >= current.TimelineEnd - 1e-4
                && candidate.TimelineEnd > current.TimelineEnd + 1e-4)
            {
                return candidate;
            }
        }
        return null;
    }

    public TimelineHit? LocateAt(double timelineSeconds, Guid? preferredSegmentId = null)
    {
        if (segments.Count == 0) return null;
        var clamped = Math.Clamp(double.IsFinite(timelineSeconds) ? timelineSeconds : 0, 0, TotalDuration);

        if (preferredSegmentId is { } prefId && FindById(prefId) is { } pref
            && clamped >= pref.TimelineStart - 1e-4 && clamped <= pref.TimelineEnd + 1e-4)
        {
            var offset = Math.Clamp(clamped - pref.TimelineStart, 0, pref.Duration);
            return new TimelineHit(pref, IndexOf(pref.Id), pref.TimelineStart, pref.TimelineEnd, pref.SourceStart + offset);
        }

        TimelineSegment? best = null;
        var bestIdx = -1;
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (clamped < s.TimelineStart - 1e-4 || clamped > s.TimelineEnd + 1e-4) continue;
            var active = IsSegmentActive(s);
            if (best is null
                || (active && !IsSegmentActive(best))
                || (active == IsSegmentActive(best) && s.TrackIndex > best.TrackIndex))
            {
                best = s;
                bestIdx = i;
            }
        }

        if (best is not null)
        {
            var offset = Math.Clamp(clamped - best.TimelineStart, 0, best.Duration);
            return new TimelineHit(best, bestIdx, best.TimelineStart, best.TimelineEnd, best.SourceStart + offset);
        }

        return null;
    }

    public (TimelineSegment Left, TimelineSegment Right)? SplitSegmentAt(Guid segmentId, double timelineSeconds)
    {
        var idx = IndexOf(segmentId);
        if (idx < 0) return null;
        var seg = segments[idx];
        if (IsTrackLocked(seg.TrackIndex)) return null;

        var localOffset = timelineSeconds - seg.TimelineStart;
        if (localOffset < TimelineSegment.MinDuration || seg.Duration - localOffset < TimelineSegment.MinDuration)
            return null;

        var cutSourceTime = seg.SourceStart + localOffset;
        var right = new TimelineSegment(
            seg.SourcePath,
            seg.SourceDuration,
            cutSourceTime,
            seg.SourceEnd,
            seg.ColorIndex,
            seg.Enabled,
            seg.UseCustomNr,
            seg.CustomNr?.DeepClone(),
            id: null,
            timelineStart: timelineSeconds,
            trackIndex: seg.TrackIndex);

        seg.SourceEnd = cutSourceTime;
        segments.Insert(idx + 1, right);
        return (seg, right);
    }

    public List<(TimelineSegment Left, TimelineSegment Right)> SplitAllAt(double timelineSeconds, Guid? onlySegmentId = null)
    {
        var results = new List<(TimelineSegment, TimelineSegment)>();
        if (onlySegmentId is { } id)
        {
            if (SplitSegmentAt(id, timelineSeconds) is { } single)
                results.Add(single);
            return results;
        }

        var targets = segments
            .Where(s => !IsTrackLocked(s.TrackIndex)
                && timelineSeconds - s.TimelineStart >= TimelineSegment.MinDuration
                && s.TimelineEnd - timelineSeconds >= TimelineSegment.MinDuration)
            .Select(s => s.Id)
            .ToList();

        foreach (var targetId in targets)
        {
            if (SplitSegmentAt(targetId, timelineSeconds) is { } pair)
                results.Add(pair);
        }
        return results;
    }

    /// <summary>
    /// Trims a segment's left or right edge:
    /// - Left trim adjusts both TimelineStart and SourceStart by the same delta (right edge stays fixed).
    /// - Right trim adjusts SourceEnd (left edge stays fixed).
    /// Respects source duration bounds, locked tracks, and neighbors on the same track.
    /// </summary>
    public bool TrimEdge(Guid segmentId, bool isStart, double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || Math.Abs(deltaSeconds) < 1e-6) return false;
        var seg = FindById(segmentId);
        if (seg is null || IsTrackLocked(seg.TrackIndex)) return false;

        var minGap = Math.Min(TimelineSegment.MinDuration, seg.SourceDuration > 0 ? seg.SourceDuration : TimelineSegment.MinDuration);

        if (isStart)
        {
            var minDeltaFromSource = -seg.SourceStart;
            var minDeltaFromTimelineZero = -seg.TimelineStart;
            var leftNeighborEnd = FindLeftNeighborEnd(seg);
            var minDeltaFromNeighbor = leftNeighborEnd - seg.TimelineStart;
            var minDelta = Math.Max(minDeltaFromSource, Math.Max(minDeltaFromTimelineZero, minDeltaFromNeighbor));
            var maxDelta = seg.Duration - minGap;

            var clampedDelta = Math.Clamp(deltaSeconds, minDelta, Math.Max(minDelta, maxDelta));
            if (Math.Abs(clampedDelta) < 1e-6) return false;

            seg.SourceStart += clampedDelta;
            seg.TimelineStart += clampedDelta;
            return true;
        }
        else
        {
            var maxSource = seg.SourceDuration > 0 ? seg.SourceDuration : seg.SourceEnd + Math.Max(0, deltaSeconds);
            var maxDeltaFromSource = maxSource - seg.SourceEnd;
            var rightNeighborStart = FindRightNeighborStart(seg);
            var maxDeltaFromNeighbor = rightNeighborStart < double.PositiveInfinity
                ? rightNeighborStart - seg.TimelineEnd
                : double.PositiveInfinity;
            var maxDelta = Math.Min(maxDeltaFromSource, maxDeltaFromNeighbor);
            var minDelta = -(seg.Duration - minGap);

            var clampedDelta = Math.Clamp(deltaSeconds, Math.Min(minDelta, maxDelta), maxDelta);
            if (Math.Abs(clampedDelta) < 1e-6) return false;

            seg.SourceEnd += clampedDelta;
            return true;
        }
    }

    public bool SetSourceEdge(Guid segmentId, bool isStart, double newSourceTime)
    {
        var seg = FindById(segmentId);
        if (seg is null || !double.IsFinite(newSourceTime)) return false;
        var delta = isStart ? newSourceTime - seg.SourceStart : newSourceTime - seg.SourceEnd;
        return TrimEdge(segmentId, isStart, delta);
    }

    public bool MoveSegmentTo(Guid segmentId, double newTimelineStart, int newTrackIndex)
    {
        var seg = FindById(segmentId);
        if (seg is null || IsTrackLocked(seg.TrackIndex)) return false;

        var clampedTrack = Math.Clamp(newTrackIndex, 0, MaxTracks - 1);
        EnsureTrack(clampedTrack);
        if (IsTrackLocked(clampedTrack)) return false;

        var desiredStart = Math.Max(0, double.IsFinite(newTimelineStart) ? newTimelineStart : 0);
        var resolvedStart = FindNonOverlappingStart(clampedTrack, desiredStart, seg.Duration, seg.Id);

        if (seg.TrackIndex == clampedTrack && Math.Abs(seg.TimelineStart - resolvedStart) < 1e-5)
            return false;

        seg.TrackIndex = clampedTrack;
        seg.TimelineStart = resolvedStart;
        return true;
    }

    public TimelineSegment? DuplicateSegment(Guid segmentId)
    {
        var seg = FindById(segmentId);
        if (seg is null || IsTrackLocked(seg.TrackIndex)) return null;

        var copyStart = FindNonOverlappingStart(seg.TrackIndex, seg.TimelineEnd, seg.Duration, ignoreSegmentId: null);
        var copy = seg.Clone(preserveId: false);
        copy.TimelineStart = copyStart;
        segments.Add(copy);
        return copy;
    }

    public bool RemoveSegment(Guid segmentId, bool ripple = false)
    {
        var idx = IndexOf(segmentId);
        if (idx < 0) return false;
        var seg = segments[idx];
        if (IsTrackLocked(seg.TrackIndex)) return false;

        var removedStart = seg.TimelineStart;
        var removedDur = seg.Duration;
        var removedTrack = seg.TrackIndex;
        segments.RemoveAt(idx);

        if (ripple && removedDur > 0)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                if (segments[i].TrackIndex == removedTrack && segments[i].TimelineStart >= removedStart - 1e-4)
                    segments[i].TimelineStart = Math.Max(0, segments[i].TimelineStart - removedDur);
            }
        }
        return true;
    }

    public int CloseGapsOnTrack(int trackIndex)
    {
        if (IsTrackLocked(trackIndex)) return 0;
        var onTrack = segments
            .Where(s => s.TrackIndex == trackIndex)
            .OrderBy(s => s.TimelineStart)
            .ToList();

        var moved = 0;
        var cursor = 0.0;
        foreach (var seg in onTrack)
        {
            if (Math.Abs(seg.TimelineStart - cursor) > 1e-4)
            {
                seg.TimelineStart = cursor;
                moved++;
            }
            cursor += seg.Duration;
        }
        return moved;
    }

    public int CloseAllGaps()
    {
        var moved = 0;
        for (var t = 0; t < tracks.Count; t++)
            moved += CloseGapsOnTrack(t);
        return moved;
    }

    public int RemoveBySourcePath(string sourcePath) =>
        segments.RemoveAll(s => s.SourcePath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase));

    public void UpdateSourceDuration(string sourcePath, double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0) return;

        var updatedTracks = new HashSet<int>();
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (seg.SourcePath.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                var hadZeroDur = seg.SourceDuration <= 0;
                seg.UpdateSourceDuration(duration);
                if (hadZeroDur) updatedTracks.Add(seg.TrackIndex);
            }
        }

        foreach (var trackIdx in updatedTracks)
        {
            var ordered = segments.Where(s => s.TrackIndex == trackIdx).OrderBy(s => s.TimelineStart).ToList();
            var cursor = 0.0;
            foreach (var seg in ordered)
            {
                if (seg.TimelineStart < cursor - 1e-4)
                    seg.TimelineStart = cursor;
                cursor = seg.TimelineEnd;
            }
        }
    }

    public void Clear()
    {
        segments.Clear();
        tracks.Clear();
        tracks.Add(new TimelineTrackInfo(0));
        tracks.Add(new TimelineTrackInfo(1));
    }

    public TimelineStateSnapshot CaptureSnapshot() =>
        new(
            segments.Select(s => s.Clone(preserveId: true)).ToList(),
            tracks.Select(t => t.Clone()).ToList());

    public void RestoreSnapshot(TimelineStateSnapshot snapshot)
    {
        segments.Clear();
        tracks.Clear();
        foreach (var track in snapshot.Tracks)
            tracks.Add(track.Clone());
        while (tracks.Count < 2)
            tracks.Add(new TimelineTrackInfo(tracks.Count));
        foreach (var seg in snapshot.Segments)
            segments.Add(seg.Clone(preserveId: true));
    }

    public double SnapTime(double rawSeconds, double thresholdSeconds, double? playheadSeconds = null, Guid? ignoreSegmentId = null)
    {
        if (thresholdSeconds <= 0) return Math.Max(0, rawSeconds);
        var best = Math.Max(0, rawSeconds);
        var bestDist = thresholdSeconds;

        void Consider(double candidate)
        {
            var dist = Math.Abs(rawSeconds - candidate);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = candidate;
            }
        }

        Consider(0.0);
        if (playheadSeconds.HasValue)
            Consider(playheadSeconds.Value);

        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (ignoreSegmentId.HasValue && seg.Id == ignoreSegmentId.Value) continue;
            Consider(seg.TimelineStart);
            Consider(seg.TimelineEnd);
        }

        return best;
    }

    private double FindLeftNeighborEnd(TimelineSegment seg)
    {
        var maxEnd = 0.0;
        for (var i = 0; i < segments.Count; i++)
        {
            var other = segments[i];
            if (other.Id == seg.Id || other.TrackIndex != seg.TrackIndex) continue;
            if (other.TimelineEnd <= seg.TimelineStart + 1e-4 && other.TimelineEnd > maxEnd)
                maxEnd = other.TimelineEnd;
        }
        return maxEnd;
    }

    private double FindRightNeighborStart(TimelineSegment seg)
    {
        var minStart = double.PositiveInfinity;
        for (var i = 0; i < segments.Count; i++)
        {
            var other = segments[i];
            if (other.Id == seg.Id || other.TrackIndex != seg.TrackIndex) continue;
            if (other.TimelineStart >= seg.TimelineEnd - 1e-4 && other.TimelineStart < minStart)
                minStart = other.TimelineStart;
        }
        return minStart;
    }

    private double FindNonOverlappingStart(int trackIndex, double desiredStart, double duration, Guid? ignoreSegmentId)
    {
        var others = segments
            .Where(s => s.TrackIndex == trackIndex && (!ignoreSegmentId.HasValue || s.Id != ignoreSegmentId.Value))
            .OrderBy(s => s.TimelineStart)
            .ToList();

        if (others.Count == 0 || duration <= 0)
            return Math.Max(0, desiredStart);

        bool Overlaps(double start)
        {
            var end = start + duration;
            for (var i = 0; i < others.Count; i++)
            {
                if (start < others[i].TimelineEnd - 1e-4 && end > others[i].TimelineStart + 1e-4)
                    return true;
            }
            return false;
        }

        var candidate = Math.Max(0, desiredStart);
        if (!Overlaps(candidate)) return candidate;

        var best = double.PositiveInfinity;
        var bestDist = double.PositiveInfinity;

        void TryCandidate(double pos)
        {
            if (pos < 0 || Overlaps(pos)) return;
            var dist = Math.Abs(pos - desiredStart);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = pos;
            }
        }

        TryCandidate(0);
        for (var i = 0; i < others.Count; i++)
        {
            TryCandidate(others[i].TimelineStart - duration);
            TryCandidate(others[i].TimelineEnd);
        }

        return double.IsFinite(best) ? best : others[^1].TimelineEnd;
    }
}
