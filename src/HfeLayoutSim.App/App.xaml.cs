using System.Windows;
using HfeLayoutSim.App.Services;

namespace HfeLayoutSim.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            AppServices.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"규칙 지식베이스를 불러오지 못해 프로그램을 시작할 수 없습니다.\n\n{ex.Message}",
                "HFE Layout Simulator — 시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        new MainWindow().Show();
    }
}
