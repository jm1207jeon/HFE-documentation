using HfeLayoutSim.App.Services;
using HfeLayoutSim.App.ViewModels;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.Tests;

/// <summary>
/// Drives the editor the way a user would — create, place, edit, undo, save, reopen, evaluate,
/// export, compare — on a real WPF dispatcher. These are the paths the static XAML check cannot
/// reach: they fail on a wrong command wiring, a broken round trip, or an exception in a code path
/// that was only ever read, never run.
/// </summary>
[Collection("wpf")]
public sealed class EditorSmokeTests
{
    private readonly UiThread _ui;

    public EditorSmokeTests(UiThread ui) => _ui = ui;

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hfe-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string RepoFile(params string[] parts)
    {
        var relative = string.Join("/", parts);
        return AppServices.TryFindFile(relative)
               ?? throw new FileNotFoundException($"테스트 데이터를 찾을 수 없습니다: {relative}");
    }

    [Fact]
    public void NewLayout_ThenEveryPreset_PlacesWithoutError()
    {
        _ui.Run(() =>
        {
            var dialogs = new FakeDialogs();
            var vm = new MainViewModel(dialogs);

            vm.NewPaperCommand.Execute(null);
            Assert.True(vm.HasLayout);

            // Every palette entry must be placeable on the medium it offers — a preset with a bad id
            // or a missing size would throw here instead of at a user's first double-click.
            foreach (var preset in vm.PaletteItems.ToList())
            {
                var before = vm.Elements.Count;
                vm.AddPreset(preset);
                Assert.True(vm.Elements.Count == before + 1, $"프리셋 배치 실패: {preset.Id}");
            }

            Assert.All(vm.Elements, e => Assert.False(string.IsNullOrWhiteSpace(e.Id)));
            Assert.Equal(vm.Elements.Select(e => e.Id).Distinct().Count(), vm.Elements.Count);
            Assert.Empty(dialogs.Errors);
        });
    }

    [Fact]
    public void ScreenPalette_DiffersFromPaper_AndPlaces()
    {
        _ui.Run(() =>
        {
            var vm = new MainViewModel(new FakeDialogs());

            vm.NewPaperCommand.Execute(null);
            var paper = vm.PaletteItems.Select(p => p.Id).ToList();

            vm.NewScreenCommand.Execute(null);
            var screen = vm.PaletteItems.Select(p => p.Id).ToList();

            Assert.NotEqual(paper, screen);
            foreach (var preset in vm.PaletteItems.ToList()) vm.AddPreset(preset);
            Assert.Equal(screen.Count, vm.Elements.Count);
        });
    }

    [Fact]
    public void Undo_Redo_RestoresEveryEditExactly()
    {
        _ui.Run(() =>
        {
            var vm = new MainViewModel(new FakeDialogs());
            vm.NewPaperCommand.Execute(null);

            vm.AddPreset(vm.PaletteItems.First());
            var element = vm.Elements.Single();
            vm.SelectElement(element);

            element.Text = "치수 검사 결과";
            element.X = 42;
            vm.DuplicateSelectedCommand.Execute(null);
            Assert.Equal(2, vm.Elements.Count);

            // unwind everything
            while (vm.CanUndo) vm.UndoCommand.Execute(null);
            Assert.Empty(vm.Elements);

            // and put it all back
            while (vm.CanRedo) vm.RedoCommand.Execute(null);
            Assert.Equal(2, vm.Elements.Count);
            Assert.Contains(vm.Elements, e => e.Text == "치수 검사 결과" && Math.Abs(e.X - 42) < 0.001);
        });
    }

