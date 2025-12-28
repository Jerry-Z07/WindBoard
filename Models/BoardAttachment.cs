using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace WindBoard
{
    public sealed class BoardAttachment : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private Guid _id = Guid.NewGuid();
        private BoardAttachmentType _type;
        private double _x;
        private double _y;
        private double _width = 320;
        private double _height = 180;
        private int _zIndex;
        private bool _isPinnedTop;
        private bool _isSelected;

        private string? _filePath;
        private string? _text;
        private string? _url;
        private ImageSource? _image;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public BoardAttachmentType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        public double Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        public int ZIndex
        {
            get => _zIndex;
            set { _zIndex = value; OnPropertyChanged(); }
        }

        public bool IsPinnedTop
        {
            get => _isPinnedTop;
            set { _isPinnedTop = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string? FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string? Text
        {
            get => _text;
            set
            {
                _text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string? Url
        {
            get => _url;
            set
            {
                _url = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public ImageSource? Image
        {
            get => _image;
            set
            {
                _image = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string DisplayName
        {
            get
            {
                return Type switch
                {
                    BoardAttachmentType.Image => !string.IsNullOrWhiteSpace(FilePath) ? Path.GetFileName(FilePath) : "图片",
                    BoardAttachmentType.Video => !string.IsNullOrWhiteSpace(FilePath) ? Path.GetFileName(FilePath) : "视频",
                    BoardAttachmentType.Text => !string.IsNullOrWhiteSpace(Text) ? "文本" : "空文本",
                    BoardAttachmentType.Link => !string.IsNullOrWhiteSpace(Url) ? Url : "链接",
                    _ => "附件"
                };
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
    }
}
