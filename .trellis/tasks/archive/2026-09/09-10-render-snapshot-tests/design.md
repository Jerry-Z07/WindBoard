# P1 渲染快照测试 — 技术设计

## 1. 边界与不变量

- 被测对象：`WindBoard.Rendering.Board.BoardSceneRenderer`（internal，测试项目经 `InternalsVisibleTo` 访问）。
- 输入：`BoardDocument`（笔迹/元素集合）、`IBoardInkItem`（活动笔迹，可空）、`BoardViewport`（尺寸/缩放/相机）。
- 输出：BGRA 像素缓冲。
- **不变量：主工程代码零改动（本设计的硬目标）**。harness、比对器、基准管理全部位于 `WindBoard.Tests`。

## 2. 离屏渲染路径选型

| 方案 | 说明 | 结论 |
|---|---|---|
| A. WIC bitmap render target | `ID2D1Factory.CreateWicBitmapRenderTarget`，纯软件、零设备依赖 | **否决**：该目标是经典 `ID2D1RenderTarget`，无法升级为 `ID2D1DeviceContext`；而主程序是 D2D device context 路径，笔迹绘制若依赖 `DeviceContext2`（`_inkStyle`、`WithOptionalDeviceContext2` 暗示）将走不同分支，快照与真实渲染可能不一致 |
| B. D3D11 WARP + D2D device context + 离屏位图 | 与主程序同一 API 路径，仅设备换成 WARP 软件设备、目标换成离屏 bitmap | **采用**：行为一致性最高；Vortice 全覆盖所需 API |

方案 B 渲染流程（测试侧 `OffscreenRenderHarness`）：

```
D3D11CreateDevice(DriverType.Warp, BgraSupport)
  → QueryInterface<IDXGIDevice>
  → D2D1CreateFactory<ID2D1Factory1>(SingleThreaded)
  → factory.CreateDevice(dxgiDevice)
  → device.CreateDeviceContext(...)
  → context.CreateBitmap(desc, ...)          // 支持渲染目标的 D2D 位图
  → context.Target = bitmap; BeginDraw()
  → BoardSceneRenderer.Draw(context, doc, ink, viewport)
  → EndDraw()
  → CreateTexture2D(staging, CpuRead) + CopyResource(bitmap 资源)
  → Map 读出 BGRA 像素
```

- 参照实现：`DxSwapChainPanelRenderer.CreateDeviceResources`（L312-352）的设备创建顺序；其 `TryCreateD3DDevice` static 方法本身在 private 区域不可复用，测试侧按同模式实现（不复制调用，仅借鉴模式）。
- `ID2D1DeviceContext` 本身实现 `ID2D1RenderTarget`，可直接传入 `BoardSceneRenderer.Draw`，无需适配层。

## 3. 模块划分（均在 WindBoard.Tests/Rendering/Snapshot/ 下）

| 模块 | 职责 |
|---|---|
| `OffscreenRenderHarness.cs` | 设备/上下文/位图生命周期管理；`Render(document, viewport, drawAction)` 返回像素缓冲；实现 IDisposable |
| `SnapshotComparer.cs` | 逐像素比对（每通道容差）；生成 diff 缓冲（差异像素标红）；导出 PNG（WIC 编码） |
| `SnapshotBaseline.cs` | 基准目录解析（`__snapshots__/<场景名>.png`）；缺失/`WINDBOARD_REGEN_SNAPSHOTS=1` 时写入并跳过断言 |
| `BoardSceneFixtures.cs` | 场景构造工厂：文档 + 笔迹（含压感点列）/形状/文本卡片/选中态/marquee + 视口参数 |
| `BoardSceneRendererSnapshotTests.cs` | xUnit 用例：每场景一个 Fact |

## 4. 比对与容差策略

- 逐像素比较 RGBA 各通道，容差 `Tolerance = 3`（0-255，常量，可按场景覆写）。
- 失败判定：差异像素占比 > `MaxDiffPixelRatio`（默认 0，文本场景可放宽到 0.5%）。
- diff 图：差异像素置红、其余置灰，便于目视定位。

## 5. 已知风险与对策

| 风险 | 对策 |
|---|---|
| 笔迹绘制依赖 `ID2D1DeviceContext2`（DrawInk/InkStyle），离屏 context 是否具备 | 方案 B 的 context 与主程序同为 D2D device context，能力一致；实现首个笔迹场景时验证，若发现依赖 D3D11.1+ 特性而 WARP 不支持，回报并调整 |
| 文本（DWrite）渲染跨机器差异 | 容差吸收；仍不稳定则文本场景降级结构断言（记录在 prd 允许） |
| 图片元素缓存依赖 render target 指针（`_imageBitmapCacheRenderTargetPtr`） | 首批场景不含图片元素；图片快照列为后续增强，不在本任务范围 |
| xUnit 并行执行导致 D2D 资源竞争 | D2D factory 用 SingleThreaded；测试类加 `[Collection]` 串行化渲染类用例 |
| staging 读回代码错误导致假阴性 | 用"空白场景应全屏背景色"作为 harness 自检用例先行验证 |

## 6. 回滚

- 纯新增测试文件 + 一条 CI 无关改动；回滚即删除 `Snapshot/` 目录，主工程零影响。

## 7. 实现偏差记录（2026-09-10，实现时确认）

1. **PNG 编码**：WIC 需引入 Vortice.WIC 新包（既有依赖不含），改用主工程既有依赖 System.Drawing.Common（`SnapshotComparer`）；BGRA 缓冲与 `Format32bppArgb` 内存序逐字节对应。
2. **离屏位图创建**：`CreateBitmapFromDxgiSurface` 显式指定 `BitmapProperties1`（含 `BitmapOptions.Target`）实测触发 E_INVALIDARG；改为 bitmapProperties 传 null（D2D 从 surface 推断格式，DPI 默认 96 与上下文一致），DXGI surface 支撑的位图可直接 `SetTarget`（OneMore 等成熟实现同款）。
3. **场景 7/8（选中 overlay/marquee）**：实现时确认为 XAML 层元素，不属于 D2D 快照能力范围，降级处理见 prd 偏差记录 1。
4. **文本场景隔离**：除固定 zh-CN 区域性外，还需覆写进程级 MRT `PrimaryLanguageOverride`（其他测试残留会破坏 L10n 解析；unpackaged 环境赋空串清除会抛异常），详见测试注释与 prd 偏差记录 3。
