using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HfeLayoutSim.App.Services;
using HfeLayoutSim.App.ViewModels;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Presets;
using Microsoft.Win32;

namespace HfeLayoutSim.App;

public partial class MainWindow : Window, IDialogService
{
    private readonly MainViewModel _vm;

    private ElementViewModel? _dragging;
    private Point _dragStart;
    private double _dragOriginX, _dragOriginY;
    private bool _dragMoved;

    public MainWindow()
    {
        _vm = new MainViewModel(this);
        InitializeComponent();
        DataContext = _vm;

        Width = AppServices.Settings.WindowWidth;
        Height = AppServices.Settings.WindowHeight;
        if (AppServices.Settings.WindowMaximized) WindowState = WindowState.Maximized;

        _vm.ViewportProvider = () => (CanvasScroll.ViewportWidth, CanvasScroll.ViewportHeight);
        _vm.ScrollToElementRequested += ScrollElementIntoView;
        _vm.VerdictReadyRequested += () => RightTabs.SelectedIndex = 0;

        Loaded += (_, _) => _vm.OfferRecovery();
        Closing += OnWindowClosing;
        PreviewKeyDown += OnWindowPreviewKeyDown;
    }

    // ================================================================ lifecycle

    /// <summary>
    /// Pushes the value in the focused editor into the view model. Most property editors commit on
    /// LostFocus, but a keyboard shortcut runs its command without moving focus — so without this,
    /// Ctrl+S writes the file, reports "저장 완료", and leaves the value the user just typed behind.
    /// </summary>
    private static void CommitFocusedEdit()
    {
        if (Keyboard.FocusedElement is TextBox box)
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // every shortcut in InputBindings is either Ctrl-modified or F5
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 || e.Key == Key.F5)
            CommitFocusedEdit();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // the X button never moves focus, so an uncommitted edit would vanish without a prompt
        CommitFocusedEdit();

        var maximized = WindowState == WindowState.Maximized;
        var width = maximized ? RestoreBounds.Width : Width;
        var height = maximized ? RestoreBounds.Height : Height;

