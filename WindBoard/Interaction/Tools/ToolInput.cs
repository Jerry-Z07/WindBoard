using System.Numerics;
using Microsoft.UI.Input;

namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 一次指针输入的快照（屏幕 DIP 坐标 + 归一化压感 + 设备类型）与共享上下文。
    /// </summary>
    /// <remarks>
    /// - 位置为屏幕 DIP 坐标（与指针事件的 Position 一致），工具经
    ///   <see cref="BoardInputContext.Viewport"/> 换算世界坐标；
    /// - 压感已由控制器在路由层归一化（触控笔 [0.1, 1]，其它设备 1.0），
    ///   工具直接使用，无需感知设备差异；
    /// - <see cref="Context"/> 是工具访问视口/文档/会话/参数/通知的唯一通道。
    /// </remarks>
    internal readonly record struct ToolInput(
        Vector2 PositionScreenDip,
        float Pressure,
        PointerDeviceType DeviceType,
        BoardInputContext Context);
}
