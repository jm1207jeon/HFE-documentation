using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HfeLayoutSim.App.Services;

namespace HfeLayoutSim.App.Tests;

/// <summary>
/// Opens the real window. A WPF binding that cannot resolve does not throw — it writes a trace
/// message and leaves the control blank — so these tests render the window, walk its tabs, and fail
/// on any binding error WPF reported along the way.
/// </summary>
[Collection("wpf")]
public sealed class WindowSmokeTests
{
    private readonly UiThread _ui;

    public WindowSmokeTests(UiThread ui) => _ui = ui;

    [Fact]
    public void MainWindow_Renders_WithoutBindingErrors()
    {
        _ui.Run(() =>
        {
            // a pending autosave would pop the recovery prompt through the window's real dialogs
            AutosaveService.Clear();
            BindingErrors.Clear();

            var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = -4000 };
            try
            {
                window.Show();
                _ui.Drain(DispatcherPriority.Loaded);

                // every tab must render — a template that only fails when selected is still broken
                var tabs = FindVisual<TabControl>(window, t => t.Items.Count >= 4);
                Assert.NotNull(tabs);
                for (var i = 0; i < tabs!.Items.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    window.UpdateLayout();
                    _ui.Drain(DispatcherPriority.Loaded);
                }
            }
            finally
            {
                window.Close();
            }

            var errors = BindingErrors.Drain()
                .Where(m => m.Contains("System.Windows.Data Error", StringComparison.Ordinal))
                .ToList();
            Assert.True(errors.Count == 0,
                "바인딩 오류 — 화면의 해당 부분이 조용히 비어 있게 됩니다:\n" + string.Join("\n", errors));
        });
    }

    [Fact]
    public void MainWindow_WithAFullLayout_RendersEveryElementTemplate()
    {
        _ui.Run(() =>
        {
            AutosaveService.Clear();
            BindingErrors.Clear();

            var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = -4000 };
            try
            {
                window.Show();
                _ui.Drain(DispatcherPriority.Loaded);

                var vm = (ViewModels.MainViewModel)window.DataContext;
                vm.NewPaperCommand.Execute(null);
                foreach (var preset in vm.PaletteItems.ToList()) vm.AddPreset(preset);
                vm.SelectElement(vm.Elements.First());

                window.UpdateLayout();
                _ui.Drain(DispatcherPriority.Loaded);

                Assert.NotEmpty(vm.Elements);
            }
            finally
            {
                window.Close();
            }

            var errors = BindingErrors.Drain()
                .Where(m => m.Contains("System.Windows.Data Error", StringComparison.Ordinal))
                .ToList();
            Assert.True(errors.Count == 0,
                "요소 렌더링 중 바인딩 오류:\n" + string.Join("\n", errors));
        });
    }

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        if (root is T typed && match(typed)) return typed;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindVisual(System.Windows.Media.VisualTreeHelper.GetChild(root, i), match);
            if (found is not null) return found;
        }
        return null;
    }
}
