using System;
using System.Collections.Generic;
using UnityEngine;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 规则文件导入/导出。文件只包含规则，不包含生成目录、语言等本机配置，
    /// 便于跨项目复用；导入只生成内存规则，仍需在设置页点“应用”才落盘。
    /// </summary>
    internal static class MenuRedirectRuleFile
    {
        internal const int CurrentVersion = 1;
        internal const string Format = "MUD.MenuRedirect.Rules";

        [Serializable]
        private sealed class RuleFileDto
        {
            public string? format;
            public int version;
            public int count = -1;
            public List<RuleDto?>? rules;
        }

        [Serializable]
        private sealed class RuleDto
        {
            public string fromPath = string.Empty;
            public string toPath = string.Empty;
            public int priority;
            public bool enabled = true;
        }

        internal static string ToJson(IReadOnlyList<MenuRedirectRule> rules)
        {
            var fileRules = new List<RuleDto?>();

            for (int i = 0; i < rules.Count; i++)
            {
                MenuRedirectRule rule = rules[i];
                if (rule == null)
                    continue;

                fileRules.Add(new RuleDto
                {
                    fromPath = rule.fromPath ?? string.Empty,
                    toPath = rule.toPath ?? string.Empty,
                    priority = rule.priority,
                    enabled = rule.enabled
                });
            }

            var file = new RuleFileDto
            {
                format = Format,
                version = CurrentVersion,
                count = fileRules.Count,
                rules = fileRules
            };

            return JsonUtility.ToJson(file, true) + Environment.NewLine;
        }

        private static bool TryReadFile(
            string? json,
            out RuleFileDto? file,
            out MenuRedirectText? error)
        {
            file = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = L.Text("ui.import.error.empty");
                return false;
            }

            try
            {
                file = JsonUtility.FromJson<RuleFileDto>(json);
            }
            catch (Exception exception)
            {
                error = L.Text("ui.import.error.json", exception.Message);
                return false;
            }

            if (file == null ||
                !string.Equals(file.format, Format, StringComparison.Ordinal))
            {
                error = L.Text("ui.import.error.format");
                return false;
            }

            if (file.version != CurrentVersion)
            {
                error = L.Text("ui.import.error.version", file.version);
                return false;
            }

            if (file.rules == null)
            {
                error = L.Text("ui.import.error.rules");
                return false;
            }

            if (file.count < 0 || file.count != file.rules.Count)
            {
                error = L.Text("ui.import.error.rules");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 读取草稿中的规则：只校验文件结构，不校验路径是否合法。
        /// 草稿必须能保存用户键入到一半的内容，否则崩溃或域重载会丢输入。
        /// </summary>
        internal static bool TryParseDraft(
            string? json,
            out List<MenuRedirectRule> rules,
            out MenuRedirectText? error)
        {
            rules = new List<MenuRedirectRule>();

            if (!TryReadFile(json, out RuleFileDto? file, out error))
                return false;

            List<RuleDto?> fileRules = file!.rules!;
            for (int i = 0; i < fileRules.Count; i++)
            {
                RuleDto? dto = fileRules[i];
                if (dto == null)
                {
                    rules.Clear();
                    error = L.Text("ui.import.error.rule", i + 1);
                    return false;
                }

                rules.Add(new MenuRedirectRule
                {
                    fromPath = dto.fromPath ?? string.Empty,
                    toPath = dto.toPath ?? string.Empty,
                    priority = dto.priority,
                    enabled = dto.enabled
                });
            }

            return true;
        }

        internal static bool TryParse(
            string? json,
            out List<MenuRedirectRule> rules,
            out MenuRedirectText? error)
        {
            rules = new List<MenuRedirectRule>();

            if (!TryReadFile(json, out RuleFileDto? file, out error))
                return false;

            List<RuleDto?> fileRules = file!.rules!;
            var sources = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < fileRules.Count; i++)
            {
                RuleDto? dto = fileRules[i];

                if (dto == null)
                {
                    rules.Clear();
                    error = L.Text("ui.import.error.rule", i + 1);
                    return false;
                }

                string rawFrom = dto.fromPath ?? string.Empty;
                string from = MenuRegistry.NormalizePath(rawFrom);

                if (!MenuRedirectValidator.IsValidMenuPath(rawFrom, from))
                {
                    rules.Clear();
                    error = L.Text(
                        "ui.import.error.source",
                        i + 1,
                        rawFrom.Trim());
                    return false;
                }

                string rawTo = dto.toPath ?? string.Empty;
                string to = MenuRegistry.NormalizePath(rawTo);

                if (to.Length > 0 &&
                    !MenuRedirectValidator.IsValidMenuPath(rawTo, to))
                {
                    rules.Clear();
                    error = L.Text(
                        "ui.import.error.target",
                        i + 1,
                        rawTo.Trim());
                    return false;
                }

                if (to.Length > 0 &&
                    string.Equals(from, to, StringComparison.Ordinal))
                {
                    rules.Clear();
                    error = L.Text("ui.import.error.samePath", i + 1);
                    return false;
                }

                if (!sources.Add(from))
                {
                    rules.Clear();
                    error = L.Text(
                        "ui.import.error.duplicate",
                        i + 1,
                        from);
                    return false;
                }

                rules.Add(new MenuRedirectRule
                {
                    fromPath = from,
                    toPath = to,
                    priority = dto.priority,
                    enabled = dto.enabled
                });
            }

            return true;
        }
    }
}
