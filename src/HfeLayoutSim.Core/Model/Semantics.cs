namespace HfeLayoutSim.Core.Model;

/// <summary>Triple coding flags for judgment-type elements (color + icon + text).</summary>
public sealed class JudgmentCoding
{
    public bool HasColor { get; set; }
    public bool HasIcon { get; set; }
    public bool HasText { get; set; }
}

/// <summary>Meaning attached to an element — the core input of placement/flow rules (SPEC §5).</summary>
public sealed class Semantics
{
    public SemanticRole Role { get; set; } = SemanticRole.None;

    /// <summary>Fill-in order, 1-based. 0 means order-independent.</summary>
    public int Sequence { get; set; }

    public string? GroupId { get; set; }

    /// <summary>Safety-related field (verdict, LOT, sterilization parameter...). Violations escalate one severity level.</summary>
    public bool IsCritical { get; set; }

    public bool IsRequired { get; set; }

    public JudgmentCoding? Judgment { get; set; }

    /// <summary>(Buttons) delete / cancel-judgment style destructive actions.</summary>
    public bool IsDestructiveAction { get; set; }

    public InputKind InputKind { get; set; } = InputKind.None;

    /// <summary>Whether the spec limits are shown next to a measurement input.</summary>
    public bool SpecLimitShown { get; set; }
}
