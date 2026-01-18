# WPF + WinUI 3 Island Canvas - Design

## Goal

在不迁移整个应用 UI 的前提下，把“画布区域”从 WPF 的 `D3DImage`/D3D9 互操作链路，迁移为 WinUI 3 的 `SwapChainPanel` + DXGI swap chain 呈现，从而获得：

- 更直接、更稳定的 DX11 呈现链路（不再依赖 `IDirect3DSurface9`）
- 画布内 Overlay 在同一 WinUI XAML tree 内合成（避免 airspace 问题）

WPF 继续承担：主窗口壳、MaterialDesign UI、设置窗口、对话框等。

## Proposed Architecture

### 1) WPF Shell (existing)

- `MainWindow` 保持现状：工具栏/弹窗/设置等全部仍在 WPF
- 仅替换 Viewport 内部“画布区域”的承载方式

### 2) Canvas Island (new)

新增一个 WinUI 3 画布模块，内部包含两层：

1. **World Layer（随 zoom/pan 变换）**
   - `SwapChainPanel`：绘制墨迹（DXGI swap chain）
   - Attachments（底层/置顶层）：图片/文本/视频/链接卡片（WinUI XAML 或 DX 绘制，取决于复杂度）
2. **Overlay Layer（不随 zoom/pan 缩放）**
   - SelectionDock（浮动操作条）
   - 临时 UI（提示、工具反馈等）

### 3) Interop Host (WPF -> WinUI)

WPF 侧提供一个宿主控件（例如 `WinUIIslandHost`），职责：

- 创建子 HWND（用于承载 WinUI XAML）
- 初始化/释放 WinUI XAML manager（线程与生命周期要严格控制）
- 把 WinUI 画布控件设置为 island 内容
- 同步尺寸变化与 DPI（必要时提供 pixel size）

## Rendering Flow

1. WinUI 画布控件创建 D3D11 device/context（BGRA 支持）
2. 为 `SwapChainPanel` 创建 DXGI swap chain
3. 每次需要重绘时：
   - 获取 swap chain backbuffer（`ID3D11Texture2D`）
   - 调用现有 `InkDxRenderer.Render(...)`，把 ink 绘制到 backbuffer
   - `Present`

关键点：

- 尽量复用现有 `InkDxRenderer`（其本身不依赖 WPF）
- device lost / resize / DPI 变化时必须可重建 swap chain 与 D2D 资源，并触发一次全量重绘

## Input Bridge

WinUI 侧采集 Pointer 输入：

- 鼠标/触摸/笔：PointerPressed/PointerMoved/PointerReleased
- 笔压：pressure（若设备提供）
- intermediate points：提高采样密度，改善跟手

然后转换为“平台无关的输入 DTO”，交给现有 Mode 系统处理（Ink/Eraser/Select），以尽量避免重写交互算法。

注意：现有 `Core/Input` 与 modes 目前包含 `System.Windows.*` 类型依赖；本方案倾向于：

- **优先做“桥接而不是全量去 WPF 化”**（保持范围小）
- 只有当跨模块必须共享某类型时，才抽出到共享 DTO（或逐步收敛到 `System.Numerics`）

## Data Boundary / Model Cleanup

为了避免 WinUI 模块引用 WPF 类型：

- `BoardAttachment` 不应直接持有 WPF `ImageSource`
  - 改为保存 image asset 的路径/标识符
  - WPF/WinUI 各自实现图片加载与缓存服务
- `BoardPage.Preview`（WPF UI 专用）可保留在 WPF 层；WinUI 画布不需要依赖它

## Rollout Strategy

按“最小可行链路”推进：

1. Spike：WPF 内成功嵌入 WinUI 控件，并在 swap chain 上清屏/绘制测试图形
2. 接入 InkDxRenderer：画面正确呈现，验证 resize/DPI/device lost
3. 输入桥接：能书写/擦除/选择
4. 迁移 Overlay：逐个功能替换（橡皮擦 -> 选择框 -> 附件 -> SelectionDock）
5. 清理旧路径：移除 `D3DImage`/D3D9 依赖
