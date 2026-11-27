using System;
using System.Collections.Generic;

namespace WindBoard.Features.Dock.Models
{
    /// <summary>
    /// Dock 布局的纯规则：隐藏项归一化与可见顺序过滤。
    /// </summary>
    internal static class DockLayoutRules
    {
        /// <summary>
        /// 「更多」是设置与 Dock 编辑入口，隐藏后无法恢复，因此永远不允许隐藏。
        /// </summary>
        internal static bool CanHide(string id)
        {
            return !string.Equals(id, DockItemIds.More, StringComparison.Ordinal);
        }

        /// <summary>
        /// 过滤未知/空白/不可隐藏 id，并按首次出现去重。
        /// </summary>
        internal static List<string> NormalizeHiddenItems(IEnumerable<string>? hiddenItems)
        {
            var normalized = new List<string>();
            if (hiddenItems is null)
            {
                return normalized;
            }

            HashSet<string> allowed = new(DockSettingsDefaults.All, StringComparer.Ordinal);
            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (string? rawId in hiddenItems)
            {
                if (string.IsNullOrWhiteSpace(rawId))
                {
                    continue;
                }

                string id = rawId.Trim();
                if (!allowed.Contains(id) || !CanHide(id) || !seen.Add(id))
                {
                    continue;
                }

                normalized.Add(id);
            }

            return normalized;
        }

        /// <summary>
        /// 按原顺序返回未隐藏的项 id。
        /// </summary>
        internal static List<string> FilterVisibleOrder(
            IReadOnlyList<string> order,
            IReadOnlySet<string> hiddenIds)
        {
            ArgumentNullException.ThrowIfNull(order);
            ArgumentNullException.ThrowIfNull(hiddenIds);

            var visible = new List<string>(order.Count);
            foreach (string id in order)
            {
                if (!hiddenIds.Contains(id))
                {
                    visible.Add(id);
                }
            }

            return visible;
        }
    }
}
