# Changelog

All notable changes to this package are documented in this file.
The format follows Keep a Changelog; versioning follows SemVer.

## [Unreleased]

### Changed

- Settings page: the two columns now share a single 1px black divider line inside one
  container; the empty gap between the rules list and rule details is gone. Splitter
  dragging keeps the black line and the horizontal-resize cursor as the only feedback
  (the blue hover/drag highlight was removed), and the right column's content sits
  closer to the divider line.

### 中文摘要

- 设置页左右两栏改为共用一条 1px 黑色分割线，移除两栏之间的空白区域；
  拖动分隔条时不再绘制蓝色高亮竖条，仅保留黑线与左右箭头光标作为反馈，
  右栏内容更靠近分割线。

## [0.2.1] - 2026-11-06

### Changed

- Changed the Package Manager display name to `MUD.Modules.MenuRedirect`.
  The package name, namespace, assembly name, and menu entry are unchanged.

### 中文摘要

- Package Manager 显示名称改为 `MUD.Modules.MenuRedirect`。
  包名、命名空间、程序集名称和菜单入口保持不变。

## [0.2.0] - 2026-11-06

### Highlights

- Renamed the package to `com.mud.modules.menuredirect`, the editor assembly to
  `MUD.Modules.MenuRedirect.Editor`, and the menu entry to `Tools/MUD/Menu Redirect`.
- Added rule search/filter, unsaved-draft recovery, rule import/export, and the parent-path picker.
- Added shared validation, transactional generated-code updates, and domain-reload-safe hiding.
- Improved settings-page performance and splitter dragging; validation and draft writes are deferred
  while dragging.
- Added 84 EditMode regression tests.

### 中文摘要

- 包名改为 `com.mud.modules.menuredirect`，程序集改为 `MUD.Modules.MenuRedirect.Editor`，
  菜单入口改为 `Tools/MUD/Menu Redirect`。
- 新增规则搜索/筛选、未保存草稿恢复、规则导入导出、父级路径选择器。
- 新增共享验证、生成代码事务、域重载安全的隐藏重放。
- 优化设置页性能和分隔条拖动，拖动期间延迟校验和草稿写盘。
- 新增 84 个 EditMode 回归测试。

> Historical entries below are retained in the form and names used at the time.

### Detailed notes (Chinese, development history)

### Added

- 左栏规则搜索与筛选：搜索框按原路径/新路径模糊匹配（不区分大小写），
  “仅显示问题”按钮只保留有警告或错误的规则；匹配为空时显示筛选空状态和“清除筛选”按钮。
  有筛选时禁用拖动排序，新增/导入/还原自动清除筛选。
- 未保存草稿保护：规则和生成目录被修改后，0.75 秒防抖写入 `Library/MenuRedirectDraft.json`
  （不进版本库）；离开页面、域重载、退出编辑器时同步落盘。打开页面时若草稿的“已保存基线”
  仍与当前配置一致，则恢复草稿并提示“已恢复上次未保存的修改”；应用或还原后删除草稿。
  草稿不校验路径合法性，崩溃前输入到一半的内容也能恢复。
- 域重载性能去重：`MenuRedirectEngine` 新增 `TryHideOriginals(settings, out reason)`，
  一次评估同时完成“包装代码是否就绪”的复核与隐藏重放，避免域重载时重复构建生成代码；
  `BuildGeneratedContent` / `HasPendingCodeChanges` / `IsGeneratedCodeReady` 均支持传入已评估结果。
- 输入校验防抖：`MarkDirty` 只标记脏状态，0.15 秒后由 `FlushDeferredValidation` 统一执行
  `BuildRuleValidations`，路径字段编辑等高频输入不再立即重跑完整校验。
