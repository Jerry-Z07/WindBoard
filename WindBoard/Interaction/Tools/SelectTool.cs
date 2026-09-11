using System;
using System.Collections.Generic;
using System.Numerics;
using WindBoard.Board;
using WindBoard.Board.Commands;
using WindBoard.Board.Editing;
using WindBoard.Board.Elements;
using WindBoard.Board.Items;
using Vortice.Mathematics;

namespace WindBoard.Interaction.Tools
{
    /// <summary>
    /// 选择工具：选中集、框选（marquee）与变换快照状态内聚（design B 节）。
    /// </summary>
    /// <remarks>
    /// - <see cref="IBoardTool"/> 生命周期承载“框选”手势（Begin=起点、Move=拖动、End=提交、Cancel=取消）；
    /// - 拖动选中项与滚轮/双指变换经专用方法由控制器调度（不在统一生命周期内）；
    /// - 指针捕获与 pointerId 跟踪（_marqueePointerId/_selectionPointerId）仍由控制器负责；
    /// - 命中测试按条目类型单点分发（<see cref="InkItemPickTest"/>/<see cref="InkItemRectSelectTest"/>）；
    /// - 选中集变化经 <see cref="SelectionChanged"/> 通知控制器（转发 FrameInvalidated + StateChanged）；
    /// - 选中集泛化为 <see cref="IBoardInkItem"/>（design D）：折线笔迹与两点式形状均可选中；
    ///   矩阵变换（滚轮/双指缩放旋转）仅作用于笔迹，形状仅支持平移（轴对齐两点式几何
    ///   无法无损承载旋转/非均匀缩放，为阶段二明示边界）。
    /// </remarks>
    internal sealed class SelectTool : IBoardTool
    {
        /// <summary>框选矩形小于该阈值（DIP）时按“点击”处理（点选），避免轻微抖动导致无法点选。</summary>
        private const float MarqueeClickThresholdDip = 6.0f;

        /// <summary>点选/选中范围判定的命中容差（DIP）。</summary>
        private const float SelectHitToleranceDip = 8.0f;

        private readonly BoardInputContext _context;

        // 选中集：支持“单条目”与“多条目框选”两种形态；
        // 约定：框选命中多个条目时，把它们视为一个整体进行移动等操作。
        private readonly List<IBoardInkItem> _selectedInkItems = new();
        private BoardElement? _selectedElement;

        // 框选（marquee）几何状态（屏幕 DIP 坐标）。
        private bool _isMarqueeActive;
        private Vector2 _marqueeStartScreen = Vector2.Zero;
        private Vector2 _marqueeCurrentScreen = Vector2.Zero;

        // 选择变换：对“选中的条目集合”做快照，提交时写入撤销记录（按条目类型分支，design D）。
        private List<SelectionItemSnapshot>? _selectionSnapshots;
        private BoardElement? _selectionTransformElement;
        private Vector2? _selectionElementBeforePositionWorld;
        private Vector2? _selectionElementBeforeSizeWorld;
        private bool _selectionModified;

