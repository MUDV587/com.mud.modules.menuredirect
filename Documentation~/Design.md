# MUD Menu Redirect 设计文档

版本：0.2.1（Unity 2022.3 LTS，纯编辑器包）

本文描述当前“菜单重定向”实现；用户操作与限制以 `README.md` 为准。

## 1. 目标与非目标

**目标**：用一份项目级规则，把已有 `[MenuItem]` 从原路径搬到新路径，或直接隐藏原菜单。
规则在 `Project Settings > MUD > Menu Redirect` 中编辑；点“应用”后生成包装代码并隐藏原菜单。

**非侵入式承诺**：

- 不修改第三方源码、资产、asmdef、package.json。
- 只在编辑器运行时操作菜单缓存；隐藏失败只影响重定向，不影响编辑器其它功能。
- 包装代码是本工具生成的独立文件，只有首行带生成头部的文件才会被覆盖或迁移。

**非目标**：

- 不动态注册菜单；重定向依赖编译期 `[MenuItem]` 包装代码。
- 不复制原条目的快捷键；不迁移旧配置；不自动恢复本会话已隐藏的菜单（需一次域重载）。
- 不修改 asmdef/asmref；归属判定只读取程序集位置、`PackageInfo` 与 asmdef/asmref 路径，
  asmref 包含的脚本按目标程序集归属。

## 2. 命名与固定路径

| 项 | 值 |
|---|---|
| 包名 / 文件夹 | `com.mud.modules.menuredirect` |
| 程序集 | `MUD.Modules.MenuRedirect.Editor`（Editor-only） |
| 命名空间 | `MUD.Modules.MenuRedirect` |
| 设置页 | `Project/MUD/Menu Redirect`；菜单入口 `Tools/MUD/Menu Redirect` |
| 规则配置 | `ProjectSettings/MenuRedirect.asset`（`ScriptableSingleton`，项目唯一配置） |
| 生成代码 | 默认 `Assets/MenuRedirects/Editor/MenuRedirects.g.cs`；目录可配置，必须在 `Assets` 下且包含 `Editor` 目录 |
| 语言偏好 | `EditorPrefs`：`MUD.Modules.MenuRedirect.Language.<productGUID>` |

旧配置 `Assets/Settings/MUD/MenuRedirect.asset` 不再读取，也不会自动迁移。

## 3. 架构

```
Editor/Redirect/
  MenuRedirectRule.cs              规则数据（fromPath / toPath / priority / enabled）
  MenuRedirectRuleFile.cs          规则 JSON 导入/导出（格式、版本、条数、路径与重复校验）
  MenuRedirectSettings.cs          ScriptableSingleton 配置与固定路径
  MenuRedirectSettingsProvider.cs  Project Settings 页面：规则列表、详情、工具栏、反馈
  MenuRedirectEngine.cs            评估、包装代码生成、事务、隐藏、旧文件清理
  MenuRedirectCleanup.cs           移除包之前的生成文件清理（保留规则配置）
  MenuRedirectValidator.cs         共享校验：路径、内置保护、冲突、目标可用性
  MenuRegistry.cs                  反射扫描 [MenuItem]，规范化路径并保存 MethodInfo
  MenuRedirectGuard.cs             Unity 内置菜单与受保护容器判断
  MenuRedirectBridge.cs            内部 Menu API 反射与失败降级
  MenuRedirectInvoker.cs           包装方法反射调用原方法
  MenuRedirectLocalization.cs      中英文文案与项目级语言偏好
  MenuRedirectBootstrap.cs         域重载后重放隐藏
```

数据流：`SettingsProvider` 读取 `MenuRedirectSettings`，用 `Validator` + `Registry` 生成共享校验结果；
`Engine` 在“应用”时写入包装代码、保存配置并隐藏原菜单；`Bootstrap` 在域重载后复核包装代码并重放隐藏。
设置页只缓存显示状态，打开页面不落盘。

## 4. 发现与保护

`MenuRegistry.Build()` 遍历 `AppDomain.CurrentDomain.GetAssemblies()` 中所有非动态程序集：

- 反射读取方法与 `UnityEditor.MenuItem` / `MenuItemAttribute` 特性，不直接引用特性类型，
  以兼容不同 Unity 版本。
