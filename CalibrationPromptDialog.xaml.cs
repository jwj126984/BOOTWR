using System.Windows;

namespace BOOTWR
{
    public partial class CalibrationPromptDialog : Window
    {
        public CalibrationPromptDialog(string message, string buttonText = "已设置完成")
        {
            InitializeComponent();
            MessageText.Text = message;
            ConfirmButton.Content = buttonText;
            ConfirmButton.Focus();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
