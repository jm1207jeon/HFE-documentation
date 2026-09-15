using System.IO;
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
                UiThread.DoEvents(DispatcherPriority.Loaded);

                // every tab must render — a template that only fails when selected is still broken
                var tabs = FindVisual<TabControl>(window, t => t.Items.Count >= 4);
                Assert.NotNull(tabs);
                for (var i = 0; i < tabs!.Items.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    window.UpdateLayout();
                    UiThread.DoEvents(DispatcherPriority.Loaded);
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
                UiThread.DoEvents(DispatcherPriority.Loaded);

                var vm = (ViewModels.MainViewModel)window.DataContext;
                vm.NewPaperCommand.Execute(null);
                foreach (var preset in vm.PaletteItems.ToList()) vm.AddPreset(preset);
                vm.SelectElement(vm.Elements.First());

                window.UpdateLayout();
                UiThread.DoEvents(DispatcherPriority.Loaded);

                Assert.NotEmpty(vm.Elements);

                // closing with unsaved work would raise the window's REAL dialog and hang the run,
                // which is exactly the guard the editor is supposed to have — so satisfy it.
                var scratch = Path.Combine(Path.GetTempPath(), "hfe-smoke",
                    Guid.NewGuid().ToString("N"), "window.hfelayout.json");
                Directory.CreateDirectory(Path.GetDirectoryName(scratch)!);
                Assert.True(vm.SaveTo(scratch));
                Assert.False(vm.IsDirty);
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

    /// <summary>
    /// The buttons a user reaches for must actually be clickable. CommunityToolkit commands only
    /// re-evaluate CanExecute when they are told to, so a missing notification leaves a button greyed
    /// out for the whole session while the keyboard shortcut still works — invisible to any test that
    /// calls the command directly.
    /// </summary>
    [Fact]
    public void ToolbarAndPropertyButtons_AreEnabled_WhenTheirActionIsAvailable()
    {
        _ui.Run(() =>
        {
            AutosaveService.Clear();

            var window = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = -4000 };
            try
            {
                window.Show();
                UiThread.DoEvents(DispatcherPriority.Loaded);

                var vm = (ViewModels.MainViewModel)window.DataContext;
                vm.NewPaperCommand.Execute(null);
                vm.AddPreset(vm.PaletteItems.First());
                vm.SelectElement(vm.Elements.First());

                // the property editors live on the 속성 tab, so realize it before looking for them
                var tabs = FindVisual<TabControl>(window, t => t.Items.Count >= 4)!;
                tabs.SelectedIndex = 2;
                window.UpdateLayout();
                UiThread.DoEvents(DispatcherPriority.Loaded);

                foreach (var label in new[] { "평가 실행 (F5)", "배치안 비교…", "선택 요소 복제", "선택 요소 삭제" })
                {
                    var button = FindVisual<Button>(window, b => b.Content as string == label);
                    Assert.True(button is not null, $"버튼을 찾지 못했습니다: {label}");
                    Assert.True(button!.IsEnabled, $"'{label}' 버튼이 비활성 상태입니다 — 마우스로는 쓸 수 없습니다.");
                }

                var scratch = Path.Combine(Path.GetTempPath(), "hfe-smoke",
                    Guid.NewGuid().ToString("N"), "buttons.hfelayout.json");
                Directory.CreateDirectory(Path.GetDirectoryName(scratch)!);
                Assert.True(vm.SaveTo(scratch));
            }
            finally
            {
                window.Close();
            }
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
