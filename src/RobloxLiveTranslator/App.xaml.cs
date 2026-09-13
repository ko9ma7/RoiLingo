using System.Windows;

namespace RobloxLiveTranslator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "예기치 못한 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