- 一个方法上的多个 `[MenuItem]` 全部登记；记录路径、程序集、`MethodInfo`、validate、priority。
- 路径规范化会移除菜单助记符下划线，并识别带修饰符或不带修饰符（如 `_g`）的快捷键后缀。
- 跳过自身生成的 `MUD.MenuRedirects` 命名空间，避免重复应用时把上一轮代理当成原菜单。

保护规则：

- Unity 内置顶级菜单（`File` / `Edit` / `Assets` / `GameObject` / `Window` 等）整体不可移除或搬移。
- 容器下存在 Unity 程序集声明的子项时，整个容器按受保护容器处理；仅隐藏/叶子重定向直接阻止，
  分支重定向跳过该子容器、保留受保护条目。
- 第三方菜单（包括 `Tools`）可移除；动态注册菜单只能仅隐藏，不能重定向。
- 归属判定解析程序集位置、`PackageInfo` 和 asmdef/asmref 路径：Unity 安装目录与 `com.unity.*`
  官方包受保护；工程/第三方包即使程序集名为 `Unity.*` 也不受保护；无法解析来源时才回退程序集名前缀。

## 5. 规则模型与校验

规则字段（`MenuRedirectRule`）：

- `fromPath`：原菜单路径；为空、格式非法或命中 Unity 内置/受保护容器时禁止应用。
- `toPath`：新路径；为空表示仅隐藏。新路径不能等于原路径，也不能位于原路径之下。
- `priority`：新菜单在父级中的排序值，只显示数字。
- `enabled`：禁用规则不参与校验、生成或隐藏。

路径语义：

- 完整叶子路径：隐藏该条目；填写新路径时同时生成重定向。
- 分支容器路径（无 `/` 的顶级路径，或自身未注册但有子项的中间容器，如 `NuGet`、`Window/Unity CLI Loop`）：
  留空隐藏整个分支；填写新路径时把分支下所有可执行子项搬到新父路径。
- 自身已注册 `[MenuItem]` 的中间路径按叶子处理，避免把“菜单项 + 子菜单”误当成整棵子树。
- 同路径判断大小写不敏感，避免 Windows 上仅大小写不同的路径互相删除。

共享校验 `MenuRedirectValidator` 同时驱动左侧行状态、右侧详情、工具栏计数和 Apply：

- 错误：路径为空/非法、Unity 内置或受保护容器、原路径重复或重叠（互为祖先/后代）、目标重复/已存在、目标等于或位于源之下、
  重定向源缺失或没有可执行静态 `[MenuItem]`、分支无可用子项、生成目录无效。存在错误时 Apply 禁用。
- 警告：仅隐藏动态注册菜单、分支中跳过受保护/动态子项、仅隐藏路径当前不存在等。警告不阻止 Apply。
- 行状态：`✓` 通过、`!` 警告、`✕` 错误、`—` 禁用；详情只显示消息正文，不重复添加 `!`/`✕` 前缀。
- Apply 前和域重载重放前都会重新校验；外部修改配置也不能绕过非法路径、目标冲突或内置保护检查。

## 6. 菜单重定向与隐藏

核心由 `Editor/Redirect/` 下的类协作：

- 配置存储：`MenuRedirectSettings : ScriptableSingleton<MenuRedirectSettings>`，唯一文件为
  `ProjectSettings/MenuRedirect.asset`；打开设置页不落盘，只有点“应用”才调用 `Save(true)`。
  配置保存可编辑的 `generatedCodeFolder`（默认 `Assets/MenuRedirects/Editor`）以及隐藏的
  `lastGeneratedCodePath`，后者只用于切换目录后定位并清理旧包装文件；旧配置缺少字段时回退默认值，
  不做配置迁移。
- 配置入口：`MenuRedirectSettingsProvider` 注册 `Project/MUD/Menu Redirect` 页面；
  `Tools/MUD/Menu Redirect` 打开同一页面。`MenuRedirectBootstrap` 仅在磁盘配置存在时重放隐藏。
