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
    /// - 选中集变化经 <see cref="SelectionChanged"/> 通知控制器（转发 FrameInvalidated + StateChanged）。
    /// </remarks>
    internal sealed class SelectTool : IBoardTool
    {
        /// <summary>框选矩形小于该阈值（DIP）时按“点击”处理（点选），避免轻微抖动导致无法点选。</summary>
        private const float MarqueeClickThresholdDip = 6.0f;

        /// <summary>点选/选中范围判定的命中容差（DIP）。</summary>
        private const float SelectHitToleranceDip = 8.0f;

        private readonly BoardInputContext _context;

        // 选中集：支持“单笔迹”与“多笔迹框选”两种形态；
        // 约定：框选命中多个笔迹时，把它们视为一个整体进行移动/缩放/旋转等操作。
        private readonly List<Stroke> _selectedStrokes = new();
        private BoardElement? _selectedElement;

        // 框选（marquee）几何状态（屏幕 DIP 坐标）。
        private bool _isMarqueeActive;
        private Vector2 _marqueeStartScreen = Vector2.Zero;
        private Vector2 _marqueeCurrentScreen = Vector2.Zero;

        // 选择变换：对“选中的笔迹集合”做快照，提交时写入撤销记录。
        private List<StrokeTransformSnapshot>? _selectionStrokeBeforeSnapshots;
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

        /// <summary>当前选中的笔迹集合。</summary>
        public IReadOnlyList<Stroke> SelectedStrokes => _selectedStrokes;

        /// <summary>
        /// 当前选中的笔迹（兼容单选场景：当且仅当选中一条时返回；多选返回 null）。
        /// </summary>
        public Stroke? SelectedStroke => _selectedStrokes.Count == 1 ? _selectedStrokes[0] : null;

        /// <summary>当前选中的元素。</summary>
        public BoardElement? SelectedElement => _selectedElement;

        /// <summary>自上次提交/取消以来选中对象是否发生了变换。</summary>
        public bool SelectionModified => _selectionModified;

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

                return (_selectionStrokeBeforeSnapshots is { Count: > 0 })
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
                HitTestSelectableAtScreenPoint(_marqueeStartScreen, out Stroke? selectedStroke, out BoardElement? selectedElement);

                if (selectedStroke is not null)
                {
                    SetSelectedStrokes(new[] { selectedStroke });
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

                List<Stroke> selectedStrokes = HitTestSelectableStrokesInWorldRect(minWorld, maxWorld);
                SetSelectedStrokes(selectedStrokes.Count > 0 ? selectedStrokes : null);
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

        /// <summary>清除选中（笔迹与元素）。</summary>
        public void ClearSelection()
        {
            SetSelectedStrokes(null);
            SetSelectedElement(null);
        }

        public void SetSelection(Stroke? stroke)
        {
            SetSelectedStrokes(stroke is null ? null : new[] { stroke });
        }

        public void SetSelectionStrokes(IReadOnlyList<Stroke>? strokes)
        {
            SetSelectedStrokes(strokes);
        }

        public void SetSelection(BoardElement? element)
        {
            SetSelectedElement(element);
        }

        /// <summary>
        /// 校验当前选择是否仍存在于文档中（例如撤销/重做导致笔迹移除时清理选择）。
        /// </summary>
        public void ValidateSelection()
        {
            if (_selectedStrokes.Count > 0)
            {
                // 选择笔迹集合：按文档当前顺序重新归一化，避免撤销/重做或重排后出现“顺序错乱/包含失效对象”。
                var set = new HashSet<Stroke>(_selectedStrokes);
                var normalized = new List<Stroke>(_selectedStrokes.Count);
                IReadOnlyList<IBoardInkItem> inkItems = _context.Document.InkItems;
                for (int i = 0; i < inkItems.Count; i++)
                {
                    if (inkItems[i] is Stroke s && set.Contains(s))
                    {
                        normalized.Add(s);
                    }
                }

                if (InkItemListComparer.IsSameList(_selectedStrokes, normalized))
                {
                    return;
                }

                _selectedStrokes.Clear();
                _selectedStrokes.AddRange(normalized);
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

        private void SetSelectedStrokes(IReadOnlyList<Stroke>? strokes)
        {
            int count = strokes?.Count ?? 0;
            if (count <= 0)
            {
                if (_selectedStrokes.Count == 0 && _selectedElement is null)
                {
                    return;
                }

                _selectedStrokes.Clear();
                _selectedElement = null;
                RaiseSelectionChanged();
                return;
            }

            // 选择集合按文档顺序归一化：
            // - 框选命中应保持相对层级顺序；
            // - 过滤掉不在文档中的对象，避免撤销/重做后出现“幽灵选择”。
            var set = new HashSet<Stroke>();
            for (int i = 0; i < count; i++)
            {
                Stroke s = strokes![i];
                if (s is not null)
                {
                    set.Add(s);
                }
            }

            var ordered = new List<Stroke>(set.Count);
            IReadOnlyList<IBoardInkItem> inkItems = _context.Document.InkItems;
            for (int i = 0; i < inkItems.Count; i++)
            {
                if (inkItems[i] is Stroke s && set.Contains(s))
                {
                    ordered.Add(s);
                }
            }

            bool unchanged = _selectedElement is null && InkItemListComparer.IsSameList(_selectedStrokes, ordered);
            if (unchanged)
            {
                return;
            }

            _selectedStrokes.Clear();
            _selectedStrokes.AddRange(ordered);
            _selectedElement = null;
            RaiseSelectionChanged();
        }

        private void SetSelectedElement(BoardElement? element)
        {
            if (ReferenceEquals(_selectedElement, element) && _selectedStrokes.Count == 0)
            {
                return;
            }

            _selectedElement = element;
            _selectedStrokes.Clear();
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
            if (_selectedStrokes.Count > 0)
            {
                return IsScreenPointInsideSelectedStrokesBounds(_selectedStrokes, screenDip);
            }

            if (_selectedElement is BoardElement element)
            {
                return IsScreenPointInsideSelectedElementBounds(element, screenDip);
            }

            return false;
        }

        private bool IsScreenPointInsideSelectedStrokesBounds(IReadOnlyList<Stroke> strokes, Vector2 screenDip)
        {
            if (strokes is null || strokes.Count == 0)
            {
                return false;
            }

            Matrix3x2 worldToScreen = _context.Viewport.GetWorldToScreenTransform();
            if (!InkItemScreenBounds.TryGetInkItemsBoundsScreenDip(strokes, worldToScreen, out Rect bounds))
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
        private void HitTestSelectableAtScreenPoint(Vector2 screenDip, out Stroke? stroke, out BoardElement? element)
        {
            stroke = null;
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
            // 阶段一集合中仅折线笔迹，故命中即 Stroke；非 Stroke 条目留待阶段二扩展选择语义。
            IBoardInkItem? hitItem = InkItemPickTest.HitTestTopMostInkItem(_context.Document.InkItems, pointWorld, toleranceWorld);
            if (hitItem is not null)
            {
                stroke = hitItem as Stroke;
                return;
            }

            element = ElementPickTest.HitTestTopMostElement(_context.Document.ElementsBelowInk, pointWorld, toleranceWorld);
        }

        private List<Stroke> HitTestSelectableStrokesInWorldRect(Vector2 minWorld, Vector2 maxWorld)
        {
            // 交互约定：元素只能通过“单击”选中，不支持框选。
            // 框选命中按条目类型单点分发；阶段一仅折线笔迹可被框选，
            // 非 Stroke 条目过滤掉，留待阶段二扩展选择语义（SetSelectedStrokes 会按文档顺序重新归一化）。
            List<IBoardInkItem> hits = InkItemRectSelectTest.HitTestInkItemsInWorldRect(_context.Document.InkItems, minWorld, maxWorld);

            var selectedStrokes = new List<Stroke>(hits.Count);
            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i] is Stroke stroke)
                {
                    selectedStrokes.Add(stroke);
                }
            }

            return selectedStrokes;
        }

        #endregion

        #region 选择变换（拖动选中项 / 滚轮 / 双指手势）

        /// <summary>
        /// 开始“拖动选中项”手势：对当前选中集或选中元素建立变换快照。
        /// </summary>
        public void BeginSelectionMove()
        {
            if (_selectedStrokes.Count > 0)
            {
                BeginSelectionTransformSnapshotForStrokes(_selectedStrokes);
            }
            else if (_selectedElement is BoardElement element)
            {
                BeginElementTransformSnapshot(element);
            }
        }

        /// <summary>
        /// 对选中笔迹集合建立变换快照（滚轮/双指路径；若快照已对应同一集合则复用）。
        /// </summary>
        public void BeginSelectionTransformSnapshotForSelectedStrokes()
        {
            BeginSelectionTransformSnapshotForStrokes(_selectedStrokes);
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

        /// <summary>按屏幕增量平移选中对象（拖动选中项路径）。</summary>
        public void MoveSelectionByScreenDelta(Vector2 deltaScreenDip)
        {
            if (_selectionStrokeBeforeSnapshots is not null && _selectedStrokes.Count > 0)
            {
                Vector2 deltaWorld = deltaScreenDip / Math.Max(0.0001f, _context.Viewport.Zoom);
                for (int i = 0; i < _selectedStrokes.Count; i++)
                {
                    _selectedStrokes[i].Translate(deltaWorld);
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

        /// <summary>对选中笔迹集合应用 2D 变换（滚轮缩放/旋转、双指捏合路径）。</summary>
        public void ApplyTransformToSelectedStrokes(Matrix3x2 transform)
        {
            for (int i = 0; i < _selectedStrokes.Count; i++)
            {
                _selectedStrokes[i].Transform(transform);
            }

            _selectionModified = true;
        }

        /// <summary>标记选中对象已发生变换（元素滚轮缩放/双指变换等直接操作元素后调用）。</summary>
        public void MarkSelectionModified()
        {
            _selectionModified = true;
        }

        /// <summary>
        /// 计算选中笔迹集合的整体中心（世界坐标）。
        /// </summary>
        public Vector2 GetSelectedStrokesCenterWorld()
        {
            // 单笔迹：沿用既有中心逻辑（Bounds 优先，否则点集平均）。
            if (_selectedStrokes.Count == 1)
            {
                return GetStrokeCenterWorld(_selectedStrokes[0]);
            }

            // 多笔迹：以“包围盒中心”为整体中心，更符合用户对“作为整体旋转”的直觉。
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            bool hasAny = false;

            for (int i = 0; i < _selectedStrokes.Count; i++)
            {
                Stroke stroke = _selectedStrokes[i];
                if (stroke.Points.Count == 0)
                {
                    continue;
                }

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
            }

            return hasAny ? (min + max) / 2.0f : Vector2.Zero;
        }

        /// <summary>
        /// 提交选择变换：把快照差异写入撤销记录（一次连续交互对应一次 Ctrl+Z）。
        /// </summary>
        /// <remarks>
        /// pointerId/捕获释放由控制器负责；本方法只处理快照与命令。
        /// </remarks>
        public void CommitSelection()
        {
            List<StrokeTransformSnapshot>? strokeSnapshots = _selectionStrokeBeforeSnapshots;
            BoardElement? element = _selectionTransformElement;
            Vector2? elementBeforePos = _selectionElementBeforePositionWorld;
            Vector2? elementBeforeSize = _selectionElementBeforeSizeWorld;

            _selectionStrokeBeforeSnapshots = null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;

            // 统一把“多笔迹变换/元素变换”合并成一次撤销记录，保证用户一次操作对应一次 Ctrl+Z。
            List<IBoardCommand>? commands = null;

            if (_selectionModified && strokeSnapshots is { Count: > 0 })
            {
                for (int i = 0; i < strokeSnapshots.Count; i++)
                {
                    StrokeTransformSnapshot snap = strokeSnapshots[i];
                    var after = new List<StrokePoint>(snap.Stroke.Points);
                    if (IsSameStrokePointList(snap.BeforePoints, after))
                    {
                        continue;
                    }

                    commands ??= new List<IBoardCommand>();
                    commands.Add(new UpdateStrokePointsCommand(snap.Stroke, snap.BeforePoints, after));
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
            if (_selectionStrokeBeforeSnapshots is { Count: > 0 } strokeSnapshots)
            {
                for (int i = 0; i < strokeSnapshots.Count; i++)
                {
                    StrokeTransformSnapshot snap = strokeSnapshots[i];
                    RestoreStrokePoints(snap.Stroke, snap.BeforePoints);
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

            _selectionStrokeBeforeSnapshots = null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;
            _selectionModified = false;
        }

        private void BeginSelectionTransformSnapshotForStrokes(IReadOnlyList<Stroke> strokes)
        {
            // 选择变换快照规则：
            // - 一次连续交互（拖拽/触摸/滚轮）只创建一次“Before”快照；
            // - 快照按当前选择集合顺序记录，提交时按相同顺序生成撤销命令；
            // - 仅支持“笔迹集合”或“单元素”二选一，避免产生混合撤销记录。
            if (IsSameSelectionStrokeSnapshot(strokes))
            {
                return;
            }

            var snapshots = new List<StrokeTransformSnapshot>(strokes.Count);
            for (int i = 0; i < strokes.Count; i++)
            {
                Stroke stroke = strokes[i];
                if (stroke is null)
                {
                    continue;
                }

                snapshots.Add(new StrokeTransformSnapshot(stroke));
            }

            _selectionStrokeBeforeSnapshots = snapshots.Count > 0 ? snapshots : null;
            _selectionTransformElement = null;
            _selectionElementBeforePositionWorld = null;
            _selectionElementBeforeSizeWorld = null;
            _selectionModified = false;
        }

        private void BeginElementTransformSnapshot(BoardElement element)
        {
            _selectionStrokeBeforeSnapshots = null;

            _selectionTransformElement = element;
            _selectionElementBeforePositionWorld = element.PositionWorld;
            _selectionElementBeforeSizeWorld = element.SizeWorld;
            _selectionModified = false;
        }

        private bool IsSameSelectionStrokeSnapshot(IReadOnlyList<Stroke> strokes)
        {
            if (_selectionStrokeBeforeSnapshots is not { Count: > 0 } snapshots)
            {
                return false;
            }

            if (strokes.Count != snapshots.Count)
            {
                return false;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (!ReferenceEquals(strokes[i], snapshots[i].Stroke))
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

        private static Vector2 GetStrokeCenterWorld(Stroke stroke)
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

        /// <summary>选择变换快照：记录单条笔迹的点列“Before”状态。</summary>
        private sealed class StrokeTransformSnapshot
        {
            public StrokeTransformSnapshot(Stroke stroke)
            {
                Stroke = stroke ?? throw new ArgumentNullException(nameof(stroke));
                BeforePoints = new List<StrokePoint>(stroke.Points);
            }

            public Stroke Stroke { get; }

            public List<StrokePoint> BeforePoints { get; }
        }
    }
}