- 分隔条悬停/拖动高亮：悬停或拖动时分隔条显示强调色中线，让可拖动区域更明显。
- 分隔条真实宽度：只在 `Repaint` 阶段记录水平分栏的实际布局宽度，忽略 `Layout` 阶段
  `GUILayoutUtility.GetLastRect()` 可能返回的 `1×1` 临时值，避免拖动被错误夹回左栏最小宽度；
  尚未测量时回退到 `EditorGUIUtility.currentViewWidth`。

### Changed

- 核心功能重写为菜单重定向：规则（原路径 → 新路径）存于 `ProjectSettings/MenuRedirect.asset`；
  点"应用"后隐藏原菜单（内部 `UnityEditor.Menu.RemoveMenuItem`，域重载自动重放）并生成 MenuItem
  包装代码（默认 `Assets/MenuRedirects/Editor/MenuRedirects.g.cs`，目录可在设置页修改；需一次重编译）。
  若本次应用改动了包装代码，隐藏会延后到编译/域重载完成；代码不变时才立即隐藏。
- 配置改为 `UnityEditor.ScriptableSingleton<MenuRedirectSettings>`：项目唯一配置，文件位于
  `ProjectSettings/MenuRedirect.asset`，不再作为 `Assets` 下的普通资产；只有点“应用”才
  `Save(true)` 落盘。
- 配置界面从独立 `EditorWindow` 迁移到 Project Settings 页面
  `Project/MUD/Menu Redirect`；`Tools/MUD/Menu Redirect` 改为打开该页面。
- 配置页面改为左右分栏的规则管理器：左侧是可拖拽排序的紧凑列表（Unity `ReorderableList`），
  行高采用 `ReorderableList` 标准高度，行内按 `原路径 → 新路径`（分支路径显示 `/*`，
  仅隐藏为 `→ 隐藏`）显示菜单预览和纯数字优先级，优先级数字与右侧 `✕` 保留间距；
  最左侧用 `✓ / ! / ✕ / —` 表示通过 / 警告 / 错误 / 禁用，路径文字使用绿 / 黄 / 红 / 灰状态色，
  每行右侧提供 `✕` 移除按钮。右侧详情编辑面板分为预览、基本设置、路径映射和验证结果，
  顶部用“状态符号 + 路径”单行展示，启用开关只保留在详情中；支持 `还原` / `应用`、
  空状态和 Light/Dark 主题适配。
- 左右分栏之间新增可拖动分隔条：左栏默认约 32%（限制在 300～340），左栏最小 220、
  右栏最小 240，窗口变窄时自动夹紧以避免两栏重叠；分隔条作为真实布局列参与排版，
  拖动由 `GUIUtility.hotControl` 捕获。宽度不持久化，离开页面或域重载后恢复默认。
- 分隔条拖动流畅度优化：拖动中的 `MouseDrag` / `MouseUp` 事件走轻量快速路径，
  不再为不可见的中间事件阶段重建整页，只更新宽度并在变化时请求一次 `Repaint`；
  随后的 `Layout` / `Repaint` 仍按新宽度完整排版，保持完全实时变宽。同时缓存生成代码路径、
  界面语言解析结果与下拉选项、工具栏计数与宽度、按颜色缓存的标签样式，以及“上次隐藏错误”文本；
  校验和草稿写盘在拖动期间推迟到松开鼠标后执行，生成代码文件时间戳查询限制为每秒最多一次。
- 规则校验改为设置页状态、详情面板和“应用”共用的单一验证层：Unity 内置菜单/受保护容器、
  目标已存在、重定向源缺少可执行静态 `[MenuItem]`、规则重叠、路径格式非法等为红色错误并禁用“应用”；
  仅隐藏动态注册菜单、跳过受保护子项等为黄色警告。验证结果直接显示消息文本，不再重复添加 `!`/`✕` 前缀。
