namespace WindBoard.Board.Elements
{
    /// <summary>
    /// 链接元素：用于导入 URL。
    /// </summary>
    internal sealed class BoardLinkElement : BoardElement
    {
        public string Url { get; set; } = string.Empty;

        public string? Title { get; set; }
    }
}

