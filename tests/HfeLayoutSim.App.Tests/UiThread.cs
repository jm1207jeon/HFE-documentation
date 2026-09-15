using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using HfeLayoutSim.App;
using HfeLayoutSim.App.Services;

namespace HfeLayoutSim.App.Tests;

/// <summary>
/// One STA thread with a running dispatcher, shared by every test in the collection.
///
/// WPF ties an <see cref="Application"/> and everything it creates to the thread that made it, so
/// spinning one up per test would hand the next test a cross-thread object. Instead the fixture owns
/// the UI thread for the whole run and tests marshal onto it, which is also how the real app behaves.
/// </summary>
public sealed class UiThread : IDisposable
{
    private readonly Thread _thread;
    private Dispatcher _dispatcher = null!;

    public UiThread()
    {
        var ready = new ManualResetEventSlim();
        Exception? startupError = null;

        _thread = new Thread(() =>
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(_dispatcher));

                // Application.Current must exist before any window: MainWindow.xaml resolves its
                // brushes and styles from App.xaml's resource dictionary.
                var app = new App();
                app.InitializeComponent();

                // The app normally does this in OnStartup; the fallback prompt answers "no" so a
                // missing rules file fails the test instead of quietly scoring with the built-in copy.
                AppServices.Initialize((description, path, problem) =>
                    throw new InvalidOperationException($"{description} 로드 실패 ({path}): {problem}"));

                BindingErrors.Install();
            }
            catch (Exception ex)
            {
                startupError = ex;
            }
            finally
            {
                ready.Set();
            }

            if (startupError is null) Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "HfeLayoutSim UI test thread",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        ready.Wait(TimeSpan.FromMinutes(1));
        if (startupError is not null) ExceptionDispatchInfo.Capture(startupError).Throw();
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread and rethrows its exception here.</summary>
    public void Run(Action body) => Run(() => { body(); return Task.CompletedTask; });

    /// <summary>
    /// Runs an async body on the UI thread, pumping the dispatcher until it finishes so that
    /// continuations posted back to the UI thread (an <c>await</c> inside a command) actually run.
    /// </summary>
    public void Run(Func<Task> body)
    {
        Exception? error = null;
        var done = new ManualResetEventSlim();

        _dispatcher.InvokeAsync(async () =>
        {
            try { await body(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });

        if (!done.Wait(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("UI 스레드 작업이 2분 안에 끝나지 않았습니다 (교착 가능성).");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Lets queued dispatcher work (layout, background-priority callbacks) run to completion.</summary>
    public void Drain(DispatcherPriority until = DispatcherPriority.SystemIdle)
    {
        var done = new ManualResetEventSlim();
        _dispatcher.InvokeAsync(() => done.Set(), until);
        done.Wait(TimeSpan.FromSeconds(30));
    }

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(10));
    }
}

/// <summary>
/// Collects WPF data-binding failures. WPF reports a broken binding to a trace listener and then
/// leaves the control blank, so without this a dead panel looks like a passing test.
/// </summary>
public static class BindingErrors
{
    private static readonly List<string> Messages = new();
    private static readonly object Gate = new();

    public static void Install()
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new Listener());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;
    }

    public static void Clear()
    {
        lock (Gate) Messages.Clear();
    }

    public static IReadOnlyList<string> Drain()
    {
        lock (Gate) return Messages.ToList();
    }

    private sealed class Listener : TraceListener
    {
        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (Gate) Messages.Add(message);
        }
    }
}

[CollectionDefinition("wpf")]
public sealed class WpfCollection : ICollectionFixture<UiThread>;
