using System.Windows.Controls;
using System.Windows;
using HfeLayoutSim.App.ViewModels;

namespace HfeLayoutSim.App;

/// <summary>Side-by-side comparison of up to three variants (SPEC §6.5).</summary>
public partial class CompareWindow : Window
{
    public CompareWindow(CompareViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Sizes the score bar to its track. A percentage width needs the track's measured width, which
    /// only exists after layout — binding alone cannot express it without a multi-value converter,
    /// and this stays readable.
    /// </summary>
    private void OnScoreTrackSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Border track || track.Child is not Border fill) return;
        if (track.DataContext is not VariantColumnViewModel variant) return;

        var inner = Math.Max(0, track.ActualWidth - track.BorderThickness.Left - track.BorderThickness.Right);
        fill.Width = inner * variant.ScoreFraction;
    }
}
