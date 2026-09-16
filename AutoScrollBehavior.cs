using System.Windows;
using System.Windows.Controls;

namespace BOOTWR
{
    /// <summary>
    /// 附加到 ScrollViewer 上，使内容增长时自动滚动到底部以显示最新内容。
    /// 当用户向上滚动浏览历史时暂停自动滚动，回到底部后自动恢复。
    /// </summary>
    public static class AutoScrollBehavior
    {
        public static readonly DependencyProperty AutoScrollProperty =
            DependencyProperty.RegisterAttached(
                "AutoScroll",
                typeof(bool),
                typeof(AutoScrollBehavior),
                new PropertyMetadata(false, OnAutoScrollChanged));

        public static bool GetAutoScroll(DependencyObject obj) =>
            (bool)obj.GetValue(AutoScrollProperty);

        public static void SetAutoScroll(DependencyObject obj, bool value) =>
            obj.SetValue(AutoScrollProperty, value);

        private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer sv) return;

            if ((bool)e.NewValue)
            {
                sv.ScrollChanged += OnScrollChanged;
                // 初始化时直接滚到底
                sv.Loaded += OnLoaded;
            }
            else
            {
                sv.ScrollChanged -= OnScrollChanged;
                sv.Loaded -= OnLoaded;
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToBottom();
                sv.Loaded -= OnLoaded;
            }
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;

            // 仅当内容高度增加（新条目到达）时才考虑自动滚动，
            // 避免用户向上滚动浏览历史时被打断。
            if (e.ExtentHeightChange > 0 && IsNearBottom(sv))
            {
                sv.ScrollToBottom();
            }
        }

        private static bool IsNearBottom(ScrollViewer sv)
        {
            // 50px 容差，避免浮点/像素对齐误差导致判断不在底部
            return sv.VerticalOffset >= sv.ScrollableHeight - 50;
        }
    }
}
