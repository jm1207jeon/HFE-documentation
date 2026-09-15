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
}
