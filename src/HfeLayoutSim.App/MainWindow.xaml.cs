using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HfeLayoutSim.App.Services;
using HfeLayoutSim.App.ViewModels;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Report;
using Microsoft.Win32;

namespace HfeLayoutSim.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    private ElementViewModel? _dragging;
    private Point _dragStart;
    private double _dragOriginX, _dragOriginY;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    // ---------------------------------------------------------------- toolbar

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "HFE 레이아웃 (*.hfelayout.json)|*.hfelayout.json|JSON (*.json)|*.json",
            InitialDirectory = SamplesDirectory()
        };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() => _vm.OpenFile(dialog.FileName), "레이아웃 열기");
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HFE 레이아웃 (*.hfelayout.json)|*.hfelayout.json",
            FileName = "layout.hfelayout.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() => _vm.SaveFile(dialog.FileName), "레이아웃 저장");
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "검사 카탈로그 (*.catalog.json)|*.catalog.json|JSON (*.json)|*.json",
            InitialDirectory = CatalogDirectory()
        };
        if (dialog.ShowDialog(this) != true) return;

        var result = MessageBox.Show(this,
            "어떤 매체로 생성할까요?\n\n예 = 종이 양식 (A4)\n아니오 = MES 화면 (1920×1080)",
            "카탈로그로 생성", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return;

        var medium = result == MessageBoxResult.Yes ? Medium.Paper : Medium.Screen;
        Guard(() => _vm.GenerateFromCatalog(dialog.FileName, medium), "카탈로그 생성");
    }

    private void OnExportHtmlClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "HTML 리포트 (*.html)|*.html",
            FileName = "hfe_report.html"
        };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() => _vm.ExportHtml(dialog.FileName), "HTML 내보내기");
    }

    // ---------------------------------------------------------------- palette

    private void OnPaletteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PaletteList.SelectedItem is PresetDefinition preset)
            Guard(() => _vm.AddPreset(preset), "요소 배치");
    }

    // ---------------------------------------------------------------- canvas drag

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = FindElementViewModel(e.OriginalSource);
        _vm.SelectElement(vm);
        if (vm is null) return;

        _dragging = vm;
        _dragStart = e.GetPosition(EditorRoot);
        _dragOriginX = vm.X;
        _dragOriginY = vm.Y;
        EditorRoot.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(EditorRoot);
        var nx = _vm.Snap(_dragOriginX + (pos.X - _dragStart.X));
        var ny = _vm.Snap(_dragOriginY + (pos.Y - _dragStart.Y));

        // keep inside the canvas
        nx = Math.Max(0, Math.Min(nx, _vm.CanvasWidth - _dragging.W));
        ny = Math.Max(0, Math.Min(ny, _vm.CanvasHeight - _dragging.H));

        _dragging.X = nx;
        _dragging.Y = ny;
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is null) return;
        EditorRoot.ReleaseMouseCapture();
        var moved = _dragging;
        _dragging = null;
        _vm.OnElementMoved(moved);
    }

    private static ElementViewModel? FindElementViewModel(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: ElementViewModel vm })
                return vm;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ---------------------------------------------------------------- report

    private void OnFindingSelected(object sender, SelectionChangedEventArgs e)
        => _vm.HighlightFinding(FindingsList.SelectedItem as Finding);

    // ---------------------------------------------------------------- helpers

    private void Guard(Action action, string operation)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, $"{operation} 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string? SamplesDirectory()
    {
        var sample = AppServices.TryFindFile(System.IO.Path.Combine("samples", "incoming_inspection_paper.hfelayout.json"));
        return sample is null ? null : System.IO.Path.GetDirectoryName(sample);
    }

    private static string? CatalogDirectory()
    {
        var catalog = AppServices.TryFindFile(System.IO.Path.Combine("catalog", "incoming_inspection.catalog.json"));
        return catalog is null ? null : System.IO.Path.GetDirectoryName(catalog);
    }
}
