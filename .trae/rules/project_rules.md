# WindBoard 项目开发规则

## 1. 项目基础信息
- **项目类型**: .NET 10.0 WPF 应用程序
- **UI 框架**: MaterialDesignThemes v5.3.0 (Material Design 3)
- **核心组件**: InkCanvas 作为主要绘图区域
- **语言**: C# + XAML

## 2. 编码规范

### 2.1 命名规范
- **类名**: PascalCase (如: MainWindow, BoardPage)
- **方法名**: PascalCase (如: OnMouseDown, HandleTouchEvent)
- **变量名**: camelCase (如: inkCanvas, strokeThickness)
- **XAML 元素名**: PascalCase (如: MainInkCanvas, ColorPicker)

### 2.2 注释规范
- 使用中文注释，保持专业且简单易懂
- 复杂逻辑必须添加注释说明

## 3. 核心功能实现规则

### 3.1 绘图功能
- 默认笔触粗细: 3px
- 预设笔触粗细: 3, 6, 9px

### 3.2 缩放与平移
- 使用 ScaleTransform 实现缩放，应用于父 Grid 而非 InkCanvas
- 笔触粗细随缩放比例反向调整，保持视觉一致性
- 空格键 + 鼠标拖拽启用平移模式


## 5. 调试与测试
- 使用 Dispatcher.InvokeAsync 进行 UI 更新，优先级设为 Loaded

## 6. 代码复用原则
- 优先复用现有代码、组件和包
- 避免重复实现相同功能
- 通用功能封装为可复用的方法或类
