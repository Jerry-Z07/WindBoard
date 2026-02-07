namespace WindBoard.Board.Elements
{
    /// <summary>
    /// 文本元素：用于导入文字（或文本文件）。
    /// </summary>
    internal sealed class BoardTextElement : BoardElement
    {
        public string Text { get; set; } = string.Empty;
    }
}

