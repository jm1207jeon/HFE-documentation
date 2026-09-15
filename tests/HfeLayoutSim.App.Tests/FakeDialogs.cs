using HfeLayoutSim.App.ViewModels;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.Tests;

/// <summary>
/// Stands in for the window's dialogs so a test can drive the view model end to end. Every answer is
/// explicit: an unexpected prompt returns the safe branch and is recorded, so a test that provokes a
/// dialog it did not plan for can assert on it instead of hanging.
/// </summary>
public sealed class FakeDialogs : IDialogService
{
    public List<string> Asked { get; } = new();
    public List<(string Title, string Message)> Errors { get; } = new();

    public DiscardChoice UnsavedAnswer { get; set; } = DiscardChoice.Discard;
    public bool ConfirmAnswer { get; set; } = true;
    public string? SavePath { get; set; }
    public string? OpenPath { get; set; }
    public string? CatalogPath { get; set; }
    public string? ReportPath { get; set; }
    public Medium? MediumAnswer { get; set; } = Medium.Paper;
    public IReadOnlyList<string> ComparePaths { get; set; } = Array.Empty<string>();
    public CompareViewModel? ShownCompare { get; private set; }

    public DiscardChoice AskUnsavedChanges(string layoutName, int elementCount)
    {
        Asked.Add($"unsaved:{layoutName}:{elementCount}");
        return UnsavedAnswer;
    }

    public string? AskSavePath(string suggestedFileName, string? initialDirectory)
    {
        Asked.Add($"save:{suggestedFileName}");
        return SavePath;
    }

    public string? AskOpenPath(string? initialDirectory)
    {
        Asked.Add("open");
        return OpenPath;
    }

    public string? AskCatalogPath(string? initialDirectory)
    {
        Asked.Add("catalog");
        return CatalogPath;
    }

    public string? AskReportPath(string suggestedFileName, string? initialDirectory)
    {
        Asked.Add($"report:{suggestedFileName}");
        return ReportPath;
    }

    public Medium? AskMedium()
    {
        Asked.Add("medium");
        return MediumAnswer;
    }

    public IReadOnlyList<string> AskComparePaths(string? initialDirectory)
    {
        Asked.Add("compare");
        return ComparePaths;
    }

    public bool Confirm(string title, string message, string confirmLabel, string cancelLabel)
    {
        Asked.Add($"confirm:{title}");
        return ConfirmAnswer;
    }

    public void ShowError(string title, string message)
    {
        Errors.Add((title, message));
    }

    public void ShowCompare(CompareViewModel compare)
    {
        Asked.Add("showCompare");
        ShownCompare = compare;
    }
}
