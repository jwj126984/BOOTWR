using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BOOTWR
{
    /// <summary>
    /// 附加到 ScrollViewer 或 DataGrid 上，使内容增长时自动滚动到底部以显示最新内容。
    /// 当用户向上滚动浏览历史时暂停自动滚动，回到底部后自动恢复。
    /// - ScrollViewer：内容高度增长且位于底部附近时 ScrollToBottom。
    /// - DataGrid（如 CAN 报文列表）：集合新增且位于底部附近时 ScrollIntoView 最后一项。
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

        private static readonly DependencyProperty DataGridScrollerProperty =
            DependencyProperty.RegisterAttached(
                "DataGridScroller",
                typeof(DataGridAutoScroller),
                typeof(AutoScrollBehavior),
                new PropertyMetadata(null));

        private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
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
                return;
            }

            if (d is DataGrid dg)
            {
                if ((bool)e.NewValue)
                {
                    var scroller = new DataGridAutoScroller(dg);
                    dg.SetValue(DataGridScrollerProperty, scroller);
                    scroller.Attach();
                }
                else
                {
                    if (dg.GetValue(DataGridScrollerProperty) is DataGridAutoScroller old)
                    {
                        old.Detach();
                        dg.SetValue(DataGridScrollerProperty, null);
                    }
                }
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

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv)
                    return sv;
                var found = FindScrollViewer(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        /// <summary>DataGrid 自动滚动辅助：集合新增时滚动到最后一项，用户上滑时暂停。</summary>
        private sealed class DataGridAutoScroller
        {
            private readonly DataGrid _dataGrid;
            private ScrollViewer? _scrollViewer;
            private INotifyCollectionChanged? _items;
            private bool _pendingScroll;
            private bool _attached;

            public DataGridAutoScroller(DataGrid dataGrid)
            {
                _dataGrid = dataGrid;
            }

            public void Attach()
            {
                if (_attached)
                    return;
                _attached = true;

                _dataGrid.Loaded += OnLoaded;
                _dataGrid.Unloaded += OnUnloaded;

                // 附加属性在元素已加载后才设置（少见）时，Loaded 不会再触发，立即初始化
                if (_dataGrid.IsLoaded)
                    OnLoaded(_dataGrid, new RoutedEventArgs());
            }

            public void Detach()
            {
                if (!_attached)
                    return;
                _attached = false;

                _dataGrid.Loaded -= OnLoaded;
                _dataGrid.Unloaded -= OnUnloaded;
                _dataGrid.LayoutUpdated -= OnLayoutUpdated;

                if (_items != null)
                    _items.CollectionChanged -= OnItemsChanged;
                _items = null;

                if (_scrollViewer != null)
                    _scrollViewer.ScrollChanged -= OnScrollChanged;
                _scrollViewer = null;
            }

            private void OnLoaded(object sender, RoutedEventArgs e)
            {
                _scrollViewer = FindScrollViewer(_dataGrid);
                if (_scrollViewer != null)
                {
                    _scrollViewer.ScrollChanged += OnScrollChanged;
                }

                // 附加属性可能先于 ItemsSource 绑定求值，Loaded 时重新获取数据源
                var items = _dataGrid.ItemsSource as INotifyCollectionChanged;
                if (!ReferenceEquals(items, _items))
                {
                    if (_items != null)
                        _items.CollectionChanged -= OnItemsChanged;
                    _items = items;
                    if (_items != null)
                        _items.CollectionChanged += OnItemsChanged;
                }

                // 初始化时直接滚到底，显示最新报文
                ScrollToLastItem();
            }

            private void OnUnloaded(object sender, RoutedEventArgs e)
            {
                // 仅解除视觉树引用；集合订阅保留，重新加载后自动恢复
                if (_scrollViewer != null)
                {
                    _scrollViewer.ScrollChanged -= OnScrollChanged;
                    _scrollViewer = null;
                }
            }

            private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
            {
                // 用户滚动位置通过 ScrollToLastItem 中的 IsNearBottom 判断，这里无需处理
            }

            private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            {
                if (e.Action == NotifyCollectionChangedAction.Add ||
                    e.Action == NotifyCollectionChangedAction.Reset)
                {
                    // 同一渲染帧内多次新增合并为一次滚动，避免高频报文下堆积滚动操作
                    _pendingScroll = true;
                    _dataGrid.LayoutUpdated -= OnLayoutUpdated;
                    _dataGrid.LayoutUpdated += OnLayoutUpdated;
                }
            }

            private void OnLayoutUpdated(object? sender, EventArgs e)
            {
                _dataGrid.LayoutUpdated -= OnLayoutUpdated;
                if (!_pendingScroll)
                    return;
                _pendingScroll = false;
                ScrollToLastItem();
            }
        
            private void ScrollToLastItem()
            {
                var items = _dataGrid.Items;
                if (items.Count == 0)
                    return;

                // 用户向上滚动浏览历史时暂停自动滚动，回到底部后恢复
                if (_scrollViewer != null && !IsNearBottom(_scrollViewer))
                    return;

                object last = items[items.Count - 1]!;
                _dataGrid.ScrollIntoView(last);
            }
        }
    }
}
