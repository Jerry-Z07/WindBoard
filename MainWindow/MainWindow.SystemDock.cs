using System.Windows;
using System.Windows.Controls.Primitives;

namespace WindBoard
{
    public partial class MainWindow
    {
        private void BtnMore_Click(object sender, RoutedEventArgs e)
        {
            // Toggle “更多”弹出菜单
            if (_popupMoreMenu == null)
                _popupMoreMenu = (Popup)FindName("PopupMoreMenu");
            if (_popupMoreMenu != null)
            {
                _popupMoreMenu.IsOpen = !_popupMoreMenu.IsOpen;
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            // 最小化窗口
            WindowState = WindowState.Minimized;
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            // 关闭菜单
            if (_popupMoreMenu != null) _popupMoreMenu.IsOpen = false;

            // 打开设置窗口（无业务依赖）
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            // 退出应用
            Application.Current.Shutdown();
        }
    }
}