        public SelectTool(BoardInputContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>框选点击路径命中的元素（End 提交后由控制器读取以处理双击打开）。</summary>
        public BoardElement? LastMarqueeClickedElement { get; private set; }

        /// <summary>框选点击路径的起点屏幕坐标（配合 <see cref="LastMarqueeClickedElement"/> 使用）。</summary>
        public Vector2 LastMarqueeClickScreenDip { get; private set; }

        /// <summary>选中集/变换状态变化通知（等价于原 FrameInvalidated + StateChanged 成对通知）。</summary>
        public event Action? SelectionChanged;

        public BoardTool Id => BoardTool.Select;

        /// <summary>当前选中的条目集合（笔迹与形状，按文档顺序）。</summary>
        public IReadOnlyList<IBoardInkItem> SelectedItems => _selectedInkItems;

        /// <summary>
        /// 当前选中的条目（兼容单选场景：当且仅当选中一条时返回；多选返回 null）。
        /// </summary>
        public IBoardInkItem? SelectedItem => _selectedInkItems.Count == 1 ? _selectedInkItems[0] : null;

        /// <summary>当前选中的元素。</summary>
        public BoardElement? SelectedElement => _selectedElement;

        /// <summary>自上次提交/取消以来选中对象是否发生了变换。</summary>
        public bool SelectionModified => _selectionModified;

        /// <summary>
        /// 选中集中是否包含笔迹。
        /// </summary>
        /// <remarks>
        /// 矩阵变换（滚轮/双指缩放旋转）仅作用于笔迹（形状跳过——design D）；
        /// 纯形状选择时控制器据此把缩放/旋转手势透传给视口，避免手势被吞掉后视口失效。
        /// </remarks>
        public bool HasTransformableStrokes
        {
            get
            {
                for (int i = 0; i < _selectedInkItems.Count; i++)
                {
                    if (_selectedInkItems[i] is Stroke)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 是否存在“待提交的选择变换”（滚轮交互结束时的提交判定条件，与原实现等价）。
        /// </summary>
        public bool HasPendingSelectionChanges
        {
            get
            {
                if (!_selectionModified)
                {
                    return false;
                }

                return (_selectionSnapshots is { Count: > 0 })
                    || (_selectionTransformElement is not null
                        && _selectionElementBeforePositionWorld is not null
                        && _selectionElementBeforeSizeWorld is not null);
            }
        }

        #region 框选（IBoardTool 生命周期）

        public void Begin(in ToolInput input)
        {
            _isMarqueeActive = true;
            _marqueeStartScreen = input.PositionScreenDip;
            _marqueeCurrentScreen = input.PositionScreenDip;
        }

        public void Move(in ToolInput input)
        {
            _marqueeCurrentScreen = input.PositionScreenDip;
        }

        public void End(in ToolInput input)
        {
            if (!_isMarqueeActive)
            {
                return;
            }

            _isMarqueeActive = false;
            LastMarqueeClickedElement = null;
            LastMarqueeClickScreenDip = Vector2.Zero;

            Rect rectDip = CreateRectFromTwoPoints(_marqueeStartScreen, _marqueeCurrentScreen);
            bool isClick = rectDip.Width <= MarqueeClickThresholdDip && rectDip.Height <= MarqueeClickThresholdDip;

            // 小于阈值时按“点击”处理，避免用户轻微抖动导致无法点选。
            if (isClick)
            {
                HitTestSelectableAtScreenPoint(_marqueeStartScreen, out IBoardInkItem? selectedItem, out BoardElement? selectedElement);

                if (selectedItem is not null)
                {
                    SetSelectedItems(new[] { selectedItem });
                }
                else
                {
                    SetSelectedElement(selectedElement);
                    LastMarqueeClickedElement = selectedElement;
                    LastMarqueeClickScreenDip = _marqueeStartScreen;
                }
            }
            else
            {
                // 框选：把屏幕矩形转换为世界坐标 AABB。
                Vector2 worldTopLeft = _context.Viewport.ScreenToWorld(new Vector2(rectDip.Left, rectDip.Top));
                Vector2 worldBottomRight = _context.Viewport.ScreenToWorld(new Vector2(rectDip.Right, rectDip.Bottom));

                Vector2 minWorld = new(
                    Math.Min(worldTopLeft.X, worldBottomRight.X),
                    Math.Min(worldTopLeft.Y, worldBottomRight.Y));
                Vector2 maxWorld = new(
                    Math.Max(worldTopLeft.X, worldBottomRight.X),
                    Math.Max(worldTopLeft.Y, worldBottomRight.Y));

                List<IBoardInkItem> selectedItems = HitTestSelectableItemsInWorldRect(minWorld, maxWorld);
                SetSelectedItems(selectedItems.Count > 0 ? selectedItems : null);
            }
        }

        public void Cancel()
        {
            _isMarqueeActive = false;
            _marqueeStartScreen = Vector2.Zero;
            _marqueeCurrentScreen = Vector2.Zero;
        }

        /// <summary>渲染层读取框选矩形（框选进行中才有效）。</summary>
        public bool TryGetMarqueeRectDip(out Rect marqueeRectDip)
        {
            if (!_isMarqueeActive)
            {
                marqueeRectDip = default;
                return false;
            }

            marqueeRectDip = CreateRectFromTwoPoints(_marqueeStartScreen, _marqueeCurrentScreen);
            return true;
        }

        #endregion

        #region 选中集管理

        /// <summary>清除选中（条目与元素）。</summary>
        public void ClearSelection()
        {
            SetSelectedItems(null);
            SetSelectedElement(null);
        }

        public void SetSelection(IBoardInkItem? item)
        {
            SetSelectedItems(item is null ? null : new[] { item });
        }

        public void SetSelectionItems(IReadOnlyList<IBoardInkItem>? items)
        {
            SetSelectedItems(items);
        }

        public void SetSelection(BoardElement? element)
        {
            SetSelectedElement(element);
        }

        /// <summary>
        /// 校验当前选择是否仍存在于文档中（例如撤销/重做导致条目移除时清理选择）。
        /// </summary>
        public void ValidateSelection()
        {
            if (_selectedInkItems.Count > 0)
            {
                // 选择条目集合：按文档当前顺序重新归一化，避免撤销/重做或重排后出现“顺序错乱/包含失效对象”。
                var set = new HashSet<IBoardInkItem>(_selectedInkItems);
                var normalized = new List<IBoardInkItem>(_selectedInkItems.Count);
                IReadOnlyList<IBoardInkItem> inkItems = _context.Document.InkItems;
                for (int i = 0; i < inkItems.Count; i++)
                {
                    if (set.Contains(inkItems[i]))
                    {
                        normalized.Add(inkItems[i]);
                    }
                }

                if (InkItemListComparer.IsSameList(_selectedInkItems, normalized))
                {
                    return;
                }

                _selectedInkItems.Clear();
                _selectedInkItems.AddRange(normalized);
                RaiseSelectionChanged();
                return;
            }

            if (_selectedElement is BoardElement element)
            {
                if (_context.Document.ElementsAboveInk.Contains(element) || _context.Document.ElementsBelowInk.Contains(element))
                {
                    return;
                }

                _selectedElement = null;
                RaiseSelectionChanged();
            }
        }

        private void SetSelectedItems(IReadOnlyList<IBoardInkItem>? items)
        {
            int count = items?.Count ?? 0;
            if (count <= 0)
            {
                if (_selectedInkItems.Count == 0 && _selectedElement is null)
                {
                    return;
                }

                _selectedInkItems.Clear();
                _selectedElement = null;
                RaiseSelectionChanged();
                return;
            }

            // 选择集合按文档顺序归一化：
            // - 框选命中应保持相对层级顺序；
            // - 过滤掉不在文档中的对象，避免撤销/重做后出现“幽灵选择”。
            var set = new HashSet<IBoardInkItem>();
            for (int i = 0; i < count; i++)
            {
                IBoardInkItem item = items![i];
                if (item is not null)
                {
                    set.Add(item);
                }
            }

            var ordered = new List<IBoardInkItem>(set.Count);
            IReadOnlyList<IBoardInkItem> inkItems = _context.Document.InkItems;
            for (int i = 0; i < inkItems.Count; i++)
            {
                if (set.Contains(inkItems[i]))
                {
                    ordered.Add(inkItems[i]);
                }
            }

            bool unchanged = _selectedElement is null && InkItemListComparer.IsSameList(_selectedInkItems, ordered);
            if (unchanged)
            {
                return;
            }

            _selectedInkItems.Clear();
            _selectedInkItems.AddRange(ordered);
            _selectedElement = null;
            RaiseSelectionChanged();
        }

        private void SetSelectedElement(BoardElement? element)
        {
            if (ReferenceEquals(_selectedElement, element) && _selectedInkItems.Count == 0)
            {
                return;
            }

            _selectedElement = element;
            _selectedInkItems.Clear();
            RaiseSelectionChanged();
        }

        private void RaiseSelectionChanged()
        {
            SelectionChanged?.Invoke();
        }

        #endregion

        #region 选中范围判定与命中测试

        /// <summary>判定屏幕点（含容差）是否落在选中对象包围盒内（用于“拖动选中项”手势判定）。</summary>
        public bool IsScreenPointInsideSelectedBounds(Vector2 screenDip)
        {
            if (_selectedInkItems.Count > 0)
            {
                return IsScreenPointInsideSelectedItemsBounds(_selectedInkItems, screenDip);
            }

            if (_selectedElement is BoardElement element)
            {
                return IsScreenPointInsideSelectedElementBounds(element, screenDip);
            }

            return false;
        }

        private bool IsScreenPointInsideSelectedItemsBounds(IReadOnlyList<IBoardInkItem> items, Vector2 screenDip)
        {
            if (items is null || items.Count == 0)
            {
                return false;
            }

            Matrix3x2 worldToScreen = _context.Viewport.GetWorldToScreenTransform();
            if (!InkItemScreenBounds.TryGetInkItemsBoundsScreenDip(items, worldToScreen, out Rect bounds))
            {
                return false;
            }

            float left = bounds.Left - SelectHitToleranceDip;
            float top = bounds.Top - SelectHitToleranceDip;
            float right = bounds.Right + SelectHitToleranceDip;
            float bottom = bounds.Bottom + SelectHitToleranceDip;

            return screenDip.X >= left
                && screenDip.X <= right
                && screenDip.Y >= top
                && screenDip.Y <= bottom;
        }

        private bool IsScreenPointInsideSelectedElementBounds(BoardElement element, Vector2 screenDip)
        {
            Rect boundsWorld = element.GetBoundsWorld();
            if (boundsWorld.Width <= 0.0001f || boundsWorld.Height <= 0.0001f)
            {
                return false;
            }

            Matrix3x2 worldToScreen = _context.Viewport.GetWorldToScreenTransform();
            Vector2 minScreen = Vector2.Transform(new Vector2(boundsWorld.Left, boundsWorld.Top), worldToScreen);
            Vector2 maxScreen = Vector2.Transform(new Vector2(boundsWorld.Right, boundsWorld.Bottom), worldToScreen);

            float left = Math.Min(minScreen.X, maxScreen.X) - SelectHitToleranceDip;
            float top = Math.Min(minScreen.Y, maxScreen.Y) - SelectHitToleranceDip;
            float right = Math.Max(minScreen.X, maxScreen.X) + SelectHitToleranceDip;
            float bottom = Math.Max(minScreen.Y, maxScreen.Y) + SelectHitToleranceDip;

            return screenDip.X >= left && screenDip.X <= right && screenDip.Y >= top && screenDip.Y <= bottom;
        }

        /// <summary>
        /// 点选命中（视觉优先级：上层元素 → 笔迹条目 → 下层元素）。
        /// </summary>
        private void HitTestSelectableAtScreenPoint(Vector2 screenDip, out IBoardInkItem? item, out BoardElement? element)
        {
            item = null;
            element = null;

            Vector2 pointWorld = _context.Viewport.ScreenToWorld(screenDip);
            float toleranceWorld = SelectHitToleranceDip / Math.Max(0.0001f, _context.Viewport.Zoom);

            // 选择优先级（视觉顺序）：上层元素 → 笔迹（条目） → 下层元素
            element = ElementPickTest.HitTestTopMostElement(_context.Document.ElementsAboveInk, pointWorld, toleranceWorld);
            if (element is not null)
            {
                return;
            }

            // 点选命中按条目类型单点分发；命中任何条目都会遮挡下层元素。
            // 命中即选中（不按类型截断）：折线笔迹与两点式形状均可点选（design D）。
            item = InkItemPickTest.HitTestTopMostInkItem(_context.Document.InkItems, pointWorld, toleranceWorld);
            if (item is not null)
            {
                return;
            }

            element = ElementPickTest.HitTestTopMostElement(_context.Document.ElementsBelowInk, pointWorld, toleranceWorld);
        }

        private List<IBoardInkItem> HitTestSelectableItemsInWorldRect(Vector2 minWorld, Vector2 maxWorld)
        {
            // 交互约定：元素只能通过“单击”选中，不支持框选。
            // 框选命中按条目类型单点分发；笔迹与形状均可框选
            // （SetSelectedItems 会按文档顺序重新归一化）。
            return InkItemRectSelectTest.HitTestInkItemsInWorldRect(_context.Document.InkItems, minWorld, maxWorld);
        }

        #endregion

        #region 选择变换（拖动选中项 / 滚轮 / 双指手势）

        /// <summary>
        /// 开始“拖动选中项”手势：对当前选中集或选中元素建立变换快照。
        /// </summary>
        public void BeginSelectionMove()
        {
            if (_selectedInkItems.Count > 0)
            {
                BeginSelectionTransformSnapshotForItems(_selectedInkItems);
            }
            else if (_selectedElement is BoardElement element)
            {
                BeginElementTransformSnapshot(element);
            }
        }

        /// <summary>
        /// 对选中条目集合建立变换快照（滚轮/双指路径；若快照已对应同一集合则复用）。
        /// </summary>
        public void BeginSelectionTransformSnapshotForSelectedItems()
        {
            BeginSelectionTransformSnapshotForItems(_selectedInkItems);
        }

        /// <summary>
        /// 确保对指定元素建立了变换快照（滚轮缩放路径：快照不存在或目标变化时重建）。
        /// </summary>
        public void EnsureElementTransformSnapshot(BoardElement element)
        {
            if (_selectionElementBeforePositionWorld is null || !ReferenceEquals(_selectionTransformElement, element))
            {
                BeginElementTransformSnapshot(element);
            }
        }

        /// <summary>按屏幕增量平移选中对象（拖动选中项路径；平移对形状同样生效）。</summary>
        public void MoveSelectionByScreenDelta(Vector2 deltaScreenDip)
        {
            if (_selectionSnapshots is not null && _selectedInkItems.Count > 0)
            {
                Vector2 deltaWorld = deltaScreenDip / Math.Max(0.0001f, _context.Viewport.Zoom);
                for (int i = 0; i < _selectedInkItems.Count; i++)
                {
                    _selectedInkItems[i].Translate(deltaWorld);
                }
            }
            else if (_selectionTransformElement is not null)
            {
                Vector2 deltaWorld = deltaScreenDip / Math.Max(0.0001f, _context.Viewport.Zoom);
                _selectionTransformElement.PositionWorld += deltaWorld;
            }

            if (deltaScreenDip.LengthSquared() > 0.0001f)
            {
                _selectionModified = true;
            }
        }

        /// <summary>按世界坐标增量平移整个选中集（双指纯平移路径；形状与笔迹都生效）。</summary>
        public void TranslateSelectedItems(Vector2 deltaWorld)
        {
            if (deltaWorld.LengthSquared() <= 0.0000001f)
            {
                return;
            }

            for (int i = 0; i < _selectedInkItems.Count; i++)
            {
                _selectedInkItems[i].Translate(deltaWorld);
            }

            _selectionModified = true;
        }

        /// <summary>
        /// 对选中集应用 2D 矩阵变换（滚轮缩放/旋转、双指捏合路径）。
        /// </summary>
        /// <remarks>
        /// 形状跳过：轴对齐两点式几何无法无损承载旋转/非均匀缩放（design D 决策），
        /// 混合选择时矩阵变换仅作用于笔迹（已知边界）。
        /// </remarks>
        public void ApplyMatrixTransformToSelectedStrokes(Matrix3x2 transform)
        {
            for (int i = 0; i < _selectedInkItems.Count; i++)
            {
                if (_selectedInkItems[i] is Stroke stroke)
                {
                    stroke.Transform(transform);
                }
            }

            _selectionModified = true;
        }

        /// <summary>标记选中对象已发生变换（元素滚轮缩放/双指变换等直接操作元素后调用）。</summary>
        public void MarkSelectionModified()
        {
            _selectionModified = true;
        }

        /// <summary>
        /// 计算选中条目集合的整体中心（世界坐标，用作滚轮旋转/缩放锚点）。
        /// </summary>
        public Vector2 GetSelectedItemsCenterWorld()
        {
            // 单条目：沿用既有中心逻辑（笔迹 Bounds 优先，否则点集平均；形状为包围盒中心）。
            if (_selectedInkItems.Count == 1)
            {
                return GetInkItemCenterWorld(_selectedInkItems[0]);
            }

            // 多条目：以“包围盒中心”为整体中心，更符合用户对“作为整体旋转”的直觉。
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            bool hasAny = false;

            for (int i = 0; i < _selectedInkItems.Count; i++)
            {
                IBoardInkItem item = _selectedInkItems[i];
                if (item is Stroke stroke)
                {
                    if (!stroke.HasBounds)
                    {
                        stroke.RecalculateBoundsFromPoints();
                    }

                    if (!stroke.HasBounds)
                    {
                        continue;
                    }

                    min = new Vector2(
                        Math.Min(min.X, stroke.BoundsMin.X),
                        Math.Min(min.Y, stroke.BoundsMin.Y));
                    max = new Vector2(
                        Math.Max(max.X, stroke.BoundsMax.X),
                        Math.Max(max.Y, stroke.BoundsMax.Y));
                    hasAny = true;
                    continue;
                }

                Rect bounds = item.BoundsWorld;
                if (bounds.Width <= 0.0001f || bounds.Height <= 0.0001f)
                {
                    continue;
                }

                min = new Vector2(Math.Min(min.X, bounds.Left), Math.Min(min.Y, bounds.Top));
                max = new Vector2(Math.Max(max.X, bounds.Right), Math.Max(max.Y, bounds.Bottom));
                hasAny = true;
            }

            return hasAny ? (min + max) / 2.0f : Vector2.Zero;
        }

        /// <summary>
        /// 提交选择变换：把快照差异写入撤销记录（一次连续交互对应一次 Ctrl+Z）。
        /// </summary>
        /// <remarks>
        /// pointerId/捕获释放由控制器负责；本方法只处理快照与命令。
        /// 混合选择（笔迹+形状）时按条目类型生成各自命令，经 CompositeCommand 合并为一次撤销（design D）。
        /// </remarks>
        public void CommitSelection()
        {
            List<SelectionItemSnapshot>? snapshots = _selectionSnapshots;
            BoardElement? element = _selectionTransformElement;
            Vector2? elementBeforePos = _selectionElementBeforePositionWorld;
            Vector2? elementBeforeSize = _selectionElementBeforeSizeWorld;

            _selectionSnapshots = null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;

            // 统一把“多条目变换/元素变换”合并成一次撤销记录，保证用户一次操作对应一次 Ctrl+Z。
            List<IBoardCommand>? commands = null;

            if (_selectionModified && snapshots is { Count: > 0 })
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    switch (snapshots[i])
                    {
                        case StrokeSelectionSnapshot strokeSnap:
                        {
                            var after = new List<StrokePoint>(strokeSnap.Stroke.Points);
                            if (IsSameStrokePointList(strokeSnap.BeforePoints, after))
                            {
                                continue;
                            }

                            commands ??= new List<IBoardCommand>();
                            commands.Add(new UpdateStrokePointsCommand(strokeSnap.Stroke, strokeSnap.BeforePoints, after));
                            break;
                        }

                        case ShapeSelectionSnapshot shapeSnap:
                        {
                            (Vector2 Start, Vector2 End) afterGeometry = (shapeSnap.Shape.Start, shapeSnap.Shape.End);
                            if (IsSameShapeGeometry(shapeSnap.BeforeGeometry, afterGeometry))
                            {
                                continue;
                            }

                            commands ??= new List<IBoardCommand>();
                            commands.Add(new UpdateShapeGeometryCommand(shapeSnap.Shape, shapeSnap.BeforeGeometry, afterGeometry));
                            break;
                        }
                    }
                }
            }

            if (element is not null
                && elementBeforePos is Vector2 beforePos
                && elementBeforeSize is Vector2 beforeSize
                && _selectionModified)
            {
                Vector2 afterPos = element.PositionWorld;
                Vector2 afterSize = element.SizeWorld;

                bool moved = Vector2.DistanceSquared(beforePos, afterPos) > 0.000001f;
                bool resized = Vector2.DistanceSquared(beforeSize, afterSize) > 0.000001f;
                if (moved || resized)
                {
                    commands ??= new List<IBoardCommand>();
                    commands.Add(new UpdateElementTransformCommand(
                        element,
                        beforePos,
                        afterPos,
                        beforeSizeWorld: beforeSize,
                        afterSizeWorld: afterSize));
                }
            }

            if (commands is { Count: > 0 })
            {
                _context.Session.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
            }

            _selectionModified = false;
        }

        /// <summary>
        /// 取消选择变换：恢复快照（不写入撤销栈）。
        /// </summary>
        /// <remarks>
        /// pointerId/触摸手势目标/捕获释放由控制器负责。
        /// </remarks>
        public void CancelSelection()
        {
            if (_selectionSnapshots is { Count: > 0 } snapshots)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    switch (snapshots[i])
                    {
                        case StrokeSelectionSnapshot strokeSnap:
                            RestoreStrokePoints(strokeSnap.Stroke, strokeSnap.BeforePoints);
                            break;

                        case ShapeSelectionSnapshot shapeSnap:
                            shapeSnap.Shape.SetGeometry(shapeSnap.BeforeGeometry.Start, shapeSnap.BeforeGeometry.End);
                            break;
                    }
                }
            }

            if (_selectionTransformElement is not null && _selectionElementBeforePositionWorld is Vector2 beforePos)
            {
                _selectionTransformElement.PositionWorld = beforePos;
            }

            if (_selectionTransformElement is not null && _selectionElementBeforeSizeWorld is Vector2 beforeSize)
            {
                _selectionTransformElement.SizeWorld = beforeSize;
            }

            _selectionSnapshots = null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;
            _selectionModified = false;
        }