        if (!_vm.PrepareToClose(width, height, maximized)) e.Cancel = true;
    }

    // ================================================================ palette

    private void OnPaletteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double-click on an actual row places an element; the scrollbar and the empty
        // space below the list must not silently add a copy of whatever was selected before.
        if (ItemUnderMouse(e.OriginalSource) is PresetDefinition preset)
            _vm.AddPreset(preset);
    }

    private void OnAddPresetClick(object sender, RoutedEventArgs e)
    {
        if (PaletteList.SelectedItem is PresetDefinition preset) _vm.AddPreset(preset);
        else _vm.SetStatus("팔레트에서 배치할 요소를 먼저 선택하십시오.", StatusLevel.Warning);
    }

    private static object? ItemUnderMouse(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ListBoxItem item) return item.DataContext;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // ================================================================ canvas

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        CanvasScroll.Focus();

        // Going back to the canvas ends the "look at this finding" mode, so the red marks do not
        // outlive the finding that caused them.
        _vm.ClearFindingHighlight();

        var element = ElementUnderMouse(e.OriginalSource);
        _vm.SelectElement(element);
        if (element is null) return;

        _dragging = element;
        _dragStart = e.GetPosition(EditorRoot);
        _dragOriginX = element.X;
        _dragOriginY = element.Y;
        _dragMoved = false;   // a click is not an edit — the gesture starts at the first real move
        EditorRoot.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null) return;

        // Losing capture (an Alt+Tab, a dialog, a lock screen) must end the gesture, otherwise the
        // next click on empty canvas teleports the element that was being dragged.
        if (e.LeftButton != MouseButtonState.Pressed || !EditorRoot.IsMouseCaptured)
        {
            EndDrag();
            return;
        }

        var position = e.GetPosition(EditorRoot);
        var x = _vm.Snap(_dragOriginX + (position.X - _dragStart.X));
        var y = _vm.Snap(_dragOriginY + (position.Y - _dragStart.Y));
        var (cx, cy) = _vm.ClampToCanvas(x, y, _dragging.DisplayW, _dragging.DisplayH);

        // Selecting an element to look at it must not cost the user their redo stack or their
        // evaluation result, so nothing is recorded until the element actually goes somewhere.
        if (Math.Abs(cx - _dragOriginX) < 1e-9 && Math.Abs(cy - _dragOriginY) < 1e-9) return;
        if (!_dragMoved)
        {
            _dragMoved = true;
            _dragging.BeginGesture("요소 이동");
        }

        _dragging.SetGeometry(cx, cy, _dragging.W, _dragging.H);
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_dragging is null) return;

        if (EditorRoot.IsMouseCaptured) EditorRoot.ReleaseMouseCapture();
        var moved = _dragging;
        var actuallyMoved = _dragMoved;
        _dragging = null;
        _dragMoved = false;
        if (!actuallyMoved) return;     // it was a click, not a drag: the document is unchanged

        moved.EndGesture();
        _vm.MarkDirty();
        _vm.MarkReportStale();
    }

    private void OnCanvasKeyDown(object sender, KeyEventArgs e)
    {
        var large = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        switch (e.Key)
        {
            case Key.Left: _vm.NudgeSelected(-1, 0, large); e.Handled = true; break;
            case Key.Right: _vm.NudgeSelected(1, 0, large); e.Handled = true; break;
            case Key.Up: _vm.NudgeSelected(0, -1, large); e.Handled = true; break;
            case Key.Down: _vm.NudgeSelected(0, 1, large); e.Handled = true; break;
            case Key.Escape: _vm.SelectElement(null); e.Handled = true; break;
        }
    }

    private static ElementViewModel? ElementUnderMouse(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: ElementViewModel element }) return element;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void ScrollElementIntoView(ElementViewModel element)
    {
        // Centre the element in the viewport so a highlighted finding is not off-screen.
        var scale = _vm.Scale;
        var x = (element.X + element.DisplayW / 2) * scale - CanvasScroll.ViewportWidth / 2;
        var y = (element.Y + element.DisplayH / 2) * scale - CanvasScroll.ViewportHeight / 2;
        CanvasScroll.ScrollToHorizontalOffset(Math.Max(0, x));
        CanvasScroll.ScrollToVerticalOffset(Math.Max(0, y));
    }

    // ================================================================ IDialogService

    public DiscardChoice AskUnsavedChanges(string layoutName, int elementCount)
    {
        // Three self-describing actions rather than 예/아니오/취소: the operator must not have to
        // re-read the prose to learn which button discards an hour of work.
        var dialog = new ChoiceWindow(
            "저장하지 않은 변경",
            $"'{layoutName}' 에 저장하지 않은 변경이 있습니다 (요소 {elementCount}개).\n" +
            "계속하면 이 변경은 사라집니다.",
            "저장한 뒤 계속", "저장하지 않고 버림", thirdLabel: "하던 작업으로 돌아가기")
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true) return DiscardChoice.Cancel;
        return dialog.Choice switch
        {
            1 => DiscardChoice.Save,
            2 => DiscardChoice.Discard,
            _ => DiscardChoice.Cancel,
        };
    }

    public string? AskSavePath(string suggestedFileName, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = "레이아웃 저장",
            Filter = "HFE 레이아웃 (*.hfelayout.json)|*.hfelayout.json|JSON (*.json)|*.json",
            FileName = suggestedFileName,
            AddExtension = true,
            DefaultExt = ".hfelayout.json",
        };
        if (Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? AskOpenPath(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "레이아웃 열기",
            Filter = "HFE 레이아웃 (*.hfelayout.json)|*.hfelayout.json|JSON (*.json)|*.json",
        };
        if (Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? AskCatalogPath(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "검사 카탈로그 선택",
            Filter = "검사 카탈로그 (*.catalog.json)|*.catalog.json|JSON (*.json)|*.json",
        };
        if (Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public string? AskReportPath(string suggestedFileName, string? initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Title = "HTML 리포트 저장",
            Filter = "HTML 리포트 (*.html)|*.html",
            FileName = suggestedFileName,
            AddExtension = true,
            DefaultExt = ".html",
        };
        if (Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    /// <summary>Medium picker with self-describing buttons — a Yes/No box would hide the choice in prose.</summary>
    public Medium? AskMedium()
    {
        // Both visible choices replace the open document, so the dialog needs an explicit way out —
        // otherwise Esc has no safe target and the user is cornered into generating something.
        var dialog = new ChoiceWindow(
            "생성할 매체 선택",
            "카탈로그의 검사 공정을 어느 매체의 레이아웃으로 생성할까요?",
            "종이 양식 (A4) 생성", "MES 화면 (1920×1080) 생성",
            thirdLabel: "생성하지 않고 돌아가기")
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true) return null;
        return dialog.Choice switch
        {
            1 => Medium.Paper,
            2 => Medium.Screen,
            _ => null,
        };
    }

    public IReadOnlyList<string> AskComparePaths(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "비교할 배치안 선택 (최대 2개, 현재 레이아웃과 함께 비교)",
            Filter = "HFE 레이아웃 (*.hfelayout.json)|*.hfelayout.json|JSON (*.json)|*.json",
            Multiselect = true,
        };
        if (Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(this) == true ? dialog.FileNames : Array.Empty<string>();
    }

    public bool Confirm(string title, string message, string confirmLabel, string cancelLabel)
    {
        var dialog = new ChoiceWindow(title, message, confirmLabel, cancelLabel, isSecondCancel: true)
        {
            Owner = this,
        };
        return dialog.ShowDialog() == true && dialog.ChoseFirst;
    }

    public void ShowError(string title, string message)
        => MessageBox.Show(this, message, $"{AppPaths.ProductTitle} — {title}",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowCompare(CompareViewModel compare)
        => new CompareWindow(compare) { Owner = this }.ShowDialog();
}
