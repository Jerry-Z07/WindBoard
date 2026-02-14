using System;
using System.Numerics;
using WindBoard.Board.Elements;

namespace WindBoard.Importing
{
    /// <summary>
    /// 导入元素放置规划：复刻旧版“网格铺开”的体验，避免多文件导入完全重叠。
    /// </summary>
    internal static class ImportPlacementPlanner
    {
        // 旧版导入网格参数（以 DIP 计）：4 列，单元格宽高 + 间距。
        private const float CellWidthDip = 420.0f;
        private const float CellHeightDip = 280.0f;
        private const float GapDip = 24.0f;
        private const int ColumnCount = 4;

        internal static void PlaceElementAtViewportCenterGrid(BoardElement element, Vector2 sizeDip, int index, Vector2 cameraWorld, float zoom)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            float z = Math.Max(0.0001f, zoom);

            // 网格偏移以 DIP 计算，再换算到世界坐标，保证不同缩放下导入布局的屏幕观感一致。
            int col = index % ColumnCount;
            int row = index / ColumnCount;

            Vector2 offsetDip = new(col * (CellWidthDip + GapDip), row * (CellHeightDip + GapDip));
            Vector2 offsetWorld = offsetDip / z;

            Vector2 sizeWorld = sizeDip / z;

            // 放置策略：
            // - 以视口中心为基准进行居中放置；
            // - 多个导入元素按网格向右/向下铺开。
            element.SizeWorld = sizeWorld;
            element.PositionWorld = cameraWorld - sizeWorld / 2.0f + offsetWorld;
        }
    }
}

