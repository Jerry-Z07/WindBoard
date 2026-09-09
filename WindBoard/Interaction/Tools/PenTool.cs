using System;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Commands;
using Vortice.Mathematics;

namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 画笔工具：活动笔迹状态内聚（原 <c>BoardInputController</c> 的
    /// ActiveStroke/CreateNewStroke/AppendPoint/Commit/Discard 逻辑迁移至此）。
    /// </summary>
    /// <remarks>
    /// - 预览（活动笔迹）经 <see cref="BoardInputContext.PreviewItem"/> 暴露，渲染路径不变；
    /// - 压感已由控制器归一化，工具直接使用；
    /// - 提交经 <see cref="BoardSession.Execute"/>（<see cref="AddStrokeCommand"/>），撤销/重做语义不变；
    /// - 高频路径（指针事件内）禁止日志。
    /// </remarks>
    internal sealed class PenTool : IBoardTool
    {
        /// <summary>增量脏矩形的额外扩展（DIP），与原控制器常量保持一致。</summary>
        private const float DirtyRectExtraDip = 2.0f;

        /// <summary>相邻点最小距离（缩放为 1 时的世界坐标），低于该距离不追加点。</summary>
        private const float MinAppendDistanceWorldBase = 0.5f;

        public BoardTool Id => BoardTool.Pen;

        /// <summary>当前活动笔迹（未提交）；无会话时为 null。</summary>
        public Stroke? ActiveStroke { get; private set; }

        public void Begin(in ToolInput input)
        {
            BoardInputContext context = input.Context;
            // 防御：清理上一会话可能残留的增量脏矩形（正常路径 End/Cancel 已清理）。
            context.ClearStrokeDirtyRect();

            // 参数快照：仅在 Begin 读取一次并值拷贝进笔迹（"仅影响后续新建笔迹"），
            // 会话中途的参数变更不影响进行中的笔迹。
            ToolOptions options = context.ToolOptions;
            var stroke = new Stroke
            {
                Color = options.PenColor,
                BaseSize = options.PenBaseSize,
                EnablePressure = options.PenEnablePressure,
            };

            ActiveStroke = stroke;
            context.PreviewItem = stroke;

            if (TryAppendPoint(stroke, input))
            {
                context.InvalidateFrame();
            }
        }

        public void Move(in ToolInput input)
        {
            if (ActiveStroke is null)
            {
                return;
            }

            if (TryAppendPoint(ActiveStroke, input))
            {
                input.Context.InvalidateFrame();
            }
        }

        public void End(in ToolInput input)
        {
            Stroke? stroke = ActiveStroke;
            if (stroke is not null && stroke.Points.Count > 0)
            {
                input.Context.Session.Execute(new AddStrokeCommand(stroke));
            }

            ActiveStroke = null;
            input.Context.PreviewItem = null;
            input.Context.ClearStrokeDirtyRect();
        }

        public void Cancel()
        {
            // 预览挂载点与脏矩形由控制器在调度 Cancel 后统一清理（Cancel 无输入参数）。
            ActiveStroke = null;
        }

        /// <summary>
        /// 追加一个点：应用最小距离过滤（避免高密度采样产生过密点集），
        /// 更新包围盒与增量脏矩形。返回是否实际追加了点。
        /// </summary>
        private static bool TryAppendPoint(Stroke stroke, in ToolInput input)
        {
            BoardInputContext context = input.Context;
            Vector2 pos = context.Viewport.ScreenToWorld(input.PositionScreenDip);

            if (stroke.Points.Count > 0)
            {
                Vector2 last = stroke.Points[^1].Position;
                float minDistWorld = MinAppendDistanceWorldBase / Math.Max(0.0001f, context.Viewport.Zoom);
                if (Vector2.DistanceSquared(last, pos) < minDistWorld * minDistWorld)
                {
                    return false;
                }
            }

            stroke.Points.Add(new StrokePoint(pos, input.Pressure));
            stroke.ExpandBounds(pos, input.Pressure);

            Rect? updatedDirtyRect = BoardInputDirtyRectCalculator.UpdatePendingStrokeDirtyRect(
                context.PeekStrokeDirtyRect(),
                stroke,
                context.Viewport,
                input.PositionScreenDip,
                DirtyRectExtraDip);
            context.RequestStrokeDirtyRect(updatedDirtyRect);
            return true;
        }
    }
}
