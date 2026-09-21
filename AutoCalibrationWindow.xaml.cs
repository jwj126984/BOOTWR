using System.Windows;
using BOOTWR.ViewModels;

namespace BOOTWR
{
    public partial class AutoCalibrationWindow : Window
    {
        private readonly AutoCalibrationViewModel _viewModel;

        public AutoCalibrationWindow(MainWindowViewModel mainViewModel)
        {
            InitializeComponent();
            _viewModel = new AutoCalibrationViewModel(mainViewModel, this);
            DataContext = _viewModel;
            Closing += AutoCalibrationWindow_Closing;
        }

        private void AutoCalibrationWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel.IsRunning)
            {
                _viewModel.StopCalibrationCommand.Execute(null);
            }
        }
    }
}