    [Fact]
    public void SaveAndReopen_PreservesTheLayout()
    {
        _ui.Run(() =>
        {
            var dir = TempDir();
            var path = Path.Combine(dir, "round-trip.hfelayout.json");
            var dialogs = new FakeDialogs { SavePath = path, OpenPath = path };
            var vm = new MainViewModel(dialogs);

            vm.NewPaperCommand.Execute(null);
            foreach (var preset in vm.PaletteItems.Take(6).ToList()) vm.AddPreset(preset);
            vm.LayoutName = "수입검사 성적서";
            var saved = vm.Elements.Select(e => (e.Id, e.X, e.Y, e.Text)).ToList();

            vm.SaveAsCommand.Execute(null);
            Assert.True(File.Exists(path), "저장 파일이 만들어지지 않았습니다.");
            Assert.False(vm.IsDirty);

            vm.OpenCommand.Execute(null);
            Assert.Equal("수입검사 성적서", vm.LayoutName);
            Assert.Equal(saved, vm.Elements.Select(e => (e.Id, e.X, e.Y, e.Text)).ToList());
            Assert.False(vm.IsDirty);
            Assert.Empty(dialogs.Errors);

            Directory.Delete(dir, recursive: true);
        });
    }

    [Fact]
    public void OpeningACorruptFile_ReportsItAndKeepsTheCurrentWork()
    {
        _ui.Run(() =>
        {
            var dir = TempDir();
            var broken = Path.Combine(dir, "broken.hfelayout.json");
            File.WriteAllText(broken, "{ \"meta\": { \"name\": ");

            var dialogs = new FakeDialogs { OpenPath = broken };
            var vm = new MainViewModel(dialogs);
            vm.NewPaperCommand.Execute(null);
            vm.AddPreset(vm.PaletteItems.First());

            vm.OpenCommand.Execute(null);

            Assert.Single(dialogs.Errors);
            Assert.Single(vm.Elements);          // the open failed; the work in hand survived
            Assert.True(vm.HasLayout);

            Directory.Delete(dir, recursive: true);
        });
    }

    [Fact]
    public void Evaluate_ProducesAVerdict_AndEditingMarksItStale()
    {
        _ui.Run(async () =>
        {
            var dialogs = new FakeDialogs { CatalogPath = RepoFile("catalog", "incoming_inspection.catalog.json") };
            var vm = new MainViewModel(dialogs);

            vm.GenerateFromCatalogCommand.Execute(null);
            Assert.True(vm.HasLayout);
            Assert.NotEmpty(vm.Elements);
            Assert.Empty(dialogs.Errors);

            await vm.EvaluateCommand.ExecuteAsync(null);

            Assert.True(vm.HasReport);
            Assert.False(vm.ReportIsStale);
            Assert.NotEmpty(vm.CategoryGrades);
            Assert.Equal(9, vm.CategoryGrades.Count);
            Assert.True(vm.Report!.ScoreCard.Pass,
                $"카탈로그 생성 레이아웃이 FAIL: {vm.Report.ScoreCard.Total}");
            Assert.True(vm.CanExportHtml);

            // any edit invalidates the verdict — a stale PASS badge is the failure mode this guards
            vm.AddPreset(vm.PaletteItems.First());
            Assert.True(vm.ReportIsStale);
            Assert.False(vm.CanExportHtml);
        });
    }

    [Fact]
    public void ExportHtml_WritesAReadableReport()
    {
        _ui.Run(async () =>
        {
            var dir = TempDir();
            var report = Path.Combine(dir, "report.html");
            var dialogs = new FakeDialogs
            {
                CatalogPath = RepoFile("catalog", "incoming_inspection.catalog.json"),
                ReportPath = report,
            };
            var vm = new MainViewModel(dialogs);

            vm.GenerateFromCatalogCommand.Execute(null);
            await vm.EvaluateCommand.ExecuteAsync(null);
            vm.ExportHtmlCommand.Execute(null);

            Assert.True(File.Exists(report));
            var html = File.ReadAllText(report);
            Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(vm.Report!.ScoreCard.Grade, html);
            Assert.Empty(dialogs.Errors);

            Directory.Delete(dir, recursive: true);
        });
    }

