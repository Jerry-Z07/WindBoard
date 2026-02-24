using System;
using System.Collections.Generic;
using Windows.System;

namespace WindBoard.Features.Shortcuts.Models
{
    /// <summary>
    /// 键盘快捷键手势（Key + Modifiers）。
    /// </summary>
    internal readonly record struct KeyboardShortcutGesture(VirtualKey Key, VirtualKeyModifiers Modifiers)
    {
        private static readonly Dictionary<string, VirtualKey> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = VirtualKey.Escape,
            ["Escape"] = VirtualKey.Escape,
            ["Enter"] = VirtualKey.Enter,
            ["Return"] = VirtualKey.Enter,
            ["Space"] = VirtualKey.Space,
            ["Spacebar"] = VirtualKey.Space,
            ["Back"] = VirtualKey.Back,
            ["Backspace"] = VirtualKey.Back,
            ["Del"] = VirtualKey.Delete,
            ["Delete"] = VirtualKey.Delete,
            ["Home"] = VirtualKey.Home,
            ["End"] = VirtualKey.End,
            ["PageUp"] = VirtualKey.PageUp,
            ["PgUp"] = VirtualKey.PageUp,
            ["PageDown"] = VirtualKey.PageDown,
            ["PgDn"] = VirtualKey.PageDown,
            ["Up"] = VirtualKey.Up,
            ["Down"] = VirtualKey.Down,
            ["Left"] = VirtualKey.Left,
            ["Right"] = VirtualKey.Right,
        };

        internal static bool TryParse(string text, out KeyboardShortcutGesture gesture)
        {
            gesture = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            VirtualKeyModifiers modifiers = VirtualKeyModifiers.None;
            VirtualKey? key = null;

            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (IsControlToken(token))
                {
                    modifiers |= VirtualKeyModifiers.Control;
                    continue;
                }

                if (IsAltToken(token))
                {
                    modifiers |= VirtualKeyModifiers.Menu;
                    continue;
                }

                if (IsShiftToken(token))
                {
                    modifiers |= VirtualKeyModifiers.Shift;
                    continue;
                }

                // 其它均视为 Key；同一个快捷键中只允许出现一个 Key。
                if (key is not null)
                {
                    return false;
                }

                if (!TryParseKeyToken(token, out VirtualKey parsedKey))
                {
                    return false;
                }

                key = parsedKey;
            }

            if (key is null)
            {
                return false;
            }

            gesture = new KeyboardShortcutGesture(key.Value, modifiers);
            return true;
        }

        internal string ToSettingString()
        {
            // 固定输出顺序：Ctrl + Alt + Shift + Key，避免同义字符串导致的冲突判断不一致。
            List<string> parts = new(capacity: 4);
            if (Modifiers.HasFlag(VirtualKeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(VirtualKeyModifiers.Menu))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(VirtualKeyModifiers.Shift))
            {
                parts.Add("Shift");
            }

            parts.Add(ToKeyToken(Key));
            return string.Join('+', parts);
        }

        internal bool IsValidForApp()
        {
            // 约束：必须包含 Ctrl 或 Alt，避免单键/仅 Shift 导致误触。
            if (!Modifiers.HasFlag(VirtualKeyModifiers.Control) && !Modifiers.HasFlag(VirtualKeyModifiers.Menu))
            {
                return false;
            }

            return !IsModifierKey(Key);
        }

        internal static bool IsModifierKey(VirtualKey key)
        {
            return key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
                or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
                or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
                or VirtualKey.LeftWindows or VirtualKey.RightWindows;
        }

        private static bool IsControlToken(string token)
        {
            return string.Equals(token, "ctrl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "control", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAltToken(string token)
        {
            return string.Equals(token, "alt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "menu", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsShiftToken(string token)
        {
            return string.Equals(token, "shift", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseKeyToken(string token, out VirtualKey key)
        {
            key = default;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (KeyAliases.TryGetValue(token, out VirtualKey aliasKey))
            {
                key = aliasKey;
                return true;
            }

            // 单字符：A-Z 或 0-9
            if (token.Length == 1)
            {
                char c = token[0];
                if (c is >= '0' and <= '9')
                {
                    return Enum.TryParse($"Number{c}", ignoreCase: true, out key);
                }

                if (c is >= 'a' and <= 'z')
                {
                    token = char.ToUpperInvariant(c).ToString();
                }

                if (c is >= 'A' and <= 'Z')
                {
                    return Enum.TryParse(token, ignoreCase: true, out key);
                }
            }

            // 其余：优先使用枚举名称解析（F1、Left、PageUp 等）。
            return Enum.TryParse(token, ignoreCase: true, out key);
        }

        private static string ToKeyToken(VirtualKey key)
        {
            return key switch
            {
                VirtualKey.Back => "Backspace",
                VirtualKey.Space => "Space",
                VirtualKey.Enter => "Enter",
                VirtualKey.Escape => "Escape",
                VirtualKey.Delete => "Delete",
                VirtualKey.PageUp => "PageUp",
                VirtualKey.PageDown => "PageDown",
                VirtualKey.Number0 => "0",
                VirtualKey.Number1 => "1",
                VirtualKey.Number2 => "2",
                VirtualKey.Number3 => "3",
                VirtualKey.Number4 => "4",
                VirtualKey.Number5 => "5",
                VirtualKey.Number6 => "6",
                VirtualKey.Number7 => "7",
                VirtualKey.Number8 => "8",
                VirtualKey.Number9 => "9",
                _ => key.ToString(),
            };
        }
    }
}

