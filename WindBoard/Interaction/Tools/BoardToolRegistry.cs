using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 工具注册表：id → 工具实例的简单字典（design B 节）。
    /// </summary>
    /// <remarks>
    /// 内置工具在画布初始化（<see cref="WindBoard.Interaction.BoardInputController"/> 构造）时注册；
    /// 重复注册同一 id 时后者覆盖前者（简单字典语义，便于测试替换工具实例）。
    /// </remarks>
    internal sealed class BoardToolRegistry
    {
        private readonly Dictionary<BoardTool, IBoardTool> _tools = new();

        /// <summary>注册（或替换）工具实例。</summary>
        public void Register(IBoardTool tool)
        {
            _tools[tool.Id] = tool;
        }

        /// <summary>按枚举 id 解析工具实例。</summary>
        public bool TryGetTool(BoardTool id, [NotNullWhen(true)] out IBoardTool? tool)
        {
            if (_tools.TryGetValue(id, out IBoardTool? found))
            {
                tool = found;
                return true;
            }

            tool = null;
            return false;
        }
    }
}
