using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.Services;

/// <summary>
/// Undo/redo over whole-layout snapshots (SPEC §9 asks for ≥50 steps).
/// Snapshots are the serialized layout: every kind of edit — move, resize, style, semantics,
/// add, delete, catalog generation — is captured by construction, so no edit path can forget to
/// register itself and silently become un-undoable.
/// </summary>
public sealed class UndoStack
{
    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();

    private readonly record struct Entry(string Label, string Json, string? CoalesceKey, DateTime At);

    /// <summary>How long a run of keystrokes stays one undo step. A pause ends the run.</summary>
    public TimeSpan CoalesceWindow { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>Kept well above the 50 the spec asks for; snapshots are small text.</summary>
    public int Capacity { get; init; } = 64;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public string UndoLabel => _undo.Count > 0 ? _undo[^1].Label : "";
    public string RedoLabel => _redo.Count > 0 ? _redo[^1].Label : "";

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>
    /// Call immediately BEFORE mutating <paramref name="layout"/>. A <paramref name="coalesceKey"/>
    /// marks an edit that arrives in a stream — typing into a text box produces one per character —
    /// and consecutive edits sharing the key fold into the step already on the stack, so one Ctrl+Z
    /// undoes the word rather than the letter, and a held key cannot evict the real history.
    /// </summary>
    public void Record(string label, Layout layout, string? coalesceKey = null)
    {
        var now = DateTime.UtcNow;
        if (coalesceKey is not null && _undo.Count > 0 &&
            _undo[^1].CoalesceKey == coalesceKey && now - _undo[^1].At <= CoalesceWindow)
        {
            // the entry already on the stack holds the state from before this run began
            _undo[^1] = _undo[^1] with { At = now };
            _redo.Clear();
            return;
        }

        _undo.Add(new Entry(label, LayoutSerializer.Save(layout), coalesceKey, now));
        if (_undo.Count > Capacity) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Returns the previous layout, or null when there is nothing to undo.</summary>
    public Layout? Undo(Layout current, out string label)
        => Step(_undo, _redo, current, out label);

    public Layout? Redo(Layout current, out string label)
        => Step(_redo, _undo, current, out label);

    private static Layout? Step(List<Entry> from, List<Entry> to, Layout current, out string label)
    {
        label = "";
        if (from.Count == 0) return null;

        var entry = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(new Entry(entry.Label, LayoutSerializer.Save(current), null, DateTime.UtcNow));
        label = entry.Label;
        return LayoutSerializer.Load(entry.Json, "undo");
    }
}