- 设置页采用左右分栏：左侧用 `ReorderableList` 提供标准行高的紧凑规则行、拖拽排序和状态色；
  行内按 `原路径 → 新路径`（分支路径显示 `/*`，仅隐藏为 `→ 隐藏`）显示预览，优先级只显示数字，
  数字与右侧 `✕` 保留间距；最左侧用 `✓ / ! / ✕ / —` 表示通过 / 警告 / 错误 / 禁用，
  路径文字同步使用绿 / 黄 / 红 / 灰状态色；每行右侧提供居中的 `✕` 移除按钮。
  启用/禁用只在右侧详情中操作。顶部工具栏用纯文本显示“已启用 / 验证通过 / 警告 / 错误”计数，
  “未保存”和“待重编译”仅显示文字，不使用背景色胶囊；原有 `HelpBox` 反馈提示移到左右分栏下方，
  提示出现或消失只改变分栏底部高度，不会把顶部列表和详情整体推下去。
  左右两栏之间由一条 6px 的分隔条隔开，可左右拖动调整左栏宽度：默认约为可用宽度的 32%
  （限制在 300～340），左栏最小 220、右栏最小 240；分隔条通过 `GUILayoutUtility.GetRect`
  参与真实布局，拖动由 `GUIUtility.hotControl` 捕获，窗口变窄时会自动夹紧宽度以避免两栏重叠。
  分栏实际宽度只在 `Repaint` 阶段用 `GUILayoutUtility.GetLastRect()` 记录；`Layout` 等阶段
  该 API 可能返回 `1×1` 临时值，必须过滤掉，否则拖动会被错误地夹回最小左栏宽度。
  拖动中的 `MouseDrag` / `MouseUp` 事件走快速路径：不重建整页，只更新宽度并在变化时请求
  `Repaint`；随后的 `Layout` / `Repaint` 仍按新宽度完整排版，保持完全实时变宽。
  宽度不写入配置、不持久化，离开页面或域重载后恢复默认。
  标题栏右侧动作组中，“规则说明 / Help”位于最左侧，语言标签使用右对齐样式并紧贴语言下拉框；
  语言选择为“跟随系统 / 中文 / English”，默认跟随系统；语言偏好按项目 productGUID
  保存在本机 `EditorPrefs`（`MUD.Modules.MenuRedirect.Language.<productGUID>`），不进入规则配置。
  按钮宽度按当前语言动态测量，避免英文 `Open Folder` 等文字被截断。
  切换语言只重建文案与校验缓存并重绘，不改变未保存状态，
  也不会改变生成代码内容；生成代码注释固定为中文，避免切换语言触发重编译。
  配置文件行下方增加“生成代码”目录行：可文本编辑、选择目录、打开目录或恢复默认；
  目录必须位于 `Assets` 下且路径中包含 `Editor` 目录，文件名固定为 `MenuRedirects.g.cs`。
  目标文件已存在但不是本工具生成（缺少生成头）时拒绝覆盖；旧位置的包装文件仅在带有生成头时删除。
  编辑目录会标记未保存；“还原”同时恢复规则和生成目录的已保存值；“应用”才保存配置并移动包装文件。
  右侧详情顶部用“状态符号 + 路径”单行展示，下方分为基本设置、
  路径映射和验证结果，并逐条显示完整警告/错误（不再重复添加 `!`/`✕` 前缀）；
  右上角 `✕` 删除当前规则。
  原路径右侧提供“选择…”：打开可搜索的树形菜单选择器，按路径层级展示注册表路径、所有中间容器及其子项
  （如 `Window/Unity CLI Loop`）；单击整行展开/折叠并显示悬停（浅蓝），双击/Enter 选中焦点条目；深蓝标记规则当前已设置的值，单击不移动焦点（折叠时保持滚动位置、不自动滚动定位），受保护条目也可悬停，默认全部展开，搜索为不区分大小写的子串过滤
  并自动展开命中项及其祖先；受保护条目显示 `✕` 且不可选（仍可单击折叠/展开），支持 `↑/↓`、`Enter`、`Esc` 和刷新注册表。
  `MenuRedirectPickerTree` 只负责把条目组织成树、按折叠状态和搜索词生成可见行，选中语义不变。
  新路径右侧提供“父级…”：复用同一树形窗口的父级模式，只列出顶级路径和有子项的容器，
  受保护条目仍显示但不可选。选中后由 `MenuRedirectPickerData.BuildTargetPath` 统一拼接：
  分支源直接使用父级；叶子源保留当前新路径的最后一段，为空时沿用原路径的最后一段；
  原路径为空时只填入父级，并在反馈区提示补全新菜单名称。
  两种选择都只更新内存中的路径并重新校验，仍需“应用”才保存配置或生成代码；动态注册菜单仍需手动输入。
  工具栏提供“导入… / 导出…”：文件格式为 `MUD.MenuRedirect.Rules` v1，只包含规则条数和规则数组
  （原路径、新路径、优先级、启用），不包含生成目录、语言等本机设置。
  导入先完整解析并校验格式、版本、条数、路径和重复原路径，全部通过后才弹出替换确认；
  取消或任一校验失败都不修改内存。导入只替换内存规则并标记未保存，仍需“应用”才写配置或生成代码。
  规则校验与“待重编译”状态按规则变更缓存，避免 `OnGUI` 点击/拖拽时重复构建菜单注册表和读取包装文件。
  每帧绘制同样只读缓存：规则行的状态、路径预览和优先级 tooltip 复用 `RuleRowDisplay`，
  工具栏统计、按钮文案和校验面板消息按语言/规则缓存，按钮与标签宽度按语言缓存，
  在语言或皮肤切换时统一失效；空规则清理、反馈区和计数标签也不再每帧分配字符串。
  这些改动只涉及缓存与分配，不改变任何界面行为。
