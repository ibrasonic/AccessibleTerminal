using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using System;

namespace AccessibleTerminal
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Handle unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception? ex = e.ExceptionObject as Exception;
            MessageBox.Show($"Fatal error: {ex?.Message}\n\nThe application will try to recover.", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Error: {e.Exception.Message}\n\nThe application will continue.", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true; // Prevent crash
        }
    }
}