- 新增原路径菜单选择器：详情页“选择…”打开可搜索窗口，列出注册表中的完整路径、所有中间容器
  （如 `Window/Unity CLI Loop`）及其子项；复用 Unity 保护判断，`✕` 受保护条目不可选，
  支持 `↑/↓`、`Enter`、`Esc` 和刷新注册表；选中只修改内存中的原路径，仍需“应用”才保存和生成代码。
- 分支路径支持嵌套容器：无 `/` 的顶级路径，或自身未注册但有子项的中间容器可按分支隐藏/搬移；
  自身已注册 `[MenuItem]` 的中间路径仍按叶子处理。
- 顶部工具栏用纯文本统计“已启用 N 条 / 验证通过 N 条 / 警告 N 条 / 错误 N 条”，
  分别使用白（主题前景）、绿、黄、红色；“未保存”和“待重编译”仅显示文字，不使用背景色胶囊。
- 新增界面语言切换（跟随系统 / 中文 / English）；默认跟随系统，语言偏好按项目保存在本机
  `EditorPrefs`，不写入 `MenuRedirect.asset`，切换语言不会标记未保存、触发保存或包装代码重编译。
  设置页文案、验证消息、引擎警告和应用反馈均即时切换。
- 顶部动作区重新排列：`规则说明 / Help` 位于右侧动作组最左侧，语言标签右对齐并紧贴语言下拉框；
  `打开目录 / Open Folder` 和规则说明按钮按当前语言动态测量宽度，避免英文被截断。
- 新增“生成代码”目录设置：文件名固定为 `MenuRedirects.g.cs`，目录必须位于 `Assets` 下且包含
  `Editor` 目录；支持选择、打开、恢复默认。编辑目录会标记未保存，`还原` 恢复保存时的目录值，
  `应用` 才保存并把旧位置的包装代码迁移到新目录；目标位置已有非本工具生成的文件时阻止覆盖。
- 反馈提示恢复为原有 `HelpBox` 样式，并移到左右分栏下方；提示出现/消失只改变分栏底部可用高度，
  不再把顶部规则列表和详情整体向下推。
- 左侧每行的 `✕` 移除按钮缩小，并使用专用居中样式，与标准行高垂直居中。
- `还原` 使用加载/应用时的规则状态，只丢弃内存中的未保存修改，不写配置、不执行重定向；
  规则在应用前新增、删除或编辑都不会落盘。
- 不自动迁移旧 `Assets/Settings/MUD/MenuRedirect.asset`；旧规则需在新页面重新录入。
- 隐藏逻辑改为直接移除配置路径，不再依赖注册表子项、不再调用 `Menu.RebuildAllMenus`
  （重建会把刚移除的菜单恢复，导致"成功隐藏"但实际仍可见）；移除后使用 `MenuItemExists` 复核。
- 包装菜单改为调用 `MenuRedirectInvoker` 反射执行原始 `[MenuItem]` 方法，修复原菜单隐藏后
  `EditorApplication.ExecuteMenuItem` 返回 false、点击包装菜单无反应的问题。
- `MenuRegistry` 记录 `MethodInfo`、validate、priority，生成包装时同步生成 validate 包装。
- Unity 内置顶级菜单与 Unity 程序集条目继续受保护；`Tools` 根菜单不再受保护，允许第三方/自定义菜单移除。
- Unity 归属改为解析程序集安装位置、`PackageInfo` 与 asmdef/asmref 路径：Unity 安装目录和
  `com.unity.*` 官方包受保护；工程/第三方包即使程序集名为 `Unity.*` 也可移除；
  只有无法解析来源的内存程序集才回退到旧的程序集名前缀规则。
- 配置页面增加重复原路径/目标路径校验，以及"目标路径位于另一条规则要移除的菜单之下"的交叉校验。
- 新增共享验证层：目标菜单已存在、重定向源不存在或缺少可执行静态方法、分支无可用子项、
  原路径重叠、路径格式非法等错误会禁用“应用”；绕过的旧配置在应用前和域重载重放时也会被拦截。