        private void BeginSelectionTransformSnapshotForItems(IReadOnlyList<IBoardInkItem> items)
        {
            // 选择变换快照规则：
            // - 一次连续交互（拖拽/触摸/滚轮）只创建一次“Before”快照；
            // - 快照按当前选择集合顺序记录，提交时按相同顺序生成撤销命令；
            // - 仅支持“条目集合”或“单元素”二选一，避免产生混合撤销记录；
            // - 笔迹走点列快照、形状走两点几何快照，混合选择经命令层合并（design D）。
            if (IsSameSelectionSnapshot(items))
            {
                return;
            }

            var snapshots = new List<SelectionItemSnapshot>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                switch (items[i])
                {
                    case Stroke stroke:
                        snapshots.Add(new StrokeSelectionSnapshot(stroke));
                        break;

                    case BoardShape shape:
                        snapshots.Add(new ShapeSelectionSnapshot(shape));
                        break;
                }
            }

            _selectionSnapshots = snapshots.Count > 0 ? snapshots : null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;
            _selectionModified = false;
        }

        private void BeginElementTransformSnapshot(BoardElement element)
        {
            _selectionSnapshots = null;

            _selectionTransformElement = element;
            _selectionElementBeforePositionWorld = element.PositionWorld;
            _selectionElementBeforeSizeWorld = element.SizeWorld;
            _selectionModified = false;
        }

