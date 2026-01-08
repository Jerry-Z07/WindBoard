using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Ink;
using System.Windows.Media;
using WindBoard.Services;

namespace WindBoard
{
    public class BoardPage : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int _number;
        private bool _isCurrent;
        private ImageSource? _preview;

        internal int ContentVersion { get; set; }
        internal int PreviewVersion { get; set; }
        internal StrokeUndoHistory UndoHistory { get; } = new StrokeUndoHistory();

        public int Number
        {
            get => _number;
            set { _number = value; OnPropertyChanged(); }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set { _isCurrent = value; OnPropertyChanged(); }
        }

        public ImageSource? Preview
        {
            get => _preview;
            set { _preview = value; OnPropertyChanged(); }
        }

        // 页面内容
        public StrokeCollection Strokes { get; set; } = new StrokeCollection();

        // 页面附件（导入的图片/视频/文本/链接等）
        public ObservableCollection<BoardAttachment> Attachments { get; } = new ObservableCollection<BoardAttachment>();

        // 每页画布大小
        public double CanvasWidth { get; set; } = 8000;
        public double CanvasHeight { get; set; } = 8000;

        // 每页视图状态（可选但体验更好）
        public double Zoom { get; set; } = 1.0;
        public double PanX { get; set; } = 0.0;
        public double PanY { get; set; } = 0.0;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
    }
}