- 新增嵌套受保护容器：容器下若有 Unity 程序集声明的子菜单，整体不能作为仅隐藏或叶子重定向目标；
  分支重定向会跳过这些子容器并保留其内容。
- 应用改为文件事务：先写入新包装代码，再保存配置；保存成功后删除旧包装文件，保存失败回滚刚写入的文件。
  旧文件不是本工具生成、目标位置被非生成文件占用、项目里存在其它残留包装文件时中止应用；
  旧文件删除失败时撤销新文件并把路径指回旧文件，避免重复类。
- 迁移改为大小写不敏感的同资产判断，避免 Windows 上仅大小写不同的路径把刚写入的新文件删除；
  旧包装文件只在首行匹配生成头部时删除。
- 域重载后的隐藏重放会先校验包装代码存在且与当前规则一致；编译失败或文件缺失时暂缓隐藏，
  避免原菜单已移除而新菜单尚未注册导致入口丢失。
- 支持一个方法声明多个 `[MenuItem]` 特性，并修复无修饰符快捷键（如 `_g`）被残留在路径中的问题。
- 独立入口：`Tools/MUD/Menu Redirect` 打开 `Project/MUD/Menu Redirect` 设置页。
- 重写 `Documentation~/Design.md` 为当前菜单重定向架构与风险说明；README 同步修正规则预览、上下文菜单和生成目录描述。
- 菜单路径选择器交互改进：单击整行即可展开/折叠（不再限于行首三角箭头），
  双击才选中；折叠/展开后保持当前滚动位置，不再自动滚动定位到选中项。
- 选择器高亮拆分为「选中」与「悬停」：深蓝标记规则当前已设置的值（`_currentPath`），
  单击/↑↓ 不移动选中标记；单击只在所点行显示浅蓝悬停（受保护条目也可悬停），
  双击/Enter 仍确认键盘焦点条目。

### Added

- 新增 `Tools/MUD/Menu Redirect/Clean Up Generated Files`：移除包前清理当前/上次生成路径中带本工具生成头的
  `MenuRedirects.g.cs`，保留 `ProjectSettings/MenuRedirect.asset`；没有生成头的用户文件不会被删除。
  README 增加移除包后的手动清理步骤。
- 新增目标父级选择器：新路径右侧“父级…”打开父级模式的选择窗口，只列出顶级路径和有子项的容器，
  受保护条目仍显示但不可选；分支规则直接使用所选父级，叶子规则保留当前新路径最后一段
  （为空时沿用原路径最后一段），原路径为空时填入父级并提示补全新菜单名称。
  选择只修改内存，仍需“应用”才保存和生成代码。
- 原路径/目标父级选择器改为可折叠树形视图：按菜单层级展示，行首箭头展开/折叠，默认全部展开；
  搜索按完整路径过滤并自动展开命中项及其祖先；键盘 `↑/↓`、`Enter`、`Esc` 和受保护条目
  不可选的规则保持不变。
- 新增规则 JSON 导入/导出：文件格式为 `MUD.MenuRedirect.Rules` v1，只包含规则条数和规则数组
  （原路径 / 新路径 / 优先级 / 启用），不含生成目录、语言等本机设置。导出便于跨项目复用；
  导入先完整校验格式、版本、条数、路径和重复原路径，通过后确认整体替换当前规则。
  取消或校验失败不修改内存，导入后仍需“应用”才保存配置和生成代码。
- 新增 `Tests/Editor` EditMode 回归测试，覆盖 `_g` 快捷键、多 `[MenuItem]`、重复可执行声明、
  目标冲突、受保护容器、规则重叠、路径格式、包装代码事务/回滚、生成代码就绪检查、
  规则文件导入/导出校验，以及 asmdef/asmref、包缓存和工程程序集的 Unity 归属判定。
