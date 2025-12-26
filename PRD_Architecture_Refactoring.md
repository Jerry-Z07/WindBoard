# WindBoard 架构重构 PRD

## 1. 项目背景

WindBoard 是一个基于 .NET 10.0 和 WPF 的白板应用，使用 MaterialDesignThemes v5.3.0 作为 UI 框架，InkCanvas 作为主要绘图区域。

当前项目代码组织混乱，MainWindow 类承担了过多职责，缺乏清晰的架构分层，导致代码难以维护和扩展。

## 2. 当前问题分析

### 2.1 架构问题
- **缺少分层架构**：没有明确的输入层、模式层、业务层划分
- **职责混乱**：MainWindow 承担输入处理、模式管理、UI 状态、页面管理、笔迹处理、缩放平移、自动扩容等多个职责
- **代码耦合严重**：各功能模块之间耦合度高，难以独立测试和维护

### 2.2 模式管理问题
- **缺少模式抽象**：没有统一的模式基类和接口
- **模式切换逻辑分散**：橡皮擦、书写、选择等模式切换逻辑分散在各处
- **没有区分主模式和激活模式**：无法支持手势擦除等瞬时交互

### 2.3 输入处理问题
- **输入过滤层缺失**：无法灵活处理手势擦除、元素交互冲突等场景
- **输入处理器不统一**：鼠标、触摸、触笔的处理逻辑分散，难以扩展新的输入设备

### 2.4 可扩展性问题
- **添加新模式困难**：需要修改多处代码
- **添加新过滤器困难**：没有统一的过滤器接口
- **难以支持新的交互方式**

## 3. 重构目标

### 3.1 架构目标
- 建立清晰的三层架构：输入前置层、模式管理层、业务处理层
- 实现职责分离，每个类只负责单一职责


### 3.2 功能目标
- 实现统一的模式管理系统
- 支持主模式（CurrentMode）和激活模式（ActiveMode）
- 保持现有功能完全兼容


## 4. 架构设计

### 4.1 整体架构

```
┌─────────────────────────────────────────────────────────┐
│                    Views (视图层)                        │
│  MainWindow.xaml.cs - UI绑定和事件路由                   │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│              Core (核心框架层)                           │
│  ┌──────────────────────────────────────────────────┐  │
│  │  Input (输入前置层)                               │  │
│  │  - InputDeviceType.cs                            │  │
│  │  - InputEventArgs.cs                             │  │
│  │  - InputManager.cs                              │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────┐  │
│  │  Modes (模式层)                                   │  │
│  │  - IInteractionMode.cs                           │  │
│  │  - InteractionModeBase.cs                        │  │
│  │  - ModeController.cs                             │  │
│  │  - InkMode.cs                                    │  │
│  │  - EraserMode.cs                                 │  │
│  │  - SelectMode.cs                                 │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────┐  │
│  │  Filters (过滤层)                                 │  │
│  │  - IInputFilter.cs                               │  │
│  │  - InputFilterBase.cs                            │  │
│  │  - ExclusiveModeFilter.cs                        │  │
│  └──────────────────────────────────────────────────┘  │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│              Services (业务服务层)                       │
│  - StrokeService.cs      - 笔迹服务                      │
│  - PageService.cs        - 页面服务                      │
│  - ZoomPanService.cs     - 缩放平移服务                  │
│  - AutoExpandService.cs  - 自动扩容服务                  │
└─────────────────────────────────────────────────────────┘
```

### 4.2 核心类设计

#### 4.2.1 输入前置层

**InputEventArgs.cs**


**InputManager.cs**
- 统一管理鼠标、触摸、触笔输入
- 保证输入的成对性（按下-移动-抬起）
- 将输入转发给 ModeController

#### 4.2.2 模式层

**IInteractionMode.cs**


**InteractionModeBase.cs**
- 实现 IInteractionMode 接口
- 提供通用的模式生命周期管理
- 子类实现具体的业务逻辑

**ModeController.cs**
- 管理当前模式（CurrentMode）
- 管理激活模式（ActiveMode）
- 处理模式切换时的输入补充
- 将输入转发给当前生效的模式

