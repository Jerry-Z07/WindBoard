namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 画板工具策略接口（阶段一"工具策略化"，design B 节）。
    /// </summary>
    /// <remarks>
    /// 生命周期：<see cref="Begin"/> → <see cref="Move"/>* → <see cref="End"/>（正常提交）
    /// 或 <see cref="Cancel"/>（取消，不写入撤销栈）。
    /// 指针事件路由、活动 pointerId 跟踪仍由 <see cref="WindBoard.Interaction.BoardInputController"/>
    /// 负责，工具只接收"已被路由的"输入（同一会话内 Begin/Move/End/Cancel 严格配对）。
    /// 阶段二接入形状工具 = 注册表注册新实例，无需修改控制器结构。
    /// </remarks>
    internal interface IBoardTool
    {
        /// <summary>
        /// 工具标识（与 <see cref="WindBoard.Interaction.BoardTool"/> 枚举对应，用于注册表解析）。
        /// </summary>
        BoardTool Id { get; }

        /// <summary>
        /// 指针按下：开始一次工具会话。
        /// </summary>
        void Begin(in ToolInput input);

        /// <summary>
        /// 指针移动：推进当前工具会话。
        /// </summary>
        void Move(in ToolInput input);

        /// <summary>
        /// 指针释放/捕获丢失：提交当前工具会话（写入撤销栈等）。
        /// </summary>
        void End(in ToolInput input);

        /// <summary>
        /// 取消当前工具会话（系统取消/外部打断），恢复到会话前状态。
        /// </summary>
        /// <remarks>
        /// 无输入参数：取消路径可能没有对应的指针事件（例如触摸切换双指手势）。
        /// 预览挂载点与脏矩形的清理由控制器在调度 Cancel 后统一完成。
        /// </remarks>
        void Cancel();
    }
}
