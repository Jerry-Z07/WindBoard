using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindBoard.Features.Dock.Services
{
    /// <summary>
    /// Dock 顺序应用器：按设置中的顺序调整 Dock 面板内元素顺序（不创建/销毁控件）。
    /// </summary>
    internal static class DockOrderApplier
    {
        internal static void Apply(StackPanel panel, IReadOnlyList<string> order, IReadOnlyDictionary<string, UIElement> elementsById)
        {
            if (panel is null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            if (order is null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            if (elementsById is null)
            {
                throw new ArgumentNullException(nameof(elementsById));
            }

            // 归一化已保证 order 只包含合法项并补齐缺失项，这里按 order 进行重排即可。
            panel.Children.Clear();

            foreach (string id in order)
            {
                if (elementsById.TryGetValue(id, out UIElement? element))
                {
                    panel.Children.Add(element);
                }
            }
        }
    }
}

