using System;
using System.Collections.Generic;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 多页面工作区（类似“文档/工程”）。
    /// 
    /// - 负责页面的新增/删除/切换
    /// - 维护当前页索引
    /// - 通过事件向 UI 层广播状态变化
    /// 
    /// 注意：本类型仅包含纯逻辑，不依赖 UI/渲染，便于单元测试与后续导入/导出复用。
    /// </summary>
    internal sealed class BoardWorkspace
    {
        private readonly List<BoardPage> _pages = new();
        private int _currentIndex;

        public BoardWorkspace()
        {
            // 保证工作区至少有一页，避免 UI/调用方处理“0 页”的特殊分支。
            _pages.Add(new BoardPage());
            _currentIndex = 0;
        }

        /// <summary>
        /// 页面列表（顺序即页序）。
        /// </summary>
        public IReadOnlyList<BoardPage> Pages => _pages;

        /// <summary>
        /// 当前页索引（从 0 开始）。
        /// </summary>
        public int CurrentIndex => _currentIndex;

        /// <summary>
        /// 当前页。
        /// </summary>
        public BoardPage CurrentPage => _pages[_currentIndex];

        /// <summary>
        /// 页面集合发生变化（新增/删除/重排等）。
        /// </summary>
        public event Action? PagesChanged;

        /// <summary>
        /// 当前页发生变化（切换页面）。
        /// </summary>
        public event Action? CurrentPageChanged;

        /// <summary>
        /// 新增页面，并默认切换到新页面。
        /// </summary>
        /// <param name="insertIndex">可选插入位置；为空则追加到末尾。</param>
        public BoardPage AddPage(int? insertIndex = null)
        {
            int index = insertIndex ?? _pages.Count;
            index = Math.Clamp(index, 0, _pages.Count);

            var page = new BoardPage();
            _pages.Insert(index, page);

            PagesChanged?.Invoke();
            SetCurrentIndex(index);
            return page;
        }

        /// <summary>
        /// 删除指定页面。
        /// 
        /// 约定：工作区始终至少保留一页；当删除导致页数为 0 时，会自动补一个空白页。
        /// </summary>
        public bool RemovePage(BoardPage page)
        {
            if (page is null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            int index = _pages.IndexOf(page);
            if (index < 0)
            {
                return false;
            }

            _pages.RemoveAt(index);

            if (_pages.Count == 0)
            {
                _pages.Add(new BoardPage());
                _currentIndex = 0;
                PagesChanged?.Invoke();
                CurrentPageChanged?.Invoke();
                return true;
            }

            // 调整当前索引：删除当前页则选择相邻页；删除当前页之前的页则 currentIndex - 1。
            if (index < _currentIndex)
            {
                _currentIndex--;
            }
            else if (index == _currentIndex)
            {
                _currentIndex = Math.Min(_currentIndex, _pages.Count - 1);
            }

            PagesChanged?.Invoke();
            CurrentPageChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 尝试切换到上一页。
        /// </summary>
        public bool TryMoveToPreviousPage()
        {
            if (_currentIndex <= 0)
            {
                return false;
            }

            SetCurrentIndex(_currentIndex - 1);
            return true;
        }

        /// <summary>
        /// 尝试切换到下一页。
        /// </summary>
        public bool TryMoveToNextPage()
        {
            if (_currentIndex >= _pages.Count - 1)
            {
                return false;
            }

            SetCurrentIndex(_currentIndex + 1);
            return true;
        }

        /// <summary>
        /// 切换到指定索引的页面（越界会抛出异常）。
        /// </summary>
        public void SetCurrentIndex(int index)
        {
            if (index < 0 || index >= _pages.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (_currentIndex == index)
            {
                return;
            }

            _currentIndex = index;
            CurrentPageChanged?.Invoke();
        }
    }
}

