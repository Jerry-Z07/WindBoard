using System;

namespace WindBoard.Settings
{
    /// <summary>
    /// 多击（连击）检测器：在指定时间窗口内累计点击次数，达到阈值后触发一次并自动重置。
    /// 
    /// 设计目标：
    /// - 纯逻辑可单测（由调用方传入时间戳）
    /// - 防御时钟回拨（now &lt; lastTapAt 时重置计数）
    /// </summary>
    internal sealed class MultiTapGestureDetector
    {
        private readonly int _requiredTaps;
        private readonly TimeSpan _maxInterval;

        private int _count;
        private DateTimeOffset _lastTapAt;

        internal MultiTapGestureDetector(int requiredTaps, TimeSpan maxInterval)
        {
            if (requiredTaps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredTaps), "requiredTaps 必须大于 0");
            }

            if (maxInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxInterval), "maxInterval 必须大于 0");
            }

            _requiredTaps = requiredTaps;
            _maxInterval = maxInterval;
        }

        internal bool RegisterTap(DateTimeOffset now)
        {
            if (_count == 0)
            {
                _count = 1;
                _lastTapAt = now;
                return _requiredTaps == 1;
            }

            TimeSpan sinceLast = now - _lastTapAt;
            if (sinceLast < TimeSpan.Zero || sinceLast > _maxInterval)
            {
                // 超时或时钟回拨：重新开始计数。
                _count = 1;
                _lastTapAt = now;
                return _requiredTaps == 1;
            }

            _count++;
            _lastTapAt = now;

            if (_count >= _requiredTaps)
            {
                Reset();
                return true;
            }

            return false;
        }

        internal void Reset()
        {
            _count = 0;
            _lastTapAt = default;
        }
    }
}

