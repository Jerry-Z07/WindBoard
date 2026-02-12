namespace WindBoard.Settings
{
    /// <summary>
    /// 伪装设置的只读快照（供 UI 使用，避免直接暴露可变引用）。
    /// </summary>
    internal sealed class CamouflageSettingsSnapshot
    {
        public required bool Enabled { get; init; }

        public required string Title { get; init; }

        public required string SourcePath { get; init; }

        public required string IconCachePath { get; init; }

        public required string ShortcutLastGeneratedSignature { get; init; }

        public required string ShortcutLastGeneratedPath { get; init; }
    }
}