- 共享验证 `MenuRedirectValidator`：左侧状态、右侧详情和 Apply 使用同一结果。
  目标菜单已存在、重定向源缺失或没有可执行静态 `[MenuItem]`、分支无可用子项、规则原路径重叠、
  路径格式非法、Unity 内置菜单/受保护容器等为错误并禁用“应用”；仅隐藏动态注册菜单、
  分支中跳过受保护/无静态方法子项等为警告。旧配置在应用前也会重新验证，不能绕过 UI 拦截。
  域重载重放时还会再次拒绝非法路径、目标等于源或位于源之下、目标已存在/属于内置菜单的规则，
  避免配置被外部修改后隐藏原菜单却没有可用的新入口。
- 不迁移旧 `Assets/Settings/MUD/MenuRedirect.asset`。

1. `MenuRegistry.Build()` 扫描已加载程序集里的 `[MenuItem]`，记录路径、程序集、`MethodInfo`、validate、priority；
   一个方法上的多个 `[MenuItem]` 全部登记；路径规范化会移除菜单助记符下划线，
   并正确识别带修饰符或不带修饰符（如 `_g`）的快捷键后缀。排除自身生成的 `MUD.MenuRedirects` 命名空间，
   避免重复应用时把上一轮代理当成原菜单。
2. `MenuRedirectEngine.Evaluate()` 评估规则：父级路径（无 `/`）遍历注册表子项；叶子路径只处理自身。
   Unity 内置顶级菜单和 Unity 程序集声明的条目会被拦截/跳过；若容器下存在 Unity 程序集子项，
   整个容器按受保护容器处理（仅隐藏/叶子重定向直接阻止，分支重定向跳过该子容器）。
   动态注册（找不到静态方法）的条目不能生成包装，重定向会验证失败，仅隐藏则允许并给警告。
3. `MenuRedirectEngine.PrepareApply()` 写入新的 `MenuRedirects.g.cs`，按需递归创建 `Assets` 子目录；
   Provider 保存配置成功后调用事务 `Commit()` 删除旧包装文件并刷新，保存失败调用 `Rollback()` 恢复文件。
   旧文件不是本工具生成、目标位置被非生成文件占用、项目里存在其它残留包装文件时中止应用。
   同路径判断大小写不敏感，避免 Windows 上仅大小写不同的路径互相删除。
   快捷键不会被包装复制：生成时若原菜单带快捷键（如 <c>_g</c>、<c>#%F12</c>），
   会在对应包装方法前写入固定中文注释提示手动添加，并记录数量与快捷键；
   Apply 成功后反馈区在主消息后追加本地化提示（条数与快捷键列表）。
   提示只在生成阶段出现，不参与校验，也不改变快捷键的任何行为。
4. 包装代码有变化时不在当前会话立即隐藏；等 Unity 编译和域重载后由 Bootstrap 重放。
   包装代码无变化时 Apply 才直接隐藏。
5. `MenuRedirectInvoker` 由包装方法调用，按原路径找到 `MethodInfo` 并反射执行，支持无参或单个 `MenuCommand` 参数；
   原条目有 validate 时，包装的 validate 一并转调。

隐藏机制：

- 直接调用 `UnityEditor.Menu.RemoveMenuItem(path)`：父级路径一次移除整棵子树；叶子路径移除自身。**不再调用** `Menu.RebuildAllMenus()`，因为它会按 `[MenuItem]` 特性重新注册所有条目，把刚移除的菜单原样恢复。
- 移除后调用 `MenuItemExists(path)` 复核；失败、仍存在或被其它插件重注册时写入状态栏告警，不阻断其它规则。
- 刷新只调用 `EditorUtility.Internal_UpdateAllMenus()`。
- 若分支下有 Unity 内置、受保护容器或动态注册子项被跳过，则逐个移除允许的条目并保留父级容器，
  避免误删受保护菜单；被跳过容器的后代也不会被单独移除。

