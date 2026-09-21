using System.Collections.Generic;
using System.Windows;

namespace BOOTWR
{
    public enum ConfirmResult
    {
        Confirm,
        Recollect,
        Cancel
    }

    public class VoltageRow
    {
        public int Tc { get; set; }
        public string TcLabel => $"TC {Tc}";
        public string X { get; set; } = string.Empty;
        public string Y { get; set; } = string.Empty;
    }

    public partial class CalibrationConfirmDialog : Window
    {
        public ConfirmResult Result { get; private set; } = ConfirmResult.Cancel;

        public CalibrationConfirmDialog(int afe, int mv, float[] xValues, float[] yValues)
        {
            InitializeComponent();
            TitleText.Text = $"AFE{afe} · {mv}mV 采集电压确认";

            var rows = new List<VoltageRow>(xValues.Length);
            for (int i = 0; i < xValues.Length; i++)
            {
                rows.Add(new VoltageRow
                {
                    Tc = i,
                    X = $"{xValues[i]:F4} V",
                    Y = i < yValues.Length ? $"{yValues[i]:F4} V" : "--"
                });
            }
            VoltageList.ItemsSource = rows;
        }

        /// <summary>模态显示并返回用户选择；直接关闭窗口时按取消处理。</summary>
        public ConfirmResult ShowResult()
        {
            ShowDialog();
            return Result;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ConfirmResult.Confirm;
            DialogResult = true;
        }

        private void RecollectButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ConfirmResult.Recollect;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ConfirmResult.Cancel;
            DialogResult = true;
        }
    }
}
