using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using WindBoard.Views.Dialogs;

namespace WindBoard
{
    public partial class MainWindow
    {
        private const double AttachmentMinSize = 60;

        private BoardAttachment? _selectedAttachment;

        private Border? _attachmentSelectionFrame;
        private Thumb? _attachmentMoveThumb;
        private readonly Dictionary<string, Thumb> _attachmentResizeThumbs = new();

         private Border? _selectionDock;
         private Button? _btnSelectionTop;
         private Button? _btnSelectionCopy;
         private bool _selectionDockUpdateScheduled;

        private readonly StaBitmapLoader _bitmapLoader = new();

         private void InitializeAttachmentUi()
         {
             _selectionDock = (Border)FindName("SelectionDock");
             _btnSelectionTop = (Button)FindName("BtnSelectionTop");
             _btnSelectionCopy = (Button)FindName("BtnSelectionCopy");

             if (MyCanvas != null)
             {
                MyCanvas.SelectionChanged -= MyCanvas_SelectionChanged;
                MyCanvas.SelectionChanged += MyCanvas_SelectionChanged;

                MyCanvas.SelectionMoved -= MyCanvas_SelectionMoved;
                MyCanvas.SelectionMoved += MyCanvas_SelectionMoved;

                MyCanvas.SelectionResized -= MyCanvas_SelectionResized;
                MyCanvas.SelectionResized += MyCanvas_SelectionResized;
            }

            BuildAttachmentSelectionOverlay();

            if (Viewport == null) return;
            Viewport.SizeChanged -= Viewport_SizeChanged;
            Viewport.SizeChanged += Viewport_SizeChanged;
         }
    }
}