时序与恢复：

- 域重载后 `MenuRedirectBootstrap` 在 `delayCall` 重放隐藏。重放前 `IsGeneratedCodeReady()`
  校验包装代码存在且内容与当前规则一致；缺失、不是本工具生成或等待编译时暂缓隐藏并记录警告。
  这样编译失败不会出现原菜单已移除而新菜单尚未注册的情况。
- 规则禁用/删除后，包装代码在下次应用时清空；已移除的原菜单在下一次域重载由 Unity 重新注册恢复。
  关闭规则不会立即恢复本会话菜单，需一次域重载（或重启编辑器）。
- 重复应用是幂等的：原条目已经不存在时视为已达成目标，不产生“未找到”告警。

失败降级：`MenuRedirectBridge` 任一反射步骤失败 → `Available=false` 并记录原因，隐藏功能整体失效但配置/生成/编译不受影响；
`MenuRedirectSettingsProvider` 状态栏可见状态与错误。若配置保存失败，文件事务回滚；若旧包装文件删除失败，
会删除本次写入的新文件并把路径指回旧文件后再次保存，避免两个包装类并存；错误会显示在状态栏，且不会执行隐藏。

## 6.5. 性能与防抖

设置页的高频操作（路径字段逐字符编辑、拖动排序、滚动）会反复触发完整校验和列表刷新，
因此做了两层去重：

- **校验防抖**：`MarkDirty` 只标记 `_validationsDirty = true` 并设定 0.15 秒的到期时间，
  由 `EditorApplication.update` 上的 `FlushDeferredValidation` 统一执行 `BuildRuleValidations`。
  结构操作（新增/删除/导入/还原/拖动排序）仍立即校验，保证即时反馈。
- **可见列表缓存**：`_visibleRules` 只在筛选条件变化或规则列表变化时重建，
  `ReorderableList` 绑定的是可见列表而不是全部规则；计数 `visible/total` 也走缓存。
- **拖动快速路径**：分隔条拖动期间的 `MouseDrag` / `MouseUp` 事件只更新宽度，不重建不可见的
  整页布局；宽度变化时请求 `Repaint`，由随后的 `Layout` / `Repaint` 按新宽度完整排版。
  生成代码路径和界面语言解析结果/下拉选项按目录/语言缓存，工具栏计数在验证结果变化时一次性统计，
  标签颜色样式按 (基础样式, 颜色) 缓存，“上次隐藏错误”文本按消息版本和语言缓存；
  生成代码文件时间戳最多每秒检查一次。拖动期间推迟校验和草稿写盘，松开鼠标后继续执行，
  避免同步校验或 IO 打断拖动。拖动时每个可见帧仍会按新宽度完整排版/重绘两栏，
  这是“完全实时变宽”的固有成本，不使用预览线或降低刷新频率来规避。

域重载时的重复评估：

- 旧流程中 `Bootstrap` 先调用 `IsGeneratedCodeReady()`（内部 `HasPendingCodeChanges` →
  `BuildGeneratedContent` → `Evaluate`），再调用 `HideOriginals()`（内部又 `IsGeneratedCodeReady` +
  `Evaluate`），一次域重载最多评估 3 次。
- 新流程：`MenuRedirectEngine.TryHideOriginals(settings, out reason)` 一次 `Evaluate` 后，
  复用同一份 `List<RuleEvaluation>` 同时完成包装代码就绪复核与隐藏重放。
  `BuildGeneratedContent` / `HasPendingCodeChanges` / `IsGeneratedCodeReady` 均支持传入已评估结果，
  避免重复构建生成代码。

草稿：

- 草稿文件位于 `Library/MenuRedirectDraft.json`，不进版本库。
- 内容同时保存“已保存基线”（规则 JSON + 生成目录）和“草稿”（规则 JSON + 生成目录），
  基线用静态字段跨 provider 实例保存，避免重开页面时把内存中的未保存规则误当已保存。
