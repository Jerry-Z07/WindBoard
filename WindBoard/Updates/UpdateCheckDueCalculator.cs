using System;
using WindBoard.Settings;

namespace WindBoard.Updates
{
    /// <summary>
    /// 自动更新检查“是否到期”的计算逻辑（纯计算，便于单测）。
    /// </summary>
    internal static class UpdateCheckDueCalculator
    {
        internal static bool IsDue(UpdateCheckInterval interval, DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc)
        {
            if (interval == UpdateCheckInterval.Never)
            {
                return false;
            }

            if (lastCheckUtc is null)
            {
                return true;
            }

            TimeSpan period = interval switch
            {
                UpdateCheckInterval.Biweekly => TimeSpan.FromDays(14),
                UpdateCheckInterval.Monthly => TimeSpan.FromDays(30),
                _ => TimeSpan.FromDays(7),
            };

            // 防御：lastCheckUtc 未来时间视为“需要检查”，避免被手工编辑卡死。
            if (lastCheckUtc.Value > nowUtc)
            {
                return true;
            }

            return nowUtc - lastCheckUtc.Value >= period;
        }
    }
}

