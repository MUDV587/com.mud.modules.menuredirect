# 变更日志

完整变更日志见 [CHANGELOG.md](https://github.com/MUDV587/com.mud.modules.menuredirect/blob/main/CHANGELOG.md)。
GitHub Releases 包含相同说明和可下载的 tag。

## 未发布

- 设置页左右两栏改为共用一条 1px 黑色分割线，移除两栏之间的空白区域；
  拖动分隔条时不再绘制蓝色高亮竖条，仅保留黑线与左右箭头光标作为反馈，
  右栏内容更靠近分割线。

## 0.2.1 — 2026-11-06

- Package Manager 显示名称改为 `MUD.Modules.MenuRedirect`。

## 0.2.0 — 2026-11-06

- 包名改为 `com.mud.modules.menuredirect`，程序集改为 `MUD.Modules.MenuRedirect.Editor`，
  菜单入口改为 **Tools > MUD > Menu Redirect**。
- 新增规则搜索/筛选、未保存草稿恢复、规则导入导出、父级路径选择器。
- 新增共享验证、生成代码事务、域重载安全的隐藏重放。
- 优化设置页性能和分隔条拖动；拖动期间延迟校验和草稿写盘。
- 新增 84 个 EditMode 回归测试。

所有版本见 [GitHub Releases](https://github.com/MUDV587/com.mud.modules.menuredirect/releases)。