- 恢复时先比较草稿基线与当前配置是否一致；不一致说明配置已在其它会话中被应用或回退，旧草稿失效。
- 草稿不校验路径合法性，崩溃前输入到一半的内容也能恢复。
- 写入带 0.75 秒防抖；离开页面、`beforeAssemblyReload`、`quitting` 时同步落盘；
  应用或还原后删除草稿。

## 7. 自动化触发

| 触发 | 行为 |
|---|---|
| 编辑器加载 / 域重载（`[InitializeOnLoad]`） | `MenuRedirectBootstrap` 在 `delayCall` 检查磁盘配置；存在时先 `IsGeneratedCodeReady()` 复核包装代码，再调用 `HideOriginals()` 重放隐藏 |
| 包装代码缺失 / 与当前规则不一致 / 编译未完成（存在重定向规则时） | 暂缓隐藏并记录警告；避免原菜单已移除而新入口尚未注册 |
| 点击“应用”且包装代码无变化 | 保存配置后立即隐藏原菜单 |
| 点击“应用”且包装代码有变化 | 先写入包装代码；隐藏延后到 Unity 编译和域重载完成，由 Bootstrap 重放 |
| 点击“导入…” | 读取并完整校验 JSON；通过后确认替换内存规则，取消或失败不修改内存，不写配置、不生成代码 |
| 点击“导出…” | 只读取当前内存规则写为 JSON，不修改配置、不生成代码 |
| 打开设置页 | 只读取配置和构建显示缓存；首次校验/评估时按需构建 `MenuRegistry`，不自动扫描、不落盘 |
| 点击“原路径 > 选择…” | 复用缓存的 `MenuRegistry` 构建选择项；搜索只过滤内存列表，受保护项不可选；选中后标记未保存并重新校验，不写配置、不生成代码 |
| 点击 `Tools/MUD/Menu Redirect/Clean Up Generated Files` | 读取当前配置路径和上次生成路径；确认后只删除文件名正确且首行带生成头的包装代码，保留 `ProjectSettings/MenuRedirect.asset`；删除后由下一次域重载恢复原菜单 |
| 点击应用（准备阶段）/ 程序集重载 | 注册表缓存失效，下次校验或隐藏时重新扫描已加载程序集 |

## 8. 风险与对策

| 风险 | 对策 |
|---|---|
| 内部 `Menu` API 跨版本漂移 | 反射 + 探测 + 降级，收口在 `MenuRedirectBridge` 单类；`Available=false` 时只停用隐藏，配置与生成不受影响 |
| 隐藏时序（菜单未构建 / 重载后复活） | 不调用 `Menu.RebuildAllMenus()`；直接 `RemoveMenuItem` + `Internal_UpdateAllMenus()`；域重载 `delayCall` 重放并用 `MenuItemExists()` 复核 |
| 包装代码编译失败或与规则不一致 | `IsGeneratedCodeReady()` 校验文件存在、生成头和当前规则匹配；不满足时暂缓隐藏并告警，避免新旧入口同时消失 |
| 配置保存失败 / 旧包装文件清理失败 | 包装代码写入、配置保存、旧文件删除走事务；失败回滚文件并显示错误，不执行隐藏 |
| 误删 Unity 内置菜单或受保护容器 | `MenuRedirectGuard` 保护内置顶级菜单；容器含 Unity 子项时阻止叶子/仅隐藏重定向，分支重定向跳过该子容器 |
| 重复应用把上一轮代理当成原菜单 | 跳过 `MUD.MenuRedirects` 生成命名空间；只覆盖/删除首行带本工具生成头的文件 |
| asmdef/asmref 混合归属 | 程序集归属以 `PackageInfo`、安装/包缓存路径和 asmdef/asmref 路径为准；asmref 包含的脚本跟随目标程序集，未知来源回退程序集名规则，宁可多保护 |
| 全程序集扫描耗时 | `MenuRegistry` 按需构建并缓存，应用准备阶段失效；选择器复用缓存，仅在点击“刷新”时重新扫描；设置页每帧绘制已做缓存与分配优化，注册表扫描本身未做进一步剖析，大工程以实测为准 |
| 动态注册菜单无法生成包装 | 仅允许隐藏并给警告；重定向在共享校验阶段直接报错，不进入生成流程 |
| 直接移除包后残留生成文件/配置 | 包代码已不存在，无法自动清理；提供移除前的清理菜单，README 记录移除后的手动删除步骤 |

## 9. 后续演进（未实现）

- `MenuRegistry` 性能剖析与按需过滤；仅在实测出现明显卡顿时处理。
