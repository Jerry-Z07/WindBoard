using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using WindBoard.Models.Wbi;
using WindBoard.Services.Export;

namespace WindBoard.Views.Dialogs
{
    public partial class ImportDialog : UserControl
    {
        private sealed class Vm : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            public ObservableCollection<string> ImagePaths { get; } = new();
            public ObservableCollection<string> VideoPaths { get; } = new();
            public ObservableCollection<string> TextFilePaths { get; } = new();

            private string? _textContent;
            private string? _linkLines;

            public string? TextContent
            {
                get => _textContent;
                set { _textContent = value; OnPropertyChanged(); }
            }

            public string? LinkLines
            {
                get => _linkLines;
                set { _linkLines = value; OnPropertyChanged(); }
            }

            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
        }

        private readonly Vm _vm = new();
        private readonly WbiImporter _wbiImporter = new();

        // WBI 导入相关
        private string? _selectedWbiPath;
        private WbiManifest? _selectedWbiManifest;

        public ImportDialog()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        private void PickImages_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择图片",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tiff;*.tif;*.ico|所有文件|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            foreach (var p in dlg.FileNames.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                _vm.ImagePaths.Add(p);
            }
        }

        private void ClearImages_Click(object sender, RoutedEventArgs e) => _vm.ImagePaths.Clear();

        private void PickVideos_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择视频",
                Filter = "视频|*.mp4;*.mov;*.mkv;*.avi;*.wmv;*.webm|所有文件|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            foreach (var p in dlg.FileNames.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                _vm.VideoPaths.Add(p);
            }
        }

        private void ClearVideos_Click(object sender, RoutedEventArgs e) => _vm.VideoPaths.Clear();

        private void PickTextFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择文本文件",
                Filter = "文本|*.txt;*.md;*.log;*.json;*.xml;*.csv|所有文件|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            foreach (var p in dlg.FileNames.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                _vm.TextFilePaths.Add(p);
            }
        }

        private void ClearTextFiles_Click(object sender, RoutedEventArgs e) => _vm.TextFilePaths.Clear();

        private void PasteText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    _vm.TextContent = Clipboard.GetText();
                }
            }
            catch
            {
                // ignore clipboard errors
            }
        }

        private void PasteLinks_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    _vm.LinkLines = Clipboard.GetText();
                }
            }
            catch
            {
                // ignore clipboard errors
            }
        }

        private void ClearLinks_Click(object sender, RoutedEventArgs e) => _vm.LinkLines = string.Empty;

        private void PickWbiFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择 WBI 文件",
                Filter = "WindBoard 文件|*.wbi|所有文件|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            LoadWbiFile(dlg.FileName);
        }

        private void LoadWbiFile(string filePath)
        {
            try
            {
                var manifest = _wbiImporter.GetManifest(filePath);
                if (manifest == null)
                {
                    MessageBox.Show("无法读取 WBI 文件信息，文件可能已损坏。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _selectedWbiPath = filePath;
                _selectedWbiManifest = manifest;

                // 更新 UI
                TxtWbiFileName.Text = Path.GetFileName(filePath);
                TxtWbiPageCount.Text = $"包含 {manifest.PageCount} 个页面";
                TxtWbiCreatedAt.Text = $"创建时间: {manifest.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";

                WbiFileInfo.Visibility = Visibility.Visible;
                WbiImportMode.Visibility = Visibility.Visible;
                BtnClearWbi.Visibility = Visibility.Visible;

                // 检查是否有外部资源提示
                if (!manifest.IncludeImageAssets)
                {
                    TxtWbiWarning.Text = "此文件未包含图片附件原始文件，导入后可能需要重新关联图片路径。";
                    TxtWbiWarning.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtWbiWarning.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 WBI 文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearWbiFile_Click(object sender, RoutedEventArgs e)
        {
            _selectedWbiPath = null;
            _selectedWbiManifest = null;

            WbiFileInfo.Visibility = Visibility.Collapsed;
            WbiImportMode.Visibility = Visibility.Collapsed;
            BtnClearWbi.Visibility = Visibility.Collapsed;
            TxtWbiWarning.Visibility = Visibility.Collapsed;
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否选择了 WBI 文件
            if (!string.IsNullOrEmpty(_selectedWbiPath) && _selectedWbiManifest != null)
            {
                var wbiRequest = new WbiImportRequest
                {
                    FilePath = _selectedWbiPath,
                    Manifest = _selectedWbiManifest,
                    ReplaceExistingPages = RbReplacePages.IsChecked == true
                };
                DialogHost.CloseDialogCommand.Execute(wbiRequest, this);
                return;
            }

            // 常规导入请求
            var req = new ImportRequest();

            req.ImagePaths.AddRange(_vm.ImagePaths.Distinct(StringComparer.OrdinalIgnoreCase));
            req.VideoPaths.AddRange(_vm.VideoPaths.Distinct(StringComparer.OrdinalIgnoreCase));
            req.TextFilePaths.AddRange(_vm.TextFilePaths.Distinct(StringComparer.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(_vm.TextContent))
            {
                req.TextContent = _vm.TextContent;
            }

            if (!string.IsNullOrWhiteSpace(_vm.LinkLines))
            {
                var urls = _vm.LinkLines
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var u in urls)
                {
                    req.Urls.Add(u);
                }
            }

            DialogHost.CloseDialogCommand.Execute(req, this);
        }
    }

    /// <summary>
    /// WBI 导入请求
    /// </summary>
    public sealed class WbiImportRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public WbiManifest? Manifest { get; set; }
        public bool ReplaceExistingPages { get; set; }
    }
}
