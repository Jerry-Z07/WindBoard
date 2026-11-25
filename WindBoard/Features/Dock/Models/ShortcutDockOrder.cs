using System;
using System.Collections.Generic;
using System.Linq;

namespace WindBoard.Features.Dock.Models
{
    /// <summary>
    /// 快捷入口的侧别与顺序调整（纯规则，便于脱离 UI 测试）。
    /// </summary>
    internal static class ShortcutDockOrder
    {
        /// <summary>
        /// 把指定快捷入口移动到目标侧的目标下标。返回按“左侧全部 + 右侧全部”重建的新列表；
        /// 传入列表与元素对象不被增删，只有被移动项的 <see cref="ShortcutDockItemSettings.Side"/> 可能改变。
        /// </summary>
        internal static List<ShortcutDockItemSettings> Move(
            IReadOnlyList<ShortcutDockItemSettings> items,
            string itemId,
            string targetSide,
            int targetIndex)
        {
            ArgumentNullException.ThrowIfNull(items);

            var left = items
                .Where(item => !string.Equals(item.Side, ShortcutDockSides.Right, StringComparison.Ordinal))
                .ToList();
            var right = items
                .Where(item => string.Equals(item.Side, ShortcutDockSides.Right, StringComparison.Ordinal))
                .ToList();

            ShortcutDockItemSettings? moved = items.FirstOrDefault(item => item.Id == itemId);
            if (moved is null)
            {
                return new List<ShortcutDockItemSettings>(items);
            }

            bool targetRight = string.Equals(targetSide, ShortcutDockSides.Right, StringComparison.Ordinal);
            List<ShortcutDockItemSettings> target = targetRight ? right : left;

            if (string.Equals(moved.Side, targetSide, StringComparison.Ordinal))
            {
                int fromIndex = target.IndexOf(moved);
                target.Remove(moved);
                if (fromIndex >= 0 && fromIndex < targetIndex)
                {
                    targetIndex--;
                }
            }
            else
            {
                left.Remove(moved);
                right.Remove(moved);
                moved.Side = targetRight ? ShortcutDockSides.Right : ShortcutDockSides.Left;
            }

            targetIndex = Math.Clamp(targetIndex, 0, target.Count);
            target.Insert(targetIndex, moved);

            var result = new List<ShortcutDockItemSettings>(items.Count);
            result.AddRange(left);
            result.AddRange(right);
            return result;
        }
    }
}
