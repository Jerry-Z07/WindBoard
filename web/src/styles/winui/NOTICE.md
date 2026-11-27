# WinUI 主题移植说明

`web/src/styles/winui-tokens.css`、`web/src/styles/winui-controls.css` 与
`web/src/app/theme.ts` 中的 WinUI 令牌/组件规则移植自：

- 包：`@pstrikez/fluent-winui-theme@0.2.0`
- 仓库：<https://github.com/pStrikeZ/fluent-winui-theme>
- 上游 commit：`7d1acf2e1fcf93cf89804a96162a6907e94f2844`
- 许可：MIT

移植范围仅限 `--winui-*` 颜色/圆角令牌、Fluent 组件表面覆盖与 WinUI 强调色调色板；
未移植其 AppShell、GradientBackground、字体文件、toaster 或组件包装命名空间。

以下为上游 MIT 许可全文：

MIT License

Copyright (c) 2025 Menci
Copyright (c) 2026 pStrikeZ

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
