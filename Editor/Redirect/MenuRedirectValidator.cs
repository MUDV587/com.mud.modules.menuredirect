using System;
using System.Collections.Generic;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;

namespace MUD.Modules.MenuRedirect
{
    internal enum MenuRedirectIssueLevel
    {
        None,
        Warning,
        Error
    }

    internal sealed class MenuRedirectIssue
    {
        internal MenuRedirectIssue(MenuRedirectIssueLevel level, MenuRedirectText message)
        {
            Level = level;
            Message = message;
        }

        internal MenuRedirectIssueLevel Level { get; }
        internal MenuRedirectText Message { get; }
    }

    internal sealed class MenuRedirectRuleValidation
    {
        internal readonly List<MenuRedirectIssue> Issues = new List<MenuRedirectIssue>();

        internal MenuRedirectIssueLevel Level { get; private set; }

        internal int Count => Issues.Count;

        internal void Add(MenuRedirectIssueLevel level, MenuRedirectText message)
        {
            if (level > Level)
                Level = level;

            for (int i = 0; i < Issues.Count; i++)
            {
                MenuRedirectIssue existing = Issues[i];
                if (existing.Message.Key == message.Key &&
                    string.Equals(
                        existing.Message.Resolve(),
                        message.Resolve(),
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            Issues.Add(new MenuRedirectIssue(level, message));
        }
    }

    internal sealed class MenuRedirectValidationResult
    {
        internal readonly List<MenuRedirectRuleValidation> Rules =
            new List<MenuRedirectRuleValidation>();

        internal MenuRedirectText? GeneratedFolderError { get; set; }

        internal bool HasErrors
        {
            get
            {
                if (GeneratedFolderError != null)
                    return true;

                for (int i = 0; i < Rules.Count; i++)
                {
                    if (Rules[i].Level == MenuRedirectIssueLevel.Error)
                        return true;
                }

                return false;
            }
        }

        internal MenuRedirectText? FirstError
        {
            get
            {
                if (GeneratedFolderError != null)
                    return GeneratedFolderError;

                for (int i = 0; i < Rules.Count; i++)
                {
                    MenuRedirectRuleValidation validation = Rules[i];
                    for (int j = 0; j < validation.Issues.Count; j++)
                    {
                        MenuRedirectIssue issue = validation.Issues[j];
                        if (issue.Level == MenuRedirectIssueLevel.Error)
                            return issue.Message;
                    }
                }

                return null;
            }
        }
    }

    /// <summary>
    /// 共享规则验证：设置页状态、详情面板和 Apply 使用同一份结果。
    /// 这里只做纯读取验证，不修改规则，也不删除/隐藏任何菜单。
    /// </summary>
    internal static class MenuRedirectValidator
    {
        internal static MenuRedirectValidationResult Validate(MenuRedirectSettings? settings)
        {
            return Validate(settings?.rules, settings?.generatedCodeFolder);
        }

        internal static MenuRedirectValidationResult Validate(
            IReadOnlyList<MenuRedirectRule>? rules,
            string? generatedCodeFolder)
        {
            var result = new MenuRedirectValidationResult();
            MenuRegistry registry = MenuRedirectEngine.Registry;
            var infos = new List<RuleInfo>();

            if (rules == null)
            {
                ValidateGeneratedFolder(result, generatedCodeFolder);
                return result;
            }

            for (int i = 0; i < rules.Count; i++)
                result.Rules.Add(new MenuRedirectRuleValidation());

            for (int i = 0; i < rules.Count; i++)
            {
                MenuRedirectRule? rule = rules[i];
                if (rule == null || !rule.enabled)
                    continue;

                MenuRedirectRuleValidation validation = result.Rules[i];
                string from = MenuRegistry.NormalizePath(rule.fromPath);
                string to = MenuRegistry.NormalizePath(rule.toPath);

                if (from.Length == 0)
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.source.empty"));
                    continue;
                }

                bool fromValid = IsValidMenuPath(rule.fromPath, from);
                bool toValid = to.Length == 0 || IsValidMenuPath(rule.toPath, to);

                if (!fromValid)
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.source.invalid", (rule.fromPath ?? string.Empty).Trim()));
                }

                if (!toValid)
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.target.invalid", (rule.toPath ?? string.Empty).Trim()));
                }

                if (!fromValid)
                    continue;

                var info = new RuleInfo(i, from, to, registry);
                info.TargetInvalid = !toValid;
                infos.Add(info);

                if (MenuRedirectGuard.IsBuiltIn(from, registry, out MenuRedirectText? sourceReason))
                {
                    info.SourceProtected = true;
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        sourceReason ?? L.Text("validation.source.builtIn"));
                }

                if (!info.SourceProtected &&
                    MenuRedirectGuard.IsProtectedContainer(
                        from,
                        registry,
                        out MenuRedirectText? containerReason))
                {
                    // 嵌套容器里的 Unity 子项不能被整体移除；仅隐藏时直接拦截。
                    // 分支重定向仍可搬走非受保护子项，因此只给警告。
                    if (info.HideOnly || !info.IsBranch)
                    {
                        info.SourceProtected = true;
                        validation.Add(
                            MenuRedirectIssueLevel.Error,
                            containerReason ?? L.Text("validation.source.protectedContainer"));
                    }
                    else
                    {
                        validation.Add(
                            MenuRedirectIssueLevel.Warning,
                            containerReason ?? L.Text("validation.source.protectedContainer"));
                    }
                }

                if (!info.HideOnly)
                {
                    if (string.Equals(from, to, StringComparison.Ordinal))
                    {
                        validation.Add(
                            MenuRedirectIssueLevel.Error,
                            L.Text("validation.same"));
                        info.TargetInvalid = true;
                    }
                    else if (IsUnder(to, from))
                    {
                        validation.Add(
                            MenuRedirectIssueLevel.Error,
                            L.Text("validation.targetUnderSource"));
                        info.TargetInvalid = true;
                    }
                }

                if (info.SourceProtected)
                    continue;

                ValidateSourceExecutable(info, validation);
                if (!info.HideOnly && toValid)
                    ValidateTargets(info, validation);
            }

