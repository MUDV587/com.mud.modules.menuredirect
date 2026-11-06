# 设计与内部实现

本页概述架构，供贡献者参考。更详细的中文设计文档见
[Documentation~/Design.md](https://github.com/MUDV587/com.mud.modules.menuredirect/blob/main/Documentation~/Design.md)。

## 组件

| 组件 | 职责 |
|---|---|
| `MenuRedirectRule` | 可序列化规则数据：原路径、新路径、优先级、启用。 |
| `MenuRedirectSettings` | 项目级 `ScriptableSingleton`，保存在 `ProjectSettings/MenuRedirect.asset`。 |
| `MenuRedirectSettingsProvider` | Project Settings 页面、规则列表、详情、导入导出、应用/还原。 |
| `MenuRedirectPickerWindow` | 可搜索的原路径/父级路径选择器。 |
| `MenuRedirectValidator` | 列表、详情和应用共用的验证层。 |
| `MenuRedirectEngine` | 生成包装、迁移文件、隐藏菜单、域重载重放。 |
| `MenuRegistry` | 扫描 `[MenuItem]` 声明，记录程序集、方法、validate、优先级。 |
| `MenuRedirectBridge` | 反射调用 Unity 内部菜单 API。 |
| `MenuRedirectGuard` | 受保护菜单和 Unity 归属判断。 |
| `MenuRedirectInvoker` | 反射执行原始 `[MenuItem]` 方法。 |
| `MenuRedirectCleanup` | 卸载前删除受管理的生成文件。 |
| `MenuRedirectLocalization` | 中英文文案和按项目的语言偏好。 |
| `MenuRedirectBootstrap` | 域重载后重放隐藏。 |

## 数据流

```text
设置页
  → MenuRedirectValidator
  → MenuRedirectEngine.PrepareApply
      → 生成 MenuRedirects.g.cs
      → 保存 ProjectSettings/MenuRedirect.asset
      → 隐藏原菜单（或等待重载）
  → MenuRedirectBootstrap 重载后
      → 校验生成代码与规则一致
      → 重放隐藏
```

## 为什么需要生成包装

Unity 菜单项在编译期注册，纯运行时的重定向无法创建新的 `[MenuItem]` 路径。
因此工具生成一个 C# 文件，用 `[MenuItem(新路径)]` 包装方法，并通过反射调用原方法。

原菜单通过 Unity 内部菜单 API 隐藏。包装是必要的，因为原菜单移除后
`EditorApplication.ExecuteMenuItem` 会返回 `false`。

## 保护模型

Unity 内置顶级菜单和 Unity 程序集声明的条目受保护。Unity 归属通过以下信息判断：

- 程序集位置；
- 包程序集的 `PackageInfo`；
- 工程程序集的 asmdef/asmref 路径。

`Tools` 根菜单有意不保护，因为它常被第三方工具使用。

## 事务式应用

应用先写新生成文件，再保存配置，最后删除旧文件。失败时回滚新文件并保留旧路径。
工具不会覆盖非生成文件；存在多个受管理生成文件时会中止。

## 性能

设置页是 IMGUI，容易受到每帧分配影响。当前实现：

- 缓存规则行显示内容、验证消息、按钮宽度和彩色标签样式；
- 缓存解析后的界面语言和语言下拉选项；
- 工具栏计数在验证结果变化时一次性统计；
- 缓存生成代码路径，文件时间戳每秒最多检查一次；
- 输入校验防抖 0.15 秒，草稿写盘防抖 0.75 秒；
- 拖动分隔条期间推迟校验和草稿写盘；
- 拖动中的 `MouseDrag` / `MouseUp` 走轻量快速路径，但随后的 `Layout` / `Repaint`
  仍按新宽度完整排版，保持完全实时变宽。

完全实时变宽的固有成本是每个可见帧都要重新排版和重绘左右两栏。
预览线或降低刷新率可以规避，但当前有意不使用。

## 测试

`Tests/Editor` 下的 EditMode 测试覆盖：

- 规则验证和受保护菜单判定；
- 分支/叶子分类；
- 生成代码事务与回滚；
- 规则文件导入/导出校验；
- 基于程序集位置、PackageInfo、asmdef/asmref 的 Unity 归属判定；
- 本地化键覆盖。

运行 **Window > General > Test Runner > EditMode > Run All**。
