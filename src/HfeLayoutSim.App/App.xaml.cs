using System.Threading;
using System.Windows;
using System.Windows.Threading;
using HfeLayoutSim.App.Services;

namespace HfeLayoutSim.App;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second copy would fight over the same autosave slot and settings file, and two windows
        // editing one layout silently overwrite each other on save.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\HfeLayoutSim_SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                $"{AppPaths.ProductTitle}이(가) 이미 실행 중입니다.\n실행 중인 창을 사용하십시오.",
                AppPaths.ProductTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        InstallExceptionHandlers();
        AppLog.Info($"앱 시작 v{typeof(App).Assembly.GetName().Version}");

        try
        {
            AppServices.Initialize(ConfirmKnowledgeFallback);
        }
        catch (KnowledgeAbortException ex)
        {
            AppLog.Warn("사용자 선택으로 시작 중단: " + ex.Message);
            Shutdown(1);
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error("시작 실패", ex);
            MessageBox.Show(
                "규칙 지식베이스를 불러오지 못해 프로그램을 시작할 수 없습니다.\n\n" +
                ex.Message +
                $"\n\n진단 로그: {AppLog.FilePath}",
                $"{AppPaths.ProductTitle} — 시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// The editor holds work that exists nowhere else yet, so an unexpected exception must not take
    /// the process down: report it, log it, and let the user save. Background and fatal exceptions
    /// are at least recorded so a failure can be explained afterwards.
    /// </summary>
    private void InstallExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("배경 작업 예외", args.Exception);
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("치명적 예외 (프로세스 종료)", args.ExceptionObject as Exception);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        AppLog.Error("UI 예외", args.Exception);
        args.Handled = true;

        MessageBox.Show(
            "예기치 않은 오류가 발생했습니다:\n\n" + args.Exception.Message +
            "\n\n프로그램은 계속 실행됩니다. 작업 중인 레이아웃을 저장한 뒤 다시 시도하십시오." +
            $"\n진단 로그: {AppLog.FilePath}",
            $"{AppPaths.ProductTitle} — 오류", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>A knowledge-base file that exists but cannot be used is the user's own edit — never
    /// swap in the built-in copy silently, because scoring would no longer match their file.</summary>
    private static bool ConfirmKnowledgeFallback(string description, string path, string problem)
    {
        var answer = MessageBox.Show(
            $"{description} 파일을 사용할 수 없습니다.\n\n파일: {path}\n\n{problem}\n\n" +
            "내장 기본 파일로 계속 실행할까요?\n" +
            "[예] 내장 기본값으로 실행 (파일의 수정 내용은 반영되지 않습니다)\n" +
            "[아니오] 실행을 중단하고 파일을 고칩니다",
            $"{AppPaths.ProductTitle} — {description} 오류",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("앱 종료");
        try { _singleInstance?.ReleaseMutex(); } catch { /* not owned */ }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
