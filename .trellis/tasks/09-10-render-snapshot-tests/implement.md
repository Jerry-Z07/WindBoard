# P1 渲染快照测试 — 执行计划

## 前置

- [x] P0 已完成（构建告警基线干净）。

## 执行清单（按序）— 已全部完成（2026-09-10）

### 第 1 步：harness 骨架 + 自检
- [x] 实现 `OffscreenRenderHarness`（WARP 设备 → D2D context → 离屏位图 → 像素读回）。
- [x] 自检用例：空白文档渲染 → 断言整体为背景色（验证读回管线正确，防假阴性）。
- 验证：`dotnet test WindBoard.slnx --filter "FullyQualifiedName~BoardSceneRendererSnapshotTests"`
- 回滚点：仅新增文件，可整体删除。

### 第 2 步：比对器与基准管理
- [x] 实现 `SnapshotComparer`（容差比对 + diff 图 + PNG 导出）。
  - 实现偏差：WIC 编码需引入 Vortice.WIC 新包，改用主工程既有依赖 System.Drawing.Common（见 prd 偏差记录 2）。
- [x] 实现 `SnapshotBaseline`（`__snapshots__/` 目录、缺失生成、`WINDBOARD_REGEN_SNAPSHOTS=1` 再生成开关）。
- [x] 明确基准 PNG 的生成环境说明（注释写入 `SnapshotBaseline`）。

### 第 3 步：场景夹具与首批场景
- [x] `BoardSceneFixtures`：笔迹（压感点列）/形状/卡片构造（marquee/选中 overlay 为 XAML 层，见 prd 偏差记录 1）。
- [x] 首批场景（顺序）：空白 → 单笔迹（DeviceContext2/DrawInk 路径在 WARP 上验证通过）→ 多笔迹混合 → 视口变换。
- 验证：`dotnet test WindBoard.slnx --filter "FullyQualifiedName~BoardSceneRendererSnapshotTests"`
- [x] 评审门：单笔迹场景首次通过后，diff 三件套机制人工确认一次（见第 5 步人为回归验证）。

### 第 4 步：补齐剩余场景
- [x] 形状（矩形/椭圆/箭头/直线）→ 深浅主题卡片。
- [x] 全场景基准生成并入库（7 张基准 PNG，`WindBoard.Tests/Rendering/__snapshots__/`）。
- 选中 overlay / marquee：D2D 快照不可行，降级说明见 prd 偏差记录 1。

### 第 5 步：回归验证与收尾
- [x] 人为回归验证：临时注释椭圆绘制调用 → ShapesAllKinds 失败（差异 1.39%）且产出 diff 三件套（diff 图红色高亮缺失椭圆）→ 恢复（主工程 git 零残留）。
- [x] 全量：`dotnet test WindBoard.slnx` 全绿（538/538）。构建告警：本次新增代码零告警；存量 2 处 xUnit2013 位于他人在途文件 `PointerRouteStateTests.cs`（不属于本任务范围）。
- [x] 计时确认快照套件总时长 < 8 个场景 × 1s 量级（8 用例合计约 0.3s）。

### 执行中发现并修复的问题（补充记录）

- **`CreateBitmapFromDxgiSurface` 显式指定 `BitmapOptions.Target` 触发 E_INVALIDARG**：改为 bitmapProperties 传 null（由 D2D 从 surface 推断格式），与 OneMore 等成熟离屏实现一致。
- **文本卡片场景在全量并行测试下失败**：`AppSettingsServiceTests` 经 `AppLanguageService.Apply` 设置进程级 MRT `PrimaryLanguageOverride` 且其清理只还原 CultureInfo；残留 override 使 L10n 回退输出 key 字符串。unpackaged 环境下赋空串清除会抛异常（实测），文本场景改为渲染前覆写 override 为 zh-CN 并 finally 尽力还原（详见测试注释）。修复后并行窗口内仍复现一次竞态（写方为并行类的 Apply），最终在 `TestAssemblyConfig.cs` 以 `[assembly: CollectionBehavior(DisableTestParallelization = true)]` 关闭跨类并行彻底消除（全量串行 ~1s，连续 3 次全绿）。

## 验证命令汇总

```powershell
dotnet build WindBoard.slnx -c Release
dotnet test WindBoard.slnx --filter "FullyQualifiedName~WindBoard.Tests.Rendering"
dotnet test WindBoard.slnx
# 重建基准（渲染行为有意变更后使用，提交说明须说明理由）：
$env:WINDBOARD_REGEN_SNAPSHOTS = "1"; dotnet test WindBoard.slnx --filter "FullyQualifiedName~BoardSceneRendererSnapshotTests"
```

## 回滚点

- 每步独立提交粒度；任一步失败可回退到上一步，主工程始终零改动。