        private bool IsSameSelectionSnapshot(IReadOnlyList<IBoardInkItem> items)
        {
            if (_selectionSnapshots is not { Count: > 0 } snapshots)
            {
                return false;
            }

            if (items.Count != snapshots.Count)
            {
                return false;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (!ReferenceEquals(items[i], snapshots[i].Item))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        private static Rect CreateRectFromTwoPoints(Vector2 a, Vector2 b)
        {
            float left = Math.Min(a.X, b.X);
            float top = Math.Min(a.Y, b.Y);
            float right = Math.Max(a.X, b.X);
            float bottom = Math.Max(a.Y, b.Y);
            // Rect 的构造函数是 (x, y, width, height)，这里应使用 FromLTRB 构造，避免把 right/bottom 误当作 width/height。
            return Rect.FromLTRB(left, top, right, bottom);
        }

        private static Vector2 GetInkItemCenterWorld(IBoardInkItem item)
        {
            if (item is Stroke stroke)
            {
                if (stroke.HasBounds)
                {
                    return (stroke.BoundsMin + stroke.BoundsMax) / 2.0f;
                }

                // 某些情况下笔迹可能还未计算 Bounds（例如外部构造/导入），此时退化为“点集平均”。
                if (stroke.Points.Count == 0)
                {
                    return Vector2.Zero;
                }

                Vector2 sum = Vector2.Zero;
                for (int i = 0; i < stroke.Points.Count; i++)
                {
                    sum += stroke.Points[i].Position;
                }

                return sum / stroke.Points.Count;
            }

            Rect bounds = item.BoundsWorld;
            return new Vector2((bounds.Left + bounds.Right) / 2.0f, (bounds.Top + bounds.Bottom) / 2.0f);
        }

        private static void RestoreStrokePoints(Stroke stroke, List<StrokePoint> snapshot)
        {
            stroke.Points.Clear();
            stroke.Points.AddRange(snapshot);
            stroke.RecalculateBoundsFromPoints();
        }

        private static bool IsSameStrokePointList(List<StrokePoint> a, List<StrokePoint> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSameShapeGeometry((Vector2 Start, Vector2 End) a, (Vector2 Start, Vector2 End) b)
        {
            return Vector2.DistanceSquared(a.Start, b.Start) <= 0.000001f
                && Vector2.DistanceSquared(a.End, b.End) <= 0.000001f;
        }

        /// <summary>选择变换快照基类：记录单个条目的“Before”状态（按条目类型分支，design D）。</summary>
        private abstract class SelectionItemSnapshot
        {
            protected SelectionItemSnapshot(IBoardInkItem item)
            {
                Item = item ?? throw new ArgumentNullException(nameof(item));
            }

            public IBoardInkItem Item { get; }
        }

        /// <summary>笔迹点列快照：提交经 <see cref="UpdateStrokePointsCommand"/>（点集 diff）。</summary>
        private sealed class StrokeSelectionSnapshot : SelectionItemSnapshot
        {
            public StrokeSelectionSnapshot(Stroke stroke)
                : base(stroke)
            {
                Stroke = stroke;
                BeforePoints = new List<StrokePoint>(stroke.Points);
            }

            public Stroke Stroke { get; }

            public List<StrokePoint> BeforePoints { get; }
        }

        /// <summary>形状两点几何快照：提交经 <see cref="UpdateShapeGeometryCommand"/>（Start/End 前后快照）。</summary>
        private sealed class ShapeSelectionSnapshot : SelectionItemSnapshot
        {
            public ShapeSelectionSnapshot(BoardShape shape)
                : base(shape)
            {
                Shape = shape;
                BeforeGeometry = (shape.Start, shape.End);
            }

            public BoardShape Shape { get; }

            public (Vector2 Start, Vector2 End) BeforeGeometry { get; }
        }
    }
}
