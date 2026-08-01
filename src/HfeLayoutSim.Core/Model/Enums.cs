namespace HfeLayoutSim.Core.Model;

/// <summary>Target medium of a layout. Paper uses mm, Screen uses px as native unit.</summary>
public enum Medium
{
    Paper,
    Screen
}

/// <summary>Element catalog per SPEC §4.</summary>
public enum ElementType
{
    Header,
    Label,
    TextInput,
    NumInput,
    Checkbox,
    RadioGroup,
    Table,
    JudgmentBadge,
    Button,
    SignatureBox,
    WarningBox,
    InfoBox,
    Image,
    Barcode,
    Section,
    Divider,
    Stepper
}

/// <summary>Semantic role per SPEC §5. Half of the placement rules are driven by this.</summary>
public enum SemanticRole
{
    None,
    Identification,
    Precondition,
    SamplingInfo,
    InspectionItem,
    NonconformanceRecord,
    FinalVerdict,
    Signature,
    Warning,
    Reference,
    Navigation,
    Action,
    Decoration
}

/// <summary>How a confirmation is captured — used by C8-01 (critical checks must be transcriptions).</summary>
public enum InputKind
{
    None,
    Check,
    Transcribe,
    Auto
}

/// <summary>Button visual kind (Screen only).</summary>
public enum ButtonKind
{
    Primary,
    Secondary,
    Danger,
    Ghost
}
