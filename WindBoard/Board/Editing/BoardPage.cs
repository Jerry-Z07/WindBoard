using System;

namespace WindBoard.Board.Editing
{
    /// <summary>
    /// 画板“页面”：一个页面对应一个独立的 <see cref="BoardSession"/>，笔迹数据（<see cref="BoardSession.Document"/>）与撤销/重做栈各自隔离。
    /// 切换页面后上一页的撤销历史不会被新页面污染。
    /// </summary>
    internal sealed class BoardPage
    {
        public BoardPage()
        {
            Id = Guid.NewGuid();
            Session = new BoardSession();
        }

        public BoardPage(Guid id, BoardSession session)
        {
            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        /// <summary>
        /// 页面唯一标识（用于后续导入/导出、跨集合定位等）。
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// 页面对应的编辑会话。
        /// </summary>
        public BoardSession Session { get; }
    }
}