- 新增快捷键迁移提示：原菜单快捷键不会随包装复制，生成包装代码时在受影响的方法前
  写入固定中文注释，Apply 成功后反馈区在主消息后追加快捷键提示（条数与快捷键列表，
  中英文本地化）；注册表新增按条目提取快捷键后缀的能力。仅提示，不改变快捷键行为。

### Fixed

- 修复隐藏后原菜单在刷新/重编译时复活的问题。
- 修复重定向包装菜单点击无效的问题。
- 修复仅大小写不同的生成目录迁移可能误删新文件的问题。
- 修复旧包装文件不是本工具生成时仍可能被覆盖/删除的问题。
- 修复编译失败时先隐藏原菜单、导致原菜单和新菜单同时消失的问题。
- 优化 Project Settings 页面性能：缓存规则校验和包装代码状态，避免点击或拖拽时每个 `OnGUI`
  事件重复构建菜单注册表、生成包装代码和读取文件。
- 进一步优化设置页每帧开销：规则行的状态/路径/优先级文案随 `RuleRowDisplay` 缓存为 `GUIContent`，
  工具栏统计与按钮、校验面板消息按语言和规则缓存，按钮与标签宽度按语言缓存并在
  语言/皮肤切换时失效；空规则清理改用预判 + 方法组，反馈区与计数标签不再每帧分配。
  只改缓存与分配，界面行为不变。

### Removed

- 移除旧 MenuHub 扫描/编目/诊断模块（`Models` / `Organization` / `Scanning` / `UI` 及散文件）。重定向模块自包含，可独立使用。
- 删除 `Assets/Editor/RemoveMenus.cs` 验证脚本，移除逻辑已合并进重定向引擎。

## [0.1.0] - 2026-10-16

### Added

- Renamed the package from `com.company.com.mud.modules.editor-menuhub` / `Company.EditorToolHub`
  to `com.mud.modules.menuhub` / `MUD.Modules.MenuHub.Editor`.
- Reflection scanner over all assemblies that reference `UnityEditor`: authoritative menu paths
  (including `nameof` / constant concatenation), `validate` / `priority` metadata, DLL plugin coverage.
- `PackageInfo`-based package attribution: disk paths (`assetPath` / `resolvedPath`,
  `Library/PackageCache`), `asmdef`-name reverse lookup for assemblies compiled into
  `Library/ScriptAssemblies`, and text-evidence upgrade; editor built-in packages copied into the
  project (`com.unity.2d.sprite`, `com.unity.ugui`, …) are classified as `UPM(内建)`.
- Source-text scanner producing `file:line` evidence (comments stripped, `~` folders and build
  directories pruned, qualified `[UnityEditor.MenuItem]` names supported, innermost-type
  attribution); records without a matching loaded assembly are flagged `仅文本`.
- Snapshot storage separated from user config:
  `ProjectSettings/MUD/MenuHubSnapshot.json` + `Assets/Settings/MUD/MenuHubConfig.asset`.
- Virtualized IMGUI catalog window: multi-token search, categories, source filter, favorites,
  recents, per-row context menu, manual entries, ignored path prefixes.
- Optional non-invasive menu hiding through the internal `UnityEditor.Menu` API, re-applied after
  domain reloads, with soft failure and diagnostics.
- Automation: dirty marker + auto rescan on package registration, auto scan on first open.
- Diagnostics window with scan stats, per-source counts and internal bridge status.

### Fixed

- `isDll` no longer counts source-compiled assemblies (`Library/ScriptAssemblies/*.dll`) as plugin
  DLLs; only precompiled plugin assemblies are marked as DLL.
- Package attribution for assemblies compiled into `Library/ScriptAssemblies` (TextMeshPro,
  2D Sprite, timeline, test framework, git packages, …) — previously misclassified as `Assets`.
- Editor built-in packages that live in the project (`com.unity.2d.sprite`, `com.unity.ugui`)
  were skipped entirely by the Unity-install filter; they are now scanned and labeled `UPM(内建)`.