    [Fact]
    public void Compare_BuildsTheVariantView()
    {
        _ui.Run(() =>
        {
            var dialogs = new FakeDialogs
            {
                ComparePaths = new[] { RepoFile("samples", "bad_layout_paper.hfelayout.json") },
            };
            var vm = new MainViewModel(dialogs);

            // the open layout is always variant 1; the dialog supplies the others
            vm.OpenPath(RepoFile("samples", "incoming_inspection_paper.hfelayout.json"));
            vm.CompareCommand.Execute(null);

            Assert.NotNull(dialogs.ShownCompare);
            Assert.Equal(2, dialogs.ShownCompare!.Variants.Count);
            Assert.NotEmpty(dialogs.ShownCompare.Categories);
            Assert.NotEmpty(dialogs.ShownCompare.DiffGroups);
            Assert.Empty(dialogs.Errors);
        });
    }

    [Fact]
    public void UnsavedWork_IsGuardedOnDiscard()
    {
        _ui.Run(() =>
        {
            var dialogs = new FakeDialogs { UnsavedAnswer = DiscardChoice.Cancel };
            var vm = new MainViewModel(dialogs);

            vm.NewPaperCommand.Execute(null);
            vm.AddPreset(vm.PaletteItems.First());
            Assert.True(vm.IsDirty);

            // cancelling the prompt must keep the layout the user was working on
            vm.NewScreenCommand.Execute(null);
            Assert.Equal(Medium.Paper, vm.Medium);
            Assert.Single(vm.Elements);
            Assert.Contains(dialogs.Asked, a => a.StartsWith("unsaved:"));

            dialogs.UnsavedAnswer = DiscardChoice.Discard;
            vm.NewScreenCommand.Execute(null);
            Assert.Equal(Medium.Screen, vm.Medium);
            Assert.Empty(vm.Elements);
        });
    }

    [Fact]
    public void Nudge_And_Delete_StayInsideTheCanvas()
    {
        _ui.Run(() =>
        {
            var vm = new MainViewModel(new FakeDialogs());
            vm.NewPaperCommand.Execute(null);
            vm.AddPreset(vm.PaletteItems.First());

            var element = vm.Elements.Single();
            vm.SelectElement(element);

            // The editor lets an element hang off the edge (that overhang is itself a finding) but
            // never lets it leave: at least a margin's worth must stay grabbable on every side.
            const double margin = 5.0;   // paper, mm — MainViewModel.ClampToCanvas

            for (var i = 0; i < 400; i++) vm.NudgeSelected(-1, -1, large: true);
            Assert.True(element.X + element.DisplayW >= margin - 0.001,
                $"요소가 왼쪽으로 사라졌습니다 (X={element.X}).");
            Assert.True(element.Y + element.DisplayH >= margin - 0.001,
                $"요소가 위로 사라졌습니다 (Y={element.Y}).");

            for (var i = 0; i < 400; i++) vm.NudgeSelected(1, 1, large: true);
            Assert.True(element.X <= vm.CanvasWidth - margin + 0.001,
                $"요소가 오른쪽으로 사라졌습니다 (X={element.X}).");
            Assert.True(element.Y <= vm.CanvasHeight - margin + 0.001,
                $"요소가 아래로 사라졌습니다 (Y={element.Y}).");

            vm.DeleteSelectedCommand.Execute(null);
            Assert.Empty(vm.Elements);
            Assert.Null(vm.SelectedElement);
        });
    }

    [Fact]
    public void Coaching_FollowsTheSelection()
    {
        _ui.Run(async () =>
        {
            var vm = new MainViewModel(new FakeDialogs());
            vm.NewPaperCommand.Execute(null);

            // a LOT field dropped in the bottom-left blind spot is the textbook mistake the coach exists for
            var lot = vm.PaletteItems.FirstOrDefault(p => p.Id == "input-lot") ?? vm.PaletteItems.First();
            vm.AddPreset(lot);
            var element = vm.Elements.Single();
            element.X = 10;
            element.Y = vm.CanvasHeight - element.H - 10;
            vm.SelectElement(element);

            // the coach runs off the UI thread and posts its result back; give it a bounded moment
            for (var i = 0; i < 100 && vm.Advice.Count == 0; i++) await Task.Delay(50);

            Assert.NotEmpty(vm.Advice);
            Assert.All(vm.Advice, a => Assert.False(string.IsNullOrWhiteSpace(a.Message)));
        });
    }
}
