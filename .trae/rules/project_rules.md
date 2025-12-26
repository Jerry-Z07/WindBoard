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


## 6. 代码复用原则
- 优先复用现有代码、组件和包
- 避免重复实现相同功能
- 通用功能封装为可复用的方法或类
