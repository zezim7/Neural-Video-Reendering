namespace VideoNR.Models;

internal sealed record TimelineHistoryEntry(TimelineStateSnapshot State, Guid? SelectedSegmentId);

internal sealed class TimelineHistory
{
    private const int MaxEntries = 80;
    private readonly List<TimelineHistoryEntry> undoStack = [];
    private readonly List<TimelineHistoryEntry> redoStack = [];

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    public void PushUndo(TimelineSequence sequence, Guid? selectedSegmentId) =>
        PushSnapshot(sequence.CaptureSnapshot(), selectedSegmentId);

    public void PushSnapshot(TimelineStateSnapshot snapshot, Guid? selectedSegmentId)
    {
        undoStack.Add(new TimelineHistoryEntry(snapshot, selectedSegmentId));
        if (undoStack.Count > MaxEntries)
            undoStack.RemoveAt(0);
        redoStack.Clear();
    }

    public bool TryUndo(TimelineSequence sequence, Guid? currentSelectedId, out Guid? restoredSelectedId)
    {
        restoredSelectedId = currentSelectedId;
        if (undoStack.Count == 0) return false;

        redoStack.Add(new TimelineHistoryEntry(sequence.CaptureSnapshot(), currentSelectedId));
        var previous = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        sequence.RestoreSnapshot(previous.State);
        restoredSelectedId = previous.SelectedSegmentId;
        return true;
    }

    public bool TryRedo(TimelineSequence sequence, Guid? currentSelectedId, out Guid? restoredSelectedId)
    {
        restoredSelectedId = currentSelectedId;
        if (redoStack.Count == 0) return false;

        undoStack.Add(new TimelineHistoryEntry(sequence.CaptureSnapshot(), currentSelectedId));
        var next = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        sequence.RestoreSnapshot(next.State);
        restoredSelectedId = next.SelectedSegmentId;
        return true;
    }

    public void Clear()
    {
        undoStack.Clear();
        redoStack.Clear();
    }
}
