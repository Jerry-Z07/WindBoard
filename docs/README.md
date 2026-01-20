# WindBoard 文档

> [!NOTE]
> 本文档与代码库保持同步更新；如果你发现文档与实际行为不一致，欢迎提交 Issue / PR。

## 用户文档

- [快速上手](user/quick-start.md)：界面与常用操作
- [导入、附件与导出](user/import-export.md)：图片/视频/文本/链接/WBI 导入，附件编辑，导出 PNG/JPG/PDF/WBI

## 开发文档

- [笔迹渲染链路](dev/ink-rendering.md)：DX11/DX9Ex/D3DImage 的原因、代码入口与 fallback 行为

## Release 发布
- 发布版本说明（可选双语）：在打 tag 前添加 `docs/release-notes/<tag>.zh-CN.md` 与 `docs/release-notes/<tag>.en-US.md`，release workflow 会优先使用它们生成 GitHub Release 描述与 `latest.json` 的更新日志。
