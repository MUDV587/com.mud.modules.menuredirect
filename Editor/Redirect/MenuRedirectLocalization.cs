using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>语言选择模式。Auto 会按 Unity 检测到的系统语言解析为中文或英文。</summary>
    internal enum MenuRedirectLanguage
    {
        Auto = 0,
        Chinese = 1,
        English = 2
    }

    /// <summary>
    /// 一条尚未解析成字符串的本地化消息。用于让引擎告警和应用反馈在切换语言后
    /// 仍能按当前语言重新显示，而不是保留创建时的语言。
    /// </summary>
    internal sealed class MenuRedirectText
    {
        internal string Key { get; }
        internal object[] Args { get; }

        internal MenuRedirectText(string key, params object[] args)
        {
            Key = key;
            Args = args ?? Array.Empty<object>();
        }

        internal string Resolve()
        {
            return MenuRedirectLocalization.Format(
                MenuRedirectLocalization.CurrentLanguage,
                Key,
                Args);
        }

        internal string Resolve(MenuRedirectLanguage language)
        {
            return MenuRedirectLocalization.Format(language, Key, Args);
        }

        public override string ToString()
        {
            return Resolve();
        }
    }

    /// <summary>
    /// Menu Redirect 编辑器页面的中英文文案与语言偏好。
    ///
    /// 语言偏好保存在 EditorPrefs，并按项目 productGUID 区分：
    /// 每个项目独立、本机有效，不写入 ProjectSettings/MenuRedirect.asset，
    /// 因此切换语言不会让规则变成“未保存”，也不会触发包装代码重编译。
    /// </summary>
    internal static class MenuRedirectLocalization
    {
        private const string PreferencePrefix = "MUD.Modules.MenuRedirect.Language.";

        private static readonly Dictionary<string, string> Chinese =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ui.language.label", "语言" },
                { "ui.language.auto", "跟随系统" },
                { "ui.language.chinese", "中文" },
                { "ui.language.english", "English" },
                { "ui.language.tooltip", "界面语言；默认跟随系统语言。" },
                { "ui.loading", "正在加载配置…" },
                {
                    "ui.header.description",
                    "把已有菜单项搬到新路径，或直接隐藏。Unity 内置菜单受保护。"
                },
                {
                    "ui.help.text",
                    "• 完整叶子路径：隐藏该条目；填写新路径则同时完成重定向。\n" +
                    "• 分支容器路径（无 / 的顶级路径，或未直接注册但有子项的中间容器，如 NuGet、Window/Unity CLI Loop）：\n" +
                    "    - 新路径留空：隐藏整个分支。\n" +
                    "    - 填写新路径：把分支下的所有可执行子项搬到新父路径。\n" +
                    "    - 自身已注册 [MenuItem] 的中间路径按叶子处理。\n" +
                    "• Unity 内置菜单受保护；第三方菜单（包括 Tools）可移除。\n" +
                    "• 新路径不能位于被移除的菜单之下。"
                },
                { "ui.header.help.show", "规则说明" },
                { "ui.header.help.hide", "收起" },
                { "ui.config.label", "配置文件" },
                { "ui.config.copy", "复制" },
                { "ui.config.open", "打开目录" },
                { "ui.generated.label", "生成代码" },
                { "ui.generated.browse", "选择…" },
                { "ui.generated.browse.tooltip", "选择包装代码的输出目录。" },
                { "ui.generated.browse.title", "选择生成代码目录" },
                { "ui.generated.open", "打开" },
                { "ui.generated.open.tooltip", "打开生成代码目录。" },
                { "ui.generated.default", "默认" },
                { "ui.generated.default.tooltip", "恢复默认生成目录。" },
                {
                    "ui.generated.tooltip",
                    "包装代码输出目录；实际文件为 {0}。目录必须位于 Assets 下并包含 Editor 目录。"
                },
                { "ui.toolbar.enabled", "已启用 {0} 条" },
                { "ui.toolbar.passed", "验证通过 {0} 条" },
                { "ui.toolbar.warnings", "警告 {0} 条" },
                { "ui.toolbar.errors", "错误 {0} 条" },
                { "ui.feedback.more", "… 还有 {0} 条，详见 Console" },
                { "ui.toolbar.unsaved", "未保存" },
                { "ui.toolbar.pending", "待重编译" },
                { "ui.toolbar.revert", "还原" },
                {
                    "ui.toolbar.revert.tooltip",
                    "放弃未保存的修改，恢复到上次保存或应用时的规则。"
                },
                { "ui.toolbar.apply", "应用" },
                { "ui.toolbar.apply.tooltip", "保存配置并应用菜单重定向。" },
                {
                    "ui.toolbar.apply.errorTooltip",
                    "请先修复验证错误；有错误时不会保存或应用。"
                },
                { "ui.toolbar.import", "导入…" },
                {
                    "ui.toolbar.import.tooltip",
                    "从 JSON 文件导入规则并替换当前规则；仍需“应用”才保存。"
                },
                { "ui.toolbar.export", "导出…" },
                {
                    "ui.toolbar.export.tooltip",
                    "把当前规则导出为 JSON；不包含生成目录等本机配置。"
                },
                { "ui.import.title", "导入菜单重定向规则" },
                { "ui.import.confirm.title", "替换当前规则？" },
                {
                    "ui.import.confirm.message",
                    "导入将替换当前 {0} 条规则（文件含 {1} 条）。未保存的修改会丢失，是否继续？"
                },
                { "ui.import.confirm.ok", "替换" },
                { "ui.import.confirm.cancel", "取消" },
                { "ui.import.success", "已导入 {0} 条规则；点击“应用”保存并生效。" },
                { "ui.import.error.read", "读取文件失败：{0}" },
                { "ui.import.error.empty", "规则文件为空。" },
                { "ui.import.error.json", "JSON 格式无效：{0}" },
                { "ui.import.error.format", "不是 Menu Redirect 规则文件。" },
                { "ui.import.error.version", "不支持的规则文件版本：{0}。" },
                { "ui.import.error.rules", "规则列表缺失或格式无效。" },
                { "ui.import.error.rule", "第 {0} 条规则无效。" },
                { "ui.import.error.source", "第 {0} 条规则的原路径无效：{1}" },
                { "ui.import.error.target", "第 {0} 条规则的新路径无效：{1}" },
                { "ui.import.error.samePath", "第 {0} 条规则的新路径不能等于原路径。" },
                { "ui.import.error.duplicate", "第 {0} 条规则与原路径重复：{1}" },
                { "ui.export.title", "导出菜单重定向规则" },
                { "ui.export.success", "已导出 {0} 条规则到 {1}。" },
                { "ui.export.error", "导出失败：{0}" },
                { "ui.list.title", "规则" },
                { "ui.list.add.tooltip", "添加规则" },
                { "ui.list.hint.empty", "点击 ＋ 添加第一条规则" },
                { "ui.list.hint.reorder", "拖动左侧把手调整顺序" },
                { "ui.list.hint.filtered", "显示 {0} / {1} 条规则；筛选时不能拖动排序" },
                { "ui.list.filter.issues.tooltip", "只显示有警告或错误的规则" },
                { "ui.list.filter.clear.tooltip", "清除搜索和筛选" },
                { "ui.list.filter.clear", "清除筛选" },
                { "ui.list.empty.filtered.title", "没有匹配的规则" },
                { "ui.list.empty.filtered.hint", "调整搜索词，或清除筛选查看全部规则。" },
                { "ui.list.empty.title", "还没有规则" },
                { "ui.list.empty.hint", "添加一条规则，把菜单搬到新位置或直接隐藏。" },
                { "ui.list.empty.add", "＋ 添加规则" },
                { "ui.detail.title", "规则详情" },
                { "ui.detail.delete.tooltip", "删除当前规则" },
                { "ui.detail.empty.title", "选择一条规则" },
                { "ui.detail.empty.hint", "在左侧点击规则，查看和编辑详细配置。" },
                { "ui.detail.section.basic", "基本设置" },
                { "ui.detail.enabled", "启用" },
                { "ui.detail.priority", "优先级" },
                { "ui.detail.priority.hint", "数值越小，新菜单在父级中越靠前。" },
                { "ui.detail.section.paths", "路径映射" },
                { "ui.detail.from", "原路径" },
                { "ui.detail.from.pick", "选择…" },
                {
                    "ui.detail.from.pick.tooltip",
                    "从已注册的菜单中选择原路径。"
                },
                { "ui.detail.to", "新路径" },
                { "ui.detail.to.pick", "父级…" },
                {
                    "ui.detail.to.pick.tooltip",
                    "选择新路径的父级容器。"
                },
                {
                    "ui.detail.to.parentOnly",
                    "已填入父级路径；原路径为空，请补全新菜单名称。"
                },
                { "ui.detail.paths.hint", "新路径留空时只隐藏原菜单，不生成新菜单项。" },
                { "ui.picker.title", "选择菜单路径" },
                { "ui.picker.title.parent", "选择目标父级" },
                { "ui.picker.search", "搜索" },
                { "ui.picker.refresh", "刷新" },
                { "ui.picker.refresh.tooltip", "重新扫描当前已加载的程序集。" },
                { "ui.picker.count", "显示 {0} / {1} 条" },
                { "ui.picker.empty", "没有匹配的菜单路径。" },
                { "ui.picker.empty.parent", "没有匹配的父级路径。" },
                {
                    "ui.picker.legend",
                    "✓ 可执行   ! 仅隐藏/含受保护子项   ✕ 受保护"
                },
                {
                    "ui.picker.legend.parent",
                    "✓ 可作为父级   ! 含受保护子项   ✕ 受保护"
                },
                {
                    "ui.picker.hint",
                    "单击行展开/折叠并显示悬停（浅蓝），双击/Enter 确认焦点条目；深蓝为规则当前已设置的值，单击不移动焦点；受保护条目也可悬停。"
                },
                {
                    "ui.picker.hint.parent",
                    "单击行展开/折叠并显示悬停（浅蓝），双击/Enter 确认焦点条目；深蓝为当前父级值，单击不移动焦点；受保护条目也可悬停。"
                },
                { "ui.picker.tooltip.executable", "可执行菜单：可重定向或隐藏。" },
                {
                    "ui.picker.tooltip.hideOnly",
                    "没有可执行的静态 [MenuItem] 方法：仅隐藏可用。"
                },
                {
                    "ui.picker.tooltip.branchExecutable",
                    "分支容器：包含可重定向的菜单。"
                },
                {
                    "ui.picker.tooltip.branchHideOnly",
                    "分支容器：没有可重定向的可执行子项。"
                },
                { "ui.detail.section.validation", "验证结果" },
                { "ui.detail.apply.hint", "修改后点“应用”才会保存并生效。" },
                { "ui.detail.validation.disabled", "已禁用，不参与验证。" },
                { "ui.detail.validation.passed", "✓ 验证通过" },
                { "ui.detail.validation.errors", "发现 {0} 个错误" },
                { "ui.detail.validation.warnings", "发现 {0} 个警告" },
                { "ui.row.hide", "隐藏" },
                { "ui.row.emptySource", "（未填写原路径）" },
                {
                    "ui.row.emptySource.tooltip",
                    "填写原菜单路径后，这里会显示菜单预览。"
                },
                { "ui.row.hideBranch.tooltip", "隐藏 {0} 及其所有子菜单。" },
                { "ui.row.hideLeaf.tooltip", "隐藏菜单 {0}。" },
                { "ui.row.paths.tooltip", "原路径：{0}\n新路径：{1}" },
                { "ui.row.priority.tooltip", "优先级：{0}" },
                { "ui.row.remove.tooltip", "移除规则" },
                { "ui.row.status.disabled", "已禁用，不参与验证。" },
                { "ui.row.status.passed", "验证通过。" },
                { "ui.row.status.errors", "有 {0} 个错误" },
                { "ui.row.status.warnings", "有 {0} 个警告" },
                { "ui.row.status.details", "：\n" },

                { "validation.source.empty", "原路径为空" },
                { "validation.source.invalid", "原路径格式无效: {0}" },
                { "validation.target.invalid", "新路径格式无效: {0}" },
                { "validation.source.builtIn", "Unity 内置菜单，不能移除或搬移" },
                {
                    "validation.source.protectedContainer",
                    "该容器包含 Unity 内置菜单，不能整体移除"
                },
                { "validation.source.missing", "当前菜单注册表中找不到原路径: {0}" },
                {
                    "validation.source.notExecutable",
                    "原路径没有可执行的静态 [MenuItem] 方法，无法重定向: {0}"
                },
                {
                    "validation.source.duplicateExecutable",
                    "原路径存在多个可执行 [MenuItem] 声明: {0}"
                },
                {
                    "validation.source.missingHideOnly",
                    "注册表中找不到该菜单；仍可隐藏动态注册的菜单: {0}"
                },
                {
                    "validation.source.notExecutableHideOnly",
                    "该菜单没有可执行的静态方法；仅隐藏仍可生效: {0}"
                },
                {
                    "validation.source.noExecutableChildren",
                    "分支下没有可重定向的可执行子菜单: {0}"
                },
                {
                    "validation.source.protectedChildren",
                    "已跳过 {0} 个包含 Unity 内置菜单的受保护子项: {1}"
                },
                {
                    "validation.source.nonExecutableChildren",
                    "已跳过 {0} 个没有静态方法的子项: {1}"
                },
                {
                    "validation.source.overlap",
                    "原路径「{0}」与其他规则的原路径「{1}」重叠，隐藏时可能误删"
                },
                { "validation.target.builtIn", "目标与 Unity 内置菜单冲突" },
                { "validation.same", "原路径与新路径相同" },
                { "validation.targetUnderSource", "新路径位于原路径下" },
                { "validation.target.exists", "目标菜单已存在: {0}" },
                { "validation.source.duplicate", "原路径重复" },
                { "validation.target.duplicate", "新路径重复" },
                {
                    "validation.target.duplicateResolved",
                    "生成的新菜单路径与其他规则重复: {0}"
                },
                { "validation.target.hiddenByOther", "目标会被其他规则隐藏" },

                { "apply.reverted", "已还原未保存的修改。" },
                { "ui.draft.restored", "已恢复上次未保存的修改，点“应用”后才会写入配置。" },
                {
                    "apply.error.validation",
                    "规则验证未通过，请先修复错误。"
                },
                {
                    "apply.error.codegenFailed",
                    "生成包装代码失败，配置未保存。"
                },
                {
                    "apply.error.emptySource",
                    "有启用的规则缺少原路径（fromPath），或将其禁用。"
                },
                {
                    "apply.error.duplicateSource",
                    "多条规则的原路径重复: {0}，请合并或禁用其中一条。"
                },
                { "apply.error.same", "原路径和新路径相同，无法重定向: {0}" },
                {
                    "apply.error.duplicateTarget",
                    "多条规则的新路径重复: {0}，Unity 不允许同名菜单。"
                },
                {
                    "apply.error.targetUnderSource",
                    "新路径「{0}」位于原路径「{1}」之下，原路径被移除后目标也会消失。"
                },
                {
                    "apply.error.targetUnderOther",
                    "规则冲突：新路径「{0}」位于另一条规则要移除的菜单「{1}」之下，重定向后仍会被隐藏。"
                },
                { "apply.error.saveFailed", "保存配置失败: {0}" },
                {
                    "apply.error.generatedFolder.outsideAssets",
                    "生成代码目录必须位于 Assets 下。"
                },
                { "apply.error.generatedFolder.invalid", "生成代码目录无效: {0}" },
                {
                    "apply.error.generatedFolder.notEditor",
                    "生成代码目录必须包含 Editor 目录，否则包装代码无法编译。"
                },
                {
                    "apply.error.generatedFolder.folderIsFile",
                    "生成代码目录路径已被文件占用: {0}"
                },
                {
                    "apply.error.generatedFolder.targetIsFolder",
                    "生成代码文件路径已被文件夹占用: {0}"
                },
                {
                    "apply.error.generatedFolder.fileExists",
                    "目标位置已存在非本工具生成的文件: {0}，请选择其他目录。"
                },
                {
                    "apply.error.generatedStateSaveFailed",
                    "配置已保存，但生成代码状态保存失败: {0}"
                },
                {
                    "apply.saved.codegen.hidden",
                    "配置已保存；包装代码已写入 {0}；已隐藏 {1} 个原菜单条目。编译完成后新菜单出现。"
                },
                {
                    "apply.saved.codegen.noneHidden",
                    "配置已保存；包装代码已写入 {0}。编译完成后新菜单出现。"
                },
                {
                    "apply.saved.codegen.deferred",
                    "配置已保存；包装代码已写入 {0}。为防止编译失败时菜单丢失，将在编译完成后隐藏原菜单。"
                },
                {
                    "apply.saved.hidden",
                    "配置已保存；已隐藏 {0} 个原菜单条目。"
                },
                { "apply.saved.withWarnings", "配置已保存，应用完成，但有警告，见下方。" },
                { "apply.saved.done", "配置已保存并应用完成。" },
                { "apply.feedback.hotkey", "{0}\n{1}" },
                {
                    "apply.hotkey.hint",
                    "快捷键提示：{0} 条原菜单带快捷键（{1}），包装菜单不会自动复制；" +
                    "请在生成文件中手动为目标菜单添加快捷键。"
                },

                { "guard.root", "「{0}」是 Unity 内置顶级菜单，不能整体移除或搬移" },
                {
                    "guard.item",
                    "「{0}」是 Unity 内置菜单项（由 {1} 注册），不能移除或搬移"
                },
                {
                    "guard.container",
                    "「{0}」包含 Unity 内置子菜单「{1}」（由 {2} 注册），不能整体移除或搬移"
                },
                { "engine.targetBuiltIn", "新路径「{0}」与 Unity 内置菜单冲突" },
                { "engine.targetExists", "新路径「{0}」已存在，跳过该条目的包装生成" },
                { "engine.targetSameSource", "新路径「{0}」与原路径相同，拒绝执行以免隐藏新菜单" },
                { "engine.targetUnderSource", "新路径「{0}」位于原路径「{1}」之下，隐藏原路径会同时移除新菜单" },
                { "engine.invalidPath", "菜单路径格式非法，拒绝执行：{0}" },
                {
                    "engine.noExecutable",
                    "找不到可执行的原菜单方法（可能是动态注册菜单）"
                },
                {
                    "engine.noExecutableLeaf",
                    "找不到可执行的原菜单方法（可能是动态注册菜单），无法重定向"
                },
                {
                    "engine.noMovableChildren",
                    "没有找到可搬移的子菜单（可能是动态注册菜单）"
                },
                { "engine.pathOnly", "{0}" },
                { "engine.originalPath", "{0}（原始路径: {1}）" },
                { "engine.cannotHide", "无法隐藏: {0} —— 内部 Menu API 调用失败" },
                {
                    "engine.stillExists",
                    "移除后条目仍然存在（可能被其它插件重新注册）: {0}"
                },
                {
                    "engine.apiUnavailable",
                    "内部 Menu API 不可用，无法隐藏原菜单: {0}"
                },
                { "engine.skipped", "已跳过: {0} —— {1}" },
                { "engine.noRemovable", "未找到可移除的菜单条目: {0}" },
                { "engine.hideFailed", "未能隐藏: {0}" },
                { "engine.summary", "[MenuRedirect] 隐藏完成: 已隐藏={0}, 跳过={1}" },
                {
                    "engine.codegenUpdated",
                    "[MenuRedirect] 包装代码已更新，等待 Unity 编译后新菜单生效。"
                },
                { "engine.writeFailed", "[MenuRedirect] 写入包装代码失败: {0}" },
                { "engine.rollbackFailed", "[MenuRedirect] 回滚包装代码失败: {0}" },
                {
                    "engine.oldGeneratedDeleteFailed",
                    "[MenuRedirect] 旧包装代码删除失败: {0}"
                },
                {
                    "engine.oldGeneratedKept",
                    "[MenuRedirect] 旧文件不是本工具生成的包装代码，未删除: {0}"
                },
                {
                    "engine.oldGeneratedNotGenerated",
                    "旧包装代码位置存在非本工具生成的文件，已中止应用: {0}"
                },
                {
                    "engine.conflictingGenerated",
                    "项目中存在其他残留包装文件，已中止应用以避免重复类: {0}"
                },
                {
                    "engine.generatedMissing",
                    "包装代码尚未生成，暂缓隐藏原菜单: {0}"
                },
                {
                    "engine.generatedNotGenerated",
                    "包装代码位置的文件不是本工具生成的，暂缓隐藏原菜单: {0}"
                },
                {
                    "engine.generatedStale",
                    "包装代码与当前规则不一致（等待编译），暂缓隐藏原菜单: {0}"
                },
                { "engine.hideDeferred", "[MenuRedirect] 暂缓隐藏原菜单: {0}" },

                { "cleanup.nothing.title", "没有可清理的生成文件" },
                {
                    "cleanup.nothing.body",
                    "当前配置路径和上次生成路径中都没有本工具生成的包装代码。"
                },
                {
                    "cleanup.kept.body",
                    "以下文件不符合清理条件，已保留：\n{0}"
                },
                { "cleanup.confirm.title", "清理 Menu Redirect 生成文件" },
                {
                    "cleanup.confirm.body",
                    "将删除以下由本工具生成的包装代码：\n\n{0}\n\n删除后原菜单会在下次域重载后恢复；重新打开设置页并点击“应用”可再次生成。"
                },
                { "cleanup.confirm.configKept", "规则配置会保留：{0}" },
                { "cleanup.confirm.noConfig", "未找到规则配置，本次只清理生成代码。" },
                { "cleanup.confirm.ok", "删除" },
                { "cleanup.confirm.cancel", "取消" },
                { "cleanup.result.title", "清理完成" },
                {
                    "cleanup.result.body",
                    "已删除 {0} 个生成文件。\n\n{1}\n\n原菜单会在下次域重载后恢复；重新打开设置页并点击“应用”可再次生成。"
                },
                { "cleanup.result.failures", "未删除 {0} 个文件：\n{1}" },
                { "cleanup.result.ok", "确定" },
                { "cleanup.error.notAsset", "拒绝删除 Assets 之外或路径非法的文件：{0}" },
                {
                    "cleanup.error.unexpectedFileName",
                    "文件名不是 MenuRedirects.g.cs，未删除：{0}"
                },
                { "cleanup.error.notGenerated", "文件不是本工具生成的包装代码，未删除：{0}" },
                { "cleanup.error.deleteFailed", "删除失败：{0}" },

                { "bridge.menuTypeMissing", "找不到 UnityEditor.Menu 类型" },
                { "bridge.probeFailed", "探测 Unity 内部菜单 API 失败" },
                {
                    "bridge.removeMissing",
                    "找不到 Menu.RemoveMenuItem(string)"
                },
                {
                    "bridge.refreshMissing",
                    "找不到 EditorUtility.Internal_UpdateAllMenus，移除后菜单界面可能不会立即刷新。"
                },
                {
                    "bridge.removeFailed",
                    "[MenuRedirect] RemoveMenuItem('{0}') 反射调用失败: {1}"
                },
                {
                    "bridge.refreshFailed",
                    "[MenuRedirect] Internal_UpdateAllMenus 反射调用失败: {0}"
                },
                {
                    "bridge.existsFailed",
                    "[MenuRedirect] MenuItemExists('{0}') 反射调用失败: {1}"
                },
                { "invoker.noMethod", "[MenuRedirect] 找不到可执行的原菜单方法: {0}" },
                {
                    "invoker.badSignature",
                    "[MenuRedirect] 原菜单方法签名不受支持: {0}.{1} ({2})"
                }
            };

        private static readonly Dictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "ui.language.label", "Language" },
                { "ui.language.auto", "System" },
                { "ui.language.chinese", "中文" },
                { "ui.language.english", "English" },
                { "ui.language.tooltip", "UI language. Defaults to the system language." },
                { "ui.loading", "Loading configuration…" },
                {
                    "ui.header.description",
                    "Move existing menu items to a new path, or hide them. Unity built-in menus are protected."
                },
                {
                    "ui.help.text",
                    "• Full leaf path: hides the item; enter a target path to redirect it.\n" +
                    "• Branch container path (top-level path with no /, or an unregistered intermediate container with children, e.g. NuGet or Window/Unity CLI Loop):\n" +
                    "    - Empty target: hides the entire branch.\n" +
                    "    - Target filled: moves all executable children under the new parent.\n" +
                    "    - A registered intermediate path with its own [MenuItem] is treated as a leaf.\n" +
                    "• Unity built-in menus are protected; third-party menus (including Tools) can be removed.\n" +
                    "• The target must not be under a menu that is being removed."
                },
                { "ui.header.help.show", "Help" },
                { "ui.header.help.hide", "Hide Help" },
                { "ui.config.label", "Config File" },
                { "ui.config.copy", "Copy" },
                { "ui.config.open", "Open Folder" },
                { "ui.generated.label", "Generated Code" },
                { "ui.generated.browse", "Browse…" },
                { "ui.generated.browse.tooltip", "Choose the output folder for wrapper code." },
                { "ui.generated.browse.title", "Select Generated Code Folder" },
                { "ui.generated.open", "Open" },
                { "ui.generated.open.tooltip", "Open the generated code folder." },
                { "ui.generated.default", "Default" },
                { "ui.generated.default.tooltip", "Restore the default output folder." },
                {
                    "ui.generated.tooltip",
                    "Folder for generated wrapper code; the actual file is {0}. It must be under Assets and inside an Editor folder."
                },
                { "ui.toolbar.enabled", "Enabled: {0}" },
                { "ui.toolbar.passed", "Passed: {0}" },
                { "ui.toolbar.warnings", "Warnings: {0}" },
                { "ui.toolbar.errors", "Errors: {0}" },
                { "ui.feedback.more", "… {0} more; see Console" },
                { "ui.toolbar.unsaved", "Unsaved" },
                { "ui.toolbar.pending", "Recompile pending" },
                { "ui.toolbar.revert", "Revert" },
                {
                    "ui.toolbar.revert.tooltip",
                    "Discard unsaved changes and restore the rules from the last save or apply."
                },
                { "ui.toolbar.apply", "Apply" },
                { "ui.toolbar.apply.tooltip", "Save the configuration and apply the menu redirects." },
                {
                    "ui.toolbar.apply.errorTooltip",
                    "Fix validation errors first; nothing will be saved or applied while errors exist."
                },
                { "ui.toolbar.import", "Import…" },
                {
                    "ui.toolbar.import.tooltip",
                    "Import rules from a JSON file and replace the current rules; Apply is still required to save."
                },
                { "ui.toolbar.export", "Export…" },
                {
                    "ui.toolbar.export.tooltip",
                    "Export the current rules as JSON; generated folder and other machine-specific settings are excluded."
                },
                { "ui.import.title", "Import Menu Redirect Rules" },
                { "ui.import.confirm.title", "Replace current rules?" },
                {
                    "ui.import.confirm.message",
                    "Importing will replace the current {0} rule(s) with {1} rule(s) from the file. Unsaved changes will be lost. Continue?"
                },
                { "ui.import.confirm.ok", "Replace" },
                { "ui.import.confirm.cancel", "Cancel" },
                { "ui.import.success", "Imported {0} rule(s); click Apply to save and activate." },
                { "ui.import.error.read", "Could not read the file: {0}" },
                { "ui.import.error.empty", "The rule file is empty." },
                { "ui.import.error.json", "Invalid JSON: {0}" },
                { "ui.import.error.format", "Not a Menu Redirect rule file." },
                { "ui.import.error.version", "Unsupported rule file version: {0}." },
                { "ui.import.error.rules", "The rules list is missing or invalid." },
                { "ui.import.error.rule", "Rule {0} is invalid." },
                { "ui.import.error.source", "Rule {0} has an invalid source path: {1}" },
                { "ui.import.error.target", "Rule {0} has an invalid target path: {1}" },
                { "ui.import.error.samePath", "Rule {0} target must differ from its source path." },
                { "ui.import.error.duplicate", "Rule {0} duplicates an earlier source path: {1}" },
                { "ui.export.title", "Export Menu Redirect Rules" },
                { "ui.export.success", "Exported {0} rule(s) to {1}." },
                { "ui.export.error", "Export failed: {0}" },
                { "ui.list.title", "Rules" },
                { "ui.list.add.tooltip", "Add rule" },
                { "ui.list.hint.empty", "Click + to add the first rule" },
                { "ui.list.hint.reorder", "Drag the handle on the left to reorder" },
                { "ui.list.hint.filtered", "Showing {0} of {1} rules; reordering is disabled while filtered" },
                { "ui.list.filter.issues.tooltip", "Show only rules with warnings or errors" },
                { "ui.list.filter.clear.tooltip", "Clear search and filters" },
                { "ui.list.filter.clear", "Clear Filters" },
                { "ui.list.empty.filtered.title", "No matching rules" },
                { "ui.list.empty.filtered.hint", "Adjust the search or clear filters to see all rules." },
                { "ui.list.empty.title", "No rules yet" },
                { "ui.list.empty.hint", "Add a rule to move a menu item or hide it." },
                { "ui.list.empty.add", "+ Add Rule" },
                { "ui.detail.title", "Rule Details" },
                { "ui.detail.delete.tooltip", "Delete current rule" },
                { "ui.detail.empty.title", "Select a rule" },
                {
                    "ui.detail.empty.hint",
                    "Click a rule on the left to view and edit its settings."
                },
                { "ui.detail.section.basic", "Basic Settings" },
                { "ui.detail.enabled", "Enabled" },
                { "ui.detail.priority", "Priority" },
                {
                    "ui.detail.priority.hint",
                    "Lower values place the new menu earlier among its siblings."
                },
                { "ui.detail.section.paths", "Path Mapping" },
                { "ui.detail.from", "Source" },
                { "ui.detail.from.pick", "Select…" },
                {
                    "ui.detail.from.pick.tooltip",
                    "Choose the source path from registered menus."
                },
                { "ui.detail.to", "Target" },
                { "ui.detail.to.pick", "Parent…" },
                {
                    "ui.detail.to.pick.tooltip",
                    "Choose the parent container for the new path."
                },
                {
                    "ui.detail.to.parentOnly",
                    "Parent inserted; the source is empty, so add the new menu name."
                },
                {
                    "ui.detail.paths.hint",
                    "Leave the target empty to only hide the original menu without creating a new item."
                },
                { "ui.picker.title", "Select Menu Path" },
                { "ui.picker.title.parent", "Select Target Parent" },
                { "ui.picker.search", "Search" },
                { "ui.picker.refresh", "Refresh" },
                {
                    "ui.picker.refresh.tooltip",
                    "Rescan the currently loaded assemblies."
                },
                { "ui.picker.count", "Showing {0} of {1}" },
                { "ui.picker.empty", "No matching menu paths." },
                { "ui.picker.empty.parent", "No matching parent paths." },
                {
                    "ui.picker.legend",
                    "✓ executable   ! hide-only / protected children   ✕ protected"
                },
                {
                    "ui.picker.legend.parent",
                    "✓ parent   ! protected children   ✕ protected"
                },
                {
                    "ui.picker.hint",
                    "Click a row to expand/collapse and show a hover (light blue); double-click/Enter to confirm the focused entry. The darker blue marks the value currently set in the rule; single-click does not move the focus; protected entries can also be hovered."
                },
                {
                    "ui.picker.hint.parent",
                    "Click a row to expand/collapse and show a hover (light blue); double-click/Enter to confirm the focused entry. The darker blue marks the current parent value; single-click does not move the focus; protected entries can also be hovered."
                },
                {
                    "ui.picker.tooltip.executable",
                    "Executable menu: can be redirected or hidden."
                },
                {
                    "ui.picker.tooltip.hideOnly",
                    "No executable static [MenuItem] method; hide-only."
                },
                {
                    "ui.picker.tooltip.branchExecutable",
                    "Branch container: contains menus that can be redirected."
                },
                {
                    "ui.picker.tooltip.branchHideOnly",
                    "Branch container: no executable children to redirect."
                },
                { "ui.detail.section.validation", "Validation" },
                { "ui.detail.apply.hint", "Click Apply to save and activate changes." },
                { "ui.detail.validation.disabled", "Disabled; not validated." },
                { "ui.detail.validation.passed", "✓ Passed" },
                { "ui.detail.validation.errors", "Errors: {0}" },
                { "ui.detail.validation.warnings", "Warnings: {0}" },
                { "ui.row.hide", "Hide" },
                { "ui.row.emptySource", "(Source path not set)" },
                {
                    "ui.row.emptySource.tooltip",
                    "Enter the source menu path to see a preview here."
                },
                { "ui.row.hideBranch.tooltip", "Hide {0} and all its children." },
                { "ui.row.hideLeaf.tooltip", "Hide the menu {0}." },
                { "ui.row.paths.tooltip", "Source: {0}\nTarget: {1}" },
                { "ui.row.priority.tooltip", "Priority: {0}" },
                { "ui.row.remove.tooltip", "Remove rule" },
                { "ui.row.status.disabled", "Disabled; not validated." },
                { "ui.row.status.passed", "Validation passed." },
                { "ui.row.status.errors", "Errors: {0}" },
                { "ui.row.status.warnings", "Warnings: {0}" },
                { "ui.row.status.details", "\n" },

                { "validation.source.empty", "Source path is empty" },
                { "validation.source.invalid", "Invalid source path: {0}" },
                { "validation.target.invalid", "Invalid target path: {0}" },
                { "validation.source.builtIn", "Unity built-in menu; cannot be removed or moved" },
                {
                    "validation.source.protectedContainer",
                    "This container contains Unity built-in menus and cannot be removed as a whole"
                },
                { "validation.source.missing", "Source path is not in the current menu registry: {0}" },
                {
                    "validation.source.notExecutable",
                    "Source has no executable static [MenuItem] method and cannot be redirected: {0}"
                },
                {
                    "validation.source.duplicateExecutable",
                    "Source has multiple executable [MenuItem] declarations: {0}"
                },
                {
                    "validation.source.missingHideOnly",
                    "The menu is not in the registry; hide-only still works for dynamically registered menus: {0}"
                },
                {
                    "validation.source.notExecutableHideOnly",
                    "The menu has no executable static method; hide-only can still work: {0}"
                },
                {
                    "validation.source.noExecutableChildren",
                    "The branch has no redirectable executable child menus: {0}"
                },
                {
                    "validation.source.protectedChildren",
                    "Skipped {0} protected child menus containing Unity built-ins: {1}"
                },
                {
                    "validation.source.nonExecutableChildren",
                    "Skipped {0} child menus without a static method: {1}"
                },
                {
                    "validation.source.overlap",
                    "Source \"{0}\" overlaps another rule's source \"{1}\"; hiding may remove too much"
                },
                {
                    "validation.target.builtIn",
                    "Target conflicts with a Unity built-in menu"
                },
                { "validation.same", "Source and target paths are identical" },
                { "validation.targetUnderSource", "Target is under the source path" },
                { "validation.target.exists", "Target menu already exists: {0}" },
                { "validation.source.duplicate", "Duplicate source path" },
                { "validation.target.duplicate", "Duplicate target path" },
                {
                    "validation.target.duplicateResolved",
                    "A generated target path is duplicated by another rule: {0}"
                },
                { "validation.target.hiddenByOther", "Target is hidden by another rule" },

                { "apply.reverted", "Unsaved changes reverted." },
                { "ui.draft.restored", "Restored unsaved changes from your last session. Click Apply to save them." },
                {
                    "apply.error.validation",
                    "Rule validation failed. Fix the errors first."
                },
                {
                    "apply.error.codegenFailed",
                    "Failed to generate wrapper code; the configuration was not saved."
                },
                {
                    "apply.error.emptySource",
                    "An enabled rule has no source path (fromPath). Fill it in or disable the rule."
                },
                {
                    "apply.error.duplicateSource",
                    "Duplicate source path: {0}. Merge the rules or disable one."
                },
                {
                    "apply.error.same",
                    "Source and target paths are identical; cannot redirect: {0}"
                },
                {
                    "apply.error.duplicateTarget",
                    "Duplicate target path: {0}. Unity does not allow duplicate menu names."
                },
                {
                    "apply.error.targetUnderSource",
                    "Target \"{0}\" is under source \"{1}\"; it will disappear when the source is removed."
                },
                {
                    "apply.error.targetUnderOther",
                    "Conflict: target \"{0}\" is under a menu removed by another rule (\"{1}\"); it will still be hidden after redirecting."
                },
                { "apply.error.saveFailed", "Failed to save the configuration: {0}" },
                {
                    "apply.error.generatedFolder.outsideAssets",
                    "The generated code folder must be under Assets."
                },
                {
                    "apply.error.generatedFolder.invalid",
                    "The generated code folder is invalid: {0}"
                },
                {
                    "apply.error.generatedFolder.notEditor",
                    "The generated code folder must be inside an Editor folder; otherwise the wrapper code cannot compile."
                },
                {
                    "apply.error.generatedFolder.folderIsFile",
                    "The generated code folder path is occupied by a file: {0}"
                },
                {
                    "apply.error.generatedFolder.targetIsFolder",
                    "The generated code file path is occupied by a folder: {0}"
                },
                {
                    "apply.error.generatedFolder.fileExists",
                    "A file not generated by this tool already exists at {0}. Choose another folder."
                },
                {
                    "apply.error.generatedStateSaveFailed",
                    "The configuration was saved, but the generated-code state could not be saved: {0}"
                },
                {
                    "apply.saved.codegen.hidden",
                    "Configuration saved. Wrapper code written to {0}. Original menu items hidden: {1}. The new menus will appear after compilation."
                },
                {
                    "apply.saved.codegen.noneHidden",
                    "Configuration saved. Wrapper code written to {0}. The new menus will appear after compilation."
                },
                {
                    "apply.saved.codegen.deferred",
                    "Configuration saved. Wrapper code written to {0}. Original menus will be hidden after compilation so a failed build cannot leave you without menu entries."
                },
                {
                    "apply.saved.hidden",
                    "Configuration saved. Original menu items hidden: {0}."
                },
                {
                    "apply.saved.withWarnings",
                    "Configuration saved and applied, with warnings shown below."
                },
                { "apply.saved.done", "Configuration saved and applied." },
                { "apply.feedback.hotkey", "{0}\n{1}" },
                {
                    "apply.hotkey.hint",
                    "Shortcut note: {0} original menu item(s) had shortcuts ({1}). " +
                    "Wrappers do not copy them; add the shortcut manually in the generated file."
                },

                {
                    "guard.root",
                    "\"{0}\" is a Unity built-in top-level menu and cannot be removed or moved as a whole"
                },
                {
                    "guard.item",
                    "\"{0}\" is a Unity built-in menu item (registered by {1}) and cannot be removed or moved"
                },
                {
                    "guard.container",
                    "\"{0}\" contains the Unity built-in submenu \"{1}\" (registered by {2}) and cannot be removed or moved as a whole"
                },
                {
                    "engine.targetBuiltIn",
                    "Target \"{0}\" conflicts with a Unity built-in menu"
                },
                {
                    "engine.targetExists",
                    "Target \"{0}\" already exists; skipped wrapper generation for this item"
                },
                {
                    "engine.targetSameSource",
                    "Target \"{0}\" is the same as the source path; execution was blocked to avoid hiding the new menu"
                },
                {
                    "engine.targetUnderSource",
                    "Target \"{0}\" is under source \"{1}\"; hiding the source would also remove the new menu"
                },
                {
                    "engine.invalidPath",
                    "Invalid menu path; execution was blocked: {0}"
                },
                {
                    "engine.noExecutable",
                    "No executable original menu method found (the menu may be dynamically registered)"
                },
                {
                    "engine.noExecutableLeaf",
                    "No executable original menu method found (the menu may be dynamically registered); cannot redirect"
                },
                {
                    "engine.noMovableChildren",
                    "No movable child menus found (the parent may be dynamically registered)"
                },
                { "engine.pathOnly", "{0}" },
                { "engine.originalPath", "{0} (original path: {1})" },
                { "engine.cannotHide", "Cannot hide: {0} — internal Menu API call failed" },
                {
                    "engine.stillExists",
                    "Item still exists after removal (another plugin may have re-registered it): {0}"
                },
                {
                    "engine.apiUnavailable",
                    "Internal Menu API is unavailable; cannot hide original menus: {0}"
                },
                { "engine.skipped", "Skipped: {0} — {1}" },
                { "engine.noRemovable", "No removable menu item found: {0}" },
                { "engine.hideFailed", "Failed to hide: {0}" },
                { "engine.summary", "[MenuRedirect] Hide complete: hidden={0}, skipped={1}" },
                {
                    "engine.codegenUpdated",
                    "[MenuRedirect] Wrapper code updated; waiting for Unity to compile before the new menus take effect."
                },
                { "engine.writeFailed", "[MenuRedirect] Failed to write wrapper code: {0}" },
                { "engine.rollbackFailed", "[MenuRedirect] Failed to roll back wrapper code: {0}" },
                {
                    "engine.oldGeneratedDeleteFailed",
                    "[MenuRedirect] Failed to delete old wrapper code: {0}"
                },
                {
                    "engine.oldGeneratedKept",
                    "[MenuRedirect] Old file is not wrapper code generated by this tool and was kept: {0}"
                },
                {
                    "engine.oldGeneratedNotGenerated",
                    "A file not generated by this tool occupies the old wrapper path; apply was aborted: {0}"
                },
                {
                    "engine.conflictingGenerated",
                    "Another generated wrapper file remains in the project; apply was aborted to avoid duplicate classes: {0}"
                },
                {
                    "engine.generatedMissing",
                    "Wrapper code has not been generated yet; hiding original menus is deferred: {0}"
                },
                {
                    "engine.generatedNotGenerated",
                    "The wrapper path contains a file not generated by this tool; hiding original menus is deferred: {0}"
                },
                {
                    "engine.generatedStale",
                    "Wrapper code does not match the current rules (waiting for compile); hiding original menus is deferred: {0}"
                },
                {
                    "engine.hideDeferred",
                    "[MenuRedirect] Hiding original menus is deferred: {0}"
                },

                { "cleanup.nothing.title", "Nothing to clean up" },
                {
                    "cleanup.nothing.body",
                    "No wrapper code generated by this tool was found at the current or previous generated path."
                },
                {
                    "cleanup.kept.body",
                    "These files did not meet the cleanup criteria and were left untouched:\n{0}"
                },
                { "cleanup.confirm.title", "Clean up Menu Redirect generated files" },
                {
                    "cleanup.confirm.body",
                    "The following wrapper code generated by this tool will be deleted:\n\n{0}\n\nOriginal menus will return after the next domain reload. Open the settings page and click Apply to generate the wrappers again."
                },
                { "cleanup.confirm.configKept", "The rules configuration will be kept: {0}" },
                {
                    "cleanup.confirm.noConfig",
                    "No rules configuration was found; only generated code will be cleaned up."
                },
                { "cleanup.confirm.ok", "Delete" },
                { "cleanup.confirm.cancel", "Cancel" },
                { "cleanup.result.title", "Cleanup complete" },
                {
                    "cleanup.result.body",
                    "Deleted {0} generated file(s).\n\n{1}\n\nOriginal menus will return after the next domain reload. Open the settings page and click Apply to generate the wrappers again."
                },
                { "cleanup.result.failures", "Failed to delete {0} file(s):\n{1}" },
                { "cleanup.result.ok", "OK" },
                {
                    "cleanup.error.notAsset",
                    "Refused to delete a file outside Assets or with an invalid path: {0}"
                },
                {
                    "cleanup.error.unexpectedFileName",
                    "The file name is not MenuRedirects.g.cs; it was not deleted: {0}"
                },
                {
                    "cleanup.error.notGenerated",
                    "The file was not generated by this tool and was not deleted: {0}"
                },
                { "cleanup.error.deleteFailed", "Failed to delete: {0}" },

                { "bridge.menuTypeMissing", "UnityEditor.Menu type not found" },
                { "bridge.probeFailed", "Failed to probe Unity internal menu API" },
                { "bridge.removeMissing", "Menu.RemoveMenuItem(string) not found" },
                {
                    "bridge.refreshMissing",
                    "EditorUtility.Internal_UpdateAllMenus not found; the menu UI may not refresh immediately after removal."
                },
                {
                    "bridge.removeFailed",
                    "[MenuRedirect] RemoveMenuItem('{0}') reflection call failed: {1}"
                },
                {
                    "bridge.refreshFailed",
                    "[MenuRedirect] Internal_UpdateAllMenus reflection call failed: {0}"
                },
                {
                    "bridge.existsFailed",
                    "[MenuRedirect] MenuItemExists('{0}') reflection call failed: {1}"
                },
                {
                    "invoker.noMethod",
                    "[MenuRedirect] No executable original menu method found: {0}"
                },
                {
                    "invoker.badSignature",
                    "[MenuRedirect] Unsupported original menu method signature: {0}.{1} ({2})"
                }
            };

        private static readonly HashSet<string> WarnedMissingKeys =
            new HashSet<string>(StringComparer.Ordinal);

        private static MenuRedirectLanguage _mode = MenuRedirectLanguage.Auto;
        private static bool _loaded;
        private static string? _preferenceKey;
        private static MenuRedirectLanguage _resolvedLanguage;
        private static bool _resolvedLanguageValid;

        internal static MenuRedirectLanguage Mode
        {
            get
            {
                EnsureLoaded();
                return _mode;
            }
        }

        internal static MenuRedirectLanguage CurrentLanguage
        {
            get
            {
                EnsureLoaded();
                if (!_resolvedLanguageValid)
                {
                    _resolvedLanguage = Resolve(_mode);
                    _resolvedLanguageValid = true;
                }

                return _resolvedLanguage;
            }
        }

        /// <summary>设置当前项目的语言模式；返回解析后的界面语言是否发生变化。</summary>
        internal static bool SetMode(MenuRedirectLanguage mode)
        {
            EnsureLoaded();
            MenuRedirectLanguage previous = Resolve(_mode);
            _mode = mode;
            _resolvedLanguageValid = false;
            EditorPrefs.SetString(PreferenceKey, ToStorageValue(mode));
            return previous != Resolve(_mode);
        }

        internal static string T(string key)
        {
            return GetTemplate(CurrentLanguage, key);
        }

        internal static string F(string key, params object[] args)
        {
            return Format(CurrentLanguage, key, args);
        }

        internal static MenuRedirectText Text(string key, params object[] args)
        {
            return new MenuRedirectText(key, args);
        }

        internal static string Format(MenuRedirectLanguage language, string key, params object[] args)
        {
            string template = GetTemplate(language, key);

            if (args == null || args.Length == 0)
                return template;

            var resolvedArgs = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                object? arg = args[i];
                resolvedArgs[i] = arg is MenuRedirectText text
                    ? text.Resolve(language)
                    : arg ?? string.Empty;
            }

            return string.Format(CultureInfo.InvariantCulture, template, resolvedArgs);
        }

        private static string GetTemplate(MenuRedirectLanguage language, string key)
        {
            Dictionary<string, string> table = language == MenuRedirectLanguage.Chinese ? Chinese : English;
            if (table.TryGetValue(key, out string? value))
                return value;

            // 英文缺失时回退中文，保证界面不会直接暴露 key。
            if (English.TryGetValue(key, out value))
            {
                WarnMissingKey(language, key);
                return value;
            }

            if (Chinese.TryGetValue(key, out value))
            {
                WarnMissingKey(language, key);
                return value;
            }

            WarnMissingKey(language, key);
            return key;
        }

        private static void WarnMissingKey(MenuRedirectLanguage language, string key)
        {
            if (WarnedMissingKeys.Add(language + ":" + key))
                Debug.LogWarning("[MenuRedirect] Missing " + language + " localization: " + key);
        }

        private static MenuRedirectLanguage Resolve(MenuRedirectLanguage mode)
        {
            if (mode != MenuRedirectLanguage.Auto)
                return mode;

            string systemLanguage = Application.systemLanguage.ToString();
            return systemLanguage == "Chinese" ||
                   systemLanguage == "ChineseSimplified" ||
                   systemLanguage == "ChineseTraditional"
                ? MenuRedirectLanguage.Chinese
                : MenuRedirectLanguage.English;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;
            string stored = EditorPrefs.GetString(PreferenceKey, string.Empty);
            _mode = ParseMode(stored);
            _resolvedLanguageValid = false;
        }

        private static MenuRedirectLanguage ParseMode(string stored)
        {
            if (string.Equals(stored, "zh", StringComparison.OrdinalIgnoreCase))
                return MenuRedirectLanguage.Chinese;

            if (string.Equals(stored, "en", StringComparison.OrdinalIgnoreCase))
                return MenuRedirectLanguage.English;

            return MenuRedirectLanguage.Auto;
        }

        private static string ToStorageValue(MenuRedirectLanguage mode)
        {
            if (mode == MenuRedirectLanguage.Chinese)
                return "zh";

            if (mode == MenuRedirectLanguage.English)
                return "en";

            return "auto";
        }

        private static string PreferenceKey
        {
            get
            {
                if (_preferenceKey != null)
                    return _preferenceKey;

                _preferenceKey = PreferencePrefix + GetProjectId();
                return _preferenceKey;
            }
        }

        private static string GetProjectId()
        {
            try
            {
                Guid productGuid = PlayerSettings.productGUID;
                if (productGuid != Guid.Empty)
                    return productGuid.ToString("N");
            }
            catch
            {
                // 极少数 Unity 版本/导入阶段无法读取 productGUID，退回项目路径哈希。
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            return Hash128.Compute(projectRoot).ToString();
        }
    }
}