            ValidateCrossRuleConflicts(infos, registry, result);
            ValidateGeneratedFolder(result, generatedCodeFolder);
            return result;
        }

        private static void ValidateSourceExecutable(
            RuleInfo info,
            MenuRedirectRuleValidation validation)
        {
            List<string> children = info.Registry.ChildrenOf(info.From);

            if (info.HideOnly)
            {
                if (!info.Registry.Contains(info.From) && children.Count == 0)
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Warning,
                        L.Text("validation.source.missingHideOnly", info.From));
                    return;
                }

                if (children.Count == 0 &&
                    info.Registry.TryGetExecutable(info.From, out _) == false)
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Warning,
                        L.Text("validation.source.notExecutableHideOnly", info.From));
                }

                return;
            }

            if (!info.IsBranch)
            {
                if (!info.Registry.Contains(info.From))
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.source.missing", info.From));
                    return;
                }

                int executableCount = info.Registry.GetExecutableCount(info.From);
                if (executableCount == 0)
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.source.notExecutable", info.From));
                }
                else if (executableCount > 1)
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.source.duplicateExecutable", info.From));
                }

                return;
            }

            int protectedCount = 0;
            int nonExecutableCount = 0;
            var protectedPrefixes = new List<string>();

            for (int i = 0; i < children.Count; i++)
            {
                string child = children[i];

                if (IsUnderAny(child, protectedPrefixes))
                    continue;

                if (MenuRedirectGuard.IsBuiltIn(child, info.Registry, out _) ||
                    MenuRedirectGuard.IsProtectedContainer(child, info.Registry, out _))
                {
                    protectedPrefixes.Add(child);
                    protectedCount++;
                    continue;
                }

                int executableCount = info.Registry.GetExecutableCount(child);
                if (executableCount == 0)
                {
                    nonExecutableCount++;
                    continue;
                }

                info.MovableChildren.Add(child);

                if (executableCount > 1)
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.source.duplicateExecutable", child));
                }
            }

            if (info.MovableChildren.Count == 0)
            {
                validation.Add(
                    MenuRedirectIssueLevel.Error,
                    L.Text("validation.source.noExecutableChildren", info.From));
            }

            if (protectedCount > 0)
            {
                validation.Add(
                    MenuRedirectIssueLevel.Warning,
                    L.Text(
                        "validation.source.protectedChildren",
                        protectedCount,
                        info.From));
            }

            if (nonExecutableCount > 0)
            {
                validation.Add(
                    MenuRedirectIssueLevel.Warning,
                    L.Text(
                        "validation.source.nonExecutableChildren",
                        nonExecutableCount,
                        info.From));
            }
        }

        private static void ValidateTargets(
            RuleInfo info,
            MenuRedirectRuleValidation validation)
        {
            if (info.TargetInvalid)
                return;

            if (MenuRedirectGuard.IsBuiltIn(info.To, info.Registry, out _))
            {
                validation.Add(
                    MenuRedirectIssueLevel.Error,
                    L.Text("validation.target.builtIn", info.To));
                info.TargetInvalid = true;
                return;
            }

            if (!info.IsBranch)
            {
                if (info.Registry.Contains(info.To))
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.target.exists", info.To));
                    info.TargetInvalid = true;
                }

                return;
            }

            List<string> children = info.MovableChildren;
            string prefix = info.From + "/";

            for (int i = 0; i < children.Count; i++)
            {
                string child = children[i];
                string target = info.To + "/" + child.Substring(prefix.Length);

                if (MenuRedirectGuard.IsBuiltIn(target, info.Registry, out _))
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.target.builtIn", target));
                    continue;
                }

                if (info.Registry.Contains(target))
                {
                    validation.Add(
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.target.exists", target));
                }
            }
        }

        private static void ValidateCrossRuleConflicts(
            List<RuleInfo> infos,
            MenuRegistry registry,
            MenuRedirectValidationResult result)
        {
            var sources = new Dictionary<string, List<RuleInfo>>(StringComparer.Ordinal);
            var declaredTargets = new Dictionary<string, List<RuleInfo>>(StringComparer.Ordinal);
            var resolvedTargets = new Dictionary<string, List<RuleInfo>>(StringComparer.Ordinal);

            foreach (RuleInfo info in infos)
            {
                AddToMap(sources, info.From, info);

                if (info.HideOnly)
                    continue;

                AddToMap(declaredTargets, info.To, info);

                if (!info.IsBranch)
                {
                    AddToMap(resolvedTargets, info.To, info);
                    continue;
                }

                List<string> children = info.MovableChildren;
                string prefix = info.From + "/";

                foreach (string child in children)
                {
                    AddToMap(
                        resolvedTargets,
                        info.To + "/" + child.Substring(prefix.Length),
                        info);
                }
            }

            foreach (KeyValuePair<string, List<RuleInfo>> pair in sources)
            {
                if (pair.Value.Count < 2)
                    continue;

                foreach (RuleInfo info in pair.Value)
                {
                    AddIssue(
                        result,
                        info.Index,
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.source.duplicate"));
                }
            }

            foreach (KeyValuePair<string, List<RuleInfo>> pair in declaredTargets)
            {
                if (pair.Value.Count < 2)
                    continue;

                foreach (RuleInfo info in pair.Value)
                {
                    AddIssue(
                        result,
                        info.Index,
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.target.duplicate"));
                }
            }

            foreach (KeyValuePair<string, List<RuleInfo>> pair in resolvedTargets)
            {
                if (pair.Value.Count < 2)
                    continue;

                foreach (RuleInfo info in pair.Value)
                {
                    AddIssue(
                        result,
                        info.Index,
                        MenuRedirectIssueLevel.Error,
                        L.Text("validation.target.duplicateResolved", pair.Key));
                }
            }

            for (int i = 0; i < infos.Count; i++)
            {
                RuleInfo current = infos[i];

                for (int j = i + 1; j < infos.Count; j++)
                {
                    RuleInfo other = infos[j];

                    if (string.Equals(current.From, other.From, StringComparison.Ordinal))
                        continue;

                    if (IsUnder(current.From, other.From))
                    {
                        AddIssue(
                            result,
                            current.Index,
                            MenuRedirectIssueLevel.Error,
                            L.Text("validation.source.overlap", current.From, other.From));
                    }

                    if (IsUnder(other.From, current.From))
                    {
                        AddIssue(
                            result,
                            other.Index,
                            MenuRedirectIssueLevel.Error,
                            L.Text("validation.source.overlap", other.From, current.From));
                        AddIssue(
                            result,
                            current.Index,
                            MenuRedirectIssueLevel.Error,
                            L.Text("validation.source.overlap", current.From, other.From));
                    }
                }

                if (current.HideOnly || current.TargetInvalid)
                    continue;

                foreach (KeyValuePair<string, List<RuleInfo>> pair in sources)
                {
                    bool otherRule = false;
                    foreach (RuleInfo sourceInfo in pair.Value)
                    {
                        if (sourceInfo.Index != current.Index)
                        {
                            otherRule = true;
                            break;
                        }
                    }

                    if (!otherRule)
                        continue;

                    if (IsUnder(current.To, pair.Key))
                    {
                        AddIssue(
                            result,
                            current.Index,
                            MenuRedirectIssueLevel.Error,
                            L.Text("validation.target.hiddenByOther"));
                    }

                    if (!current.IsBranch)
                        continue;

                    List<string> children = current.MovableChildren;
                    string prefix = current.From + "/";

                    foreach (string child in children)
                    {
                        string target = current.To + "/" + child.Substring(prefix.Length);
                        if (IsUnder(target, pair.Key))
                        {
                            AddIssue(
                                result,
                                current.Index,
                                MenuRedirectIssueLevel.Error,
                                L.Text("validation.target.hiddenByOther"));
                        }
                    }
                }
            }
        }

        private static void AddIssue(
            MenuRedirectValidationResult result,
            int index,
            MenuRedirectIssueLevel level,
            MenuRedirectText message)
        {
            if (index < 0 || index >= result.Rules.Count)
                return;

            result.Rules[index].Add(level, message);
        }

        private static void AddToMap(
            Dictionary<string, List<RuleInfo>> map,
            string path,
            RuleInfo info)
        {
            if (path.Length == 0)
                return;

            if (!map.TryGetValue(path, out List<RuleInfo>? list))
            {
                list = new List<RuleInfo>();
                map.Add(path, list);
            }

            list.Add(info);
        }

        private static void ValidateGeneratedFolder(
            MenuRedirectValidationResult result,
            string? generatedCodeFolder)
        {
            result.GeneratedFolderError =
                MenuRedirectEngine.ValidateGeneratedFolder(generatedCodeFolder);
        }

        /// <summary>
        /// 基础菜单路径校验。Apply 前的验证和旧配置的域重载重放共用，
        /// 避免旧配置绕过 UI 后把不可用路径写进包装代码并隐藏原菜单。
        /// </summary>
        internal static bool IsValidMenuPath(string? rawPath, string path)
        {
            if (string.IsNullOrWhiteSpace(rawPath) || path.Length == 0)
                return false;

            if (rawPath!.IndexOf('\\') >= 0 ||
                path.IndexOf('\\') >= 0 ||
                path[0] == '/' ||
                path[path.Length - 1] == '/')
            {
                return false;
            }

            string[] segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0 ||
                    !string.Equals(segment, segment.Trim(), StringComparison.Ordinal) ||
                    string.Equals(segment, ".", StringComparison.Ordinal) ||
                    string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    return false;
                }

                for (int j = 0; j < segment.Length; j++)
                {
                    if (char.IsControl(segment[j]))
                        return false;
                }
            }

            return true;
        }

        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
                return false;

            return string.Equals(path, root, StringComparison.Ordinal) ||
                   path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private static bool IsUnderAny(string path, List<string> roots)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                if (path.StartsWith(roots[i] + "/", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private sealed class RuleInfo
        {
            internal RuleInfo(
                int index,
                string from,
                string to,
                MenuRegistry registry)
            {
                Index = index;
                From = from;
                To = to;
                Registry = registry;
            }

            internal int Index { get; }
            internal string From { get; }
            internal string To { get; }
            internal MenuRegistry Registry { get; }
            internal readonly List<string> MovableChildren = new List<string>();
            internal bool IsBranch => Registry.IsBranchPath(From);
            internal bool HideOnly => To.Length == 0;
            internal bool SourceProtected { get; set; }
            internal bool TargetInvalid { get; set; }
        }
    }
}
