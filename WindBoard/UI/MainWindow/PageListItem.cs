using System;

namespace WindBoard
{
    /// <summary>
    /// 页面列表项（用于 UI 绑定）。
    /// 
    /// 说明：
    /// - 为避免把内部模型类型直接暴露给 XAML 绑定，这里用 <see cref="object"/> 承载页面实例。
    /// - UI 通过 <see cref="Number"/> 展示页码，通过 <see cref="Page"/> 传递给缩略图控件/事件处理。
    /// </summary>
    public sealed class PageListItem
    {
        internal PageListItem(object page, int number)
        {
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Number = number;
        }

        /// <summary>
        /// 1 基页码（用于展示）。
        /// </summary>
        public int Number { get; }

        /// <summary>
        /// 实际页面对象（内部为 BoardPage），用于 UI 事件回传/缩略图渲染。
        /// </summary>
        public object Page { get; }
    }
}