#### 4.2.3 过滤层

**IInputFilter.cs**


**InputFilterBase.cs**
- 实现 IInputFilter 接口
- 提供优先级机制
- 子类实现具体的过滤逻辑


**ExclusiveModeFilter.cs**
- 处理元素交互冲突
- 当命中特殊元素时，激活 NoMode

### 4.3 业务服务层

**StrokeService.cs**
- 笔迹的创建、修改、删除
- 笔迹平滑处理
- 笔迹的保存和加载

**PageService.cs**
- 页面的创建、删除、切换
- 页面预览生成
- 页面状态的保存和加载

**ZoomPanService.cs**
- 缩放管理
- 平移管理
- 视口状态管理

**AutoExpandService.cs**
- 自动扩容逻辑
- 内容平移逻辑

## 5. 重构步骤

### 阶段一：创建核心框架

#### 步骤 1.1：创建目录结构
- 创建 `Core/Input/` 目录
- 创建 `Core/Modes/` 目录
- 创建 `Core/Filters/` 目录
- 创建 `Services/` 目录

#### 步骤 1.2：实现输入前置层
- 创建 `InputDeviceType.cs` 枚举
- 创建 `InputEventArgs.cs` 类
- 创建 `InputManager.cs` 类
- 从 `MainWindow.InputDevice.cs` 迁移输入事件定义

#### 步骤 1.3：实现模式基类
- 创建 `IInteractionMode.cs` 接口
- 创建 `InteractionModeBase.cs` 抽象类
- 创建 `ModeController.cs` 类

#### 步骤 1.4：实现过滤器基类
- 创建 `IInputFilter.cs` 接口
- 创建 `InputFilterBase.cs` 抽象类

### 阶段二：实现具体模式

#### 步骤 2.1：实现 InkMode
- 从 `MainWindow` 中提取书写逻辑
- 实现 `SwitchOn` 和 `SwitchOff`
- 实现输入处理方法

#### 步骤 2.2：实现 EraserMode
- 从 `MainWindow.Eraser.cs` 中提取橡皮擦逻辑

#### 步骤 2.3：实现 SelectMode
- 实现选择逻辑
- 实现元素拖拽逻辑

#### 步骤 2.4：实现 NoMode
- 用于元素交互冲突场景
- 不处理任何输入

### 阶段三：实现过滤器

#### 步骤 3.1：实现 GestureEraserFilter
- 从 `MainWindow.Touch.cs` 中提取手势擦除逻辑
- 实现触摸面积检测
- 实现模式切换逻辑

#### 步骤 3.2：实现 ExclusiveModeFilter
- 实现元素命中测试
- 实现优先级判断

### 阶段四：提取业务服务

#### 步骤 4.1：实现 StrokeService
- 从 `MainWindow.StrokeSmoothing.cs` 中提取笔迹平滑逻辑
- 实现笔迹管理方法

#### 步骤 4.2：实现 PageService
- 从 `MainWindow.Pages.cs` 中提取页面管理逻辑
- 实现页面预览生成

#### 步骤 4.3：实现 ZoomPanService
- 从 `MainWindow.ZoomPan.cs` 中提取缩放平移逻辑
- 实现视口状态管理

#### 步骤 4.4：实现 AutoExpandService
- 从 `MainWindow.AutoExpand.cs` 中提取自动扩容逻辑
- 实现内容平移逻辑

### 阶段五：重构 MainWindow

#### 步骤 5.1：简化 MainWindow
- 移除已迁移的代码
- 保留 UI 绑定和事件路由
- 使用 InputManager 处理输入
- 使用 ModeController 管理模式

#### 步骤 5.2：集成服务层
- 在 MainWindow 中使用各个服务
- 确保服务之间的协作正确




## 9. 附录

### 9.1 术语表
- **CurrentMode**：当前模式，用户明确选择的主模式
- **ActiveMode**：激活模式，根据用户行为临时激活的模式
- **Filter**：过滤器，用于决定输入数据流向
- **InputProcessor**：输入处理器，包括模式和过滤器

