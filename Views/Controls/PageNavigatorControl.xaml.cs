using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WindBoard;

namespace WindBoard.Controls
{
    public partial class PageNavigatorControl : UserControl
    {
        private Popup? _popupPageManager;

        public PageNavigatorControl()
        {
            InitializeComponent();
            Loaded += PageNavigatorControl_Loaded;
        }

        // Pages 集合（由宿主绑定）
        public ObservableCollection<BoardPage>? Pages
        {
            get => (ObservableCollection<BoardPage>?)GetValue(PagesProperty);
            set => SetValue(PagesProperty, value);
        }
        public static readonly DependencyProperty PagesProperty =
            DependencyProperty.Register(nameof(Pages), typeof(ObservableCollection<BoardPage>), typeof(PageNavigatorControl), new PropertyMetadata(null));

        // 是否多页（由宿主绑定）
        public bool IsMultiPage
        {
            get => (bool)GetValue(IsMultiPageProperty);
            set => SetValue(IsMultiPageProperty, value);
        }
        public static readonly DependencyProperty IsMultiPageProperty =
            DependencyProperty.Register(nameof(IsMultiPage), typeof(bool), typeof(PageNavigatorControl), new PropertyMetadata(false));

        // 指示器文本（由宿主绑定）
        public string? PageIndicatorText
        {
            get => (string?)GetValue(PageIndicatorTextProperty);
            set => SetValue(PageIndicatorTextProperty, value);
        }
        public static readonly DependencyProperty PageIndicatorTextProperty =
            DependencyProperty.Register(nameof(PageIndicatorText), typeof(string), typeof(PageNavigatorControl), new PropertyMetadata(string.Empty));

        // —— 对外事件：交给宿主处理页面切换/新增/删除/刷新预览 ——
        public event RoutedEventHandler? PrevRequested;
        public event RoutedEventHandler? NextRequested;
        public event RoutedEventHandler? AddRequested;
        public event RoutedEventHandler? IndicatorClicked; // 打开弹窗前通知宿主刷新预览

        public event EventHandler<BoardPageEventArgs>? PageSelected;          // 选中要切到的页
        public event EventHandler<BoardPageEventArgs>? PageDeleteRequested;   // 请求删除某页
        public event EventHandler<BoardPageEventArgs>? PreviewNeeded;         // 某项进入可视区域，需要预览图

        // —— 本地按钮事件（转发为上述公共事件） ——
        private void BtnPrev_Click(object sender, RoutedEventArgs e)
            => PrevRequested?.Invoke(this, e);

        private void BtnNext_Click(object sender, RoutedEventArgs e)
            => NextRequested?.Invoke(this, e);

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
            => AddRequested?.Invoke(this, e);

        private void BtnIndicator_Click(object sender, RoutedEventArgs e)
        {
            // 先通知宿主：可用于 SaveCurrentPage + 更新缩略图
            IndicatorClicked?.Invoke(this, e);
            // 再打开弹窗（自身控制）
            if (FindName("PopupPageManager") is Popup popup)
            {
                popup.IsOpen = true;
            }
        }

        private void PageItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is BoardPage page)
            {
                PageSelected?.Invoke(this, new BoardPageEventArgs(page));
                // 选中后关闭弹窗
                if (FindName("PopupPageManager") is Popup popup) popup.IsOpen = false;
            }
        }

        private void DeletePage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is BoardPage page)
            {
                PageDeleteRequested?.Invoke(this, new BoardPageEventArgs(page));
            }
        }

        private void PreviewHost_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is BoardPage page)
            {
                if (page.Preview == null || page.PreviewVersion != page.ContentVersion)
                {
                    PreviewNeeded?.Invoke(this, new BoardPageEventArgs(page));
                }
            }
        }

        private void PageNavigatorControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_popupPageManager == null)
            {
                _popupPageManager = FindName("PopupPageManager") as Popup;
                if (_popupPageManager != null)
                {
                    _popupPageManager.Closed -= PopupPageManager_Closed;
                    _popupPageManager.Closed += PopupPageManager_Closed;
                }
            }
        }

        private void PopupPageManager_Closed(object? sender, EventArgs e)
        {
            // 页面管理关闭后释放所有缩略图引用，避免长期占用内存。
            if (Pages == null) return;
            foreach (var p in Pages)
            {
                p.Preview = null;
                p.PreviewVersion = 0;
            }
        }
    }

    public sealed class BoardPageEventArgs(BoardPage page) : EventArgs
    {
        public BoardPage Page { get; } = page;
    }
}
