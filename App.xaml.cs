using System;
using System.Configuration;
using System.Data;
using System.Windows;

namespace BOOTWR
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"应用程序发生未处理异常: {e.Exception.Message}\n\n{e.Exception.StackTrace}", 
                "异常错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            MessageBox.Show($"应用程序发生致命异常: {ex?.Message ?? "未知错误"}", 
                "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
