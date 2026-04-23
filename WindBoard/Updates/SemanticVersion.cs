using System;
using System.Collections.Generic;

namespace WindBoard.Updates
{
    /// <summary>
    /// 语义版本（SemVer）解析与比较（用于“是否有新版本”判断）。
    /// </summary>
    internal readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        internal int Major { get; }
        internal int Minor { get; }
        internal int Patch { get; }

        internal bool IsPrerelease => _prereleaseIds.Length > 0;

        private readonly PrereleaseId[] _prereleaseIds;

        private SemanticVersion(int major, int minor, int patch, PrereleaseId[] prereleaseIds)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            _prereleaseIds = prereleaseIds ?? [];
        }

        public int CompareTo(SemanticVersion other)
        {
            int major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            int minor = Minor.CompareTo(other.Minor);
            if (minor != 0)
            {
                return minor;
            }

            int patch = Patch.CompareTo(other.Patch);
            if (patch != 0)
            {
                return patch;
            }

            // SemVer：同 core 版本时，带 prerelease 的版本优先级更低。
            if (!IsPrerelease && !other.IsPrerelease)
            {
                return 0;
            }

            if (!IsPrerelease)
            {
                return 1;
            }

            if (!other.IsPrerelease)
            {
                return -1;
            }

            int len = Math.Min(_prereleaseIds.Length, other._prereleaseIds.Length);
            for (int i = 0; i < len; i++)
            {
                int cmp = _prereleaseIds[i].CompareTo(other._prereleaseIds[i]);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            // 共同前缀相同：更短的 prerelease 列表优先级更低。
            return _prereleaseIds.Length.CompareTo(other._prereleaseIds.Length);
        }

        public bool Equals(SemanticVersion other)
        {
            return CompareTo(other) == 0;
        }

        public override bool Equals(object? obj)
        {
            return obj is SemanticVersion other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Major);
            hash.Add(Minor);
            hash.Add(Patch);
            foreach (PrereleaseId id in _prereleaseIds)
            {
                hash.Add(id.GetHashCode());
            }

            return hash.ToHashCode();
        }

        internal static bool TryParse(string? text, out SemanticVersion version)
        {
            version = default;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string value = text.Trim();

            // 兼容：允许 v 前缀（例如 v2.0.0）。
            if (value.Length > 1 && (value[0] == 'v' || value[0] == 'V'))
            {
                value = value.Substring(1);
            }

            // 忽略 build metadata（+xxx）。
            int plusIndex = value.IndexOf('+', StringComparison.Ordinal);
            if (plusIndex >= 0)
            {
                value = value.Substring(0, plusIndex);
            }

            string core = value;
            string? prerelease = null;
            int dashIndex = value.IndexOf('-', StringComparison.Ordinal);
            if (dashIndex >= 0)
            {
                core = value.Substring(0, dashIndex);
                prerelease = value.Substring(dashIndex + 1);
            }

            if (!TryParseCore(core, out int major, out int minor, out int patch))
            {
                return false;
            }

            PrereleaseId[] prereleaseIds = [];
            if (!string.IsNullOrEmpty(prerelease))
            {
                if (!TryParsePrerelease(prerelease, out prereleaseIds))
                {
                    return false;
                }
            }

            version = new SemanticVersion(major, minor, patch, prereleaseIds);
            return true;
        }

        private static bool TryParseCore(string core, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;

            if (string.IsNullOrWhiteSpace(core))
            {
                return false;
            }

            string[] parts = core.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            return int.TryParse(parts[0], out major)
                   && int.TryParse(parts[1], out minor)
                   && int.TryParse(parts[2], out patch)
                   && major >= 0
                   && minor >= 0
                   && patch >= 0;
        }

        private static bool TryParsePrerelease(string prerelease, out PrereleaseId[] ids)
        {
            ids = [];

            if (string.IsNullOrWhiteSpace(prerelease))
            {
                return false;
            }

            string[] parts = prerelease.Split('.');
            var list = new List<PrereleaseId>(parts.Length);

            foreach (string raw in parts)
            {
                string p = raw?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(p))
                {
                    return false;
                }

                if (!IsValidPrereleaseChars(p))
                {
                    return false;
                }

                if (IsAllDigits(p))
                {
                    // SemVer：数字标识不允许前导 0（除非就是 "0"）。
                    if (p.Length > 1 && p[0] == '0')
                    {
                        return false;
                    }

                    if (!int.TryParse(p, out int number) || number < 0)
                    {
                        return false;
                    }

                    list.Add(PrereleaseId.Numeric(number));
                }
                else
                {
                    list.Add(PrereleaseId.Text(p));
                }
            }

            ids = list.ToArray();
            return ids.Length > 0;
        }

        private static bool IsAllDigits(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidPrereleaseChars(string text)
        {
            // SemVer：仅允许 [0-9A-Za-z-]
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    continue;
                }

                if (c >= 'A' && c <= 'Z')
                {
                    continue;
                }

                if (c >= 'a' && c <= 'z')
                {
                    continue;
                }

                if (c == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private readonly struct PrereleaseId : IComparable<PrereleaseId>
        {
            private readonly bool _isNumeric;
            private readonly int _number;
            private readonly string _text;

            private PrereleaseId(bool isNumeric, int number, string text)
            {
                _isNumeric = isNumeric;
                _number = number;
                _text = text ?? string.Empty;
            }

            internal static PrereleaseId Numeric(int number) => new(true, number, string.Empty);

            internal static PrereleaseId Text(string text) => new(false, 0, text ?? string.Empty);

            public int CompareTo(PrereleaseId other)
            {
                if (_isNumeric && other._isNumeric)
                {
                    return _number.CompareTo(other._number);
                }

                // 数字标识优先级更低（更小）。
                if (_isNumeric != other._isNumeric)
                {
                    return _isNumeric ? -1 : 1;
                }

                return string.Compare(_text, other._text, StringComparison.Ordinal);
            }

            public override int GetHashCode()
            {
                return _isNumeric
                    ? HashCode.Combine(true, _number)
                    : HashCode.Combine(false, _text, StringComparer.Ordinal);
            }
        }
    }
}

