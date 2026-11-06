using System;
using System.IO;
using UnityEngine;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>草稿内容：同时保存“已保存基线”和“未保存草稿”，用于判断草稿是否仍然适用。</summary>
    internal sealed class MenuRedirectDraftData
    {
        public string savedRulesJson = string.Empty;
        public string savedGeneratedCodeFolder = string.Empty;
        public string draftRulesJson = string.Empty;
        public string draftGeneratedCodeFolder = string.Empty;
        public string savedAtUtc = string.Empty;

        internal bool MatchesBaseline(string rulesJson, string generatedCodeFolder)
        {
            return string.Equals(savedRulesJson, rulesJson, StringComparison.Ordinal) &&
                   string.Equals(
                       MenuRedirectEngine.NormalizeGeneratedFolder(savedGeneratedCodeFolder),
                       MenuRedirectEngine.NormalizeGeneratedFolder(generatedCodeFolder),
                       StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 未保存草稿。文件位于 Library，不进版本库；只在规则/生成目录确实被修改后写入。
    /// 草稿带保存基线，配置在其它会话中成功应用后，旧草稿会被判定失效。
    /// </summary>
    internal static class MenuRedirectDraft
    {
        internal const int CurrentVersion = 1;
        internal const string Format = "MUD.MenuRedirect.Draft";
        internal const string RelativePath = "Library/MenuRedirectDraft.json";

        [Serializable]
        private sealed class DraftFileDto
        {
            public string? format;
            public int version;
            public string? savedRulesJson;
            public string? savedGeneratedCodeFolder;
            public string? draftRulesJson;
            public string? draftGeneratedCodeFolder;
            public string? savedAtUtc;
        }

        internal static string GetPath()
        {
            string projectRoot =
                Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return string.IsNullOrEmpty(projectRoot)
                ? RelativePath
                : Path.Combine(projectRoot, RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }

        internal static bool TrySave(
            string savedRulesJson,
            string savedGeneratedCodeFolder,
            string draftRulesJson,
            string draftGeneratedCodeFolder,
            out string? error)
        {
            error = null;

            try
            {
                string path = GetPath();
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = path + ".tmp";
                File.WriteAllText(
                    tempPath,
                    ToJson(
                        savedRulesJson,
                        savedGeneratedCodeFolder,
                        draftRulesJson,
                        draftGeneratedCodeFolder));
                File.Copy(tempPath, path, true);
                File.Delete(tempPath);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static string ToJson(
            string savedRulesJson,
            string savedGeneratedCodeFolder,
            string draftRulesJson,
            string draftGeneratedCodeFolder)
        {
            var file = new DraftFileDto
            {
                format = Format,
                version = CurrentVersion,
                savedRulesJson = savedRulesJson,
                savedGeneratedCodeFolder = savedGeneratedCodeFolder,
                draftRulesJson = draftRulesJson,
                draftGeneratedCodeFolder = draftGeneratedCodeFolder,
                savedAtUtc = DateTime.UtcNow.ToString("o")
            };

            return JsonUtility.ToJson(file, true) + Environment.NewLine;
        }

        internal static bool TryLoad(
            out MenuRedirectDraftData? draft,
            out string? error)
        {
            draft = null;
            error = null;

            try
            {
                string path = GetPath();
                if (!File.Exists(path))
                    return false;

                return TryParse(File.ReadAllText(path), out draft, out error);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static bool TryParse(
            string? json,
            out MenuRedirectDraftData? draft,
            out string? error)
        {
            draft = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "draft file is empty";
                return false;
            }

            DraftFileDto? file;
            try
            {
                file = JsonUtility.FromJson<DraftFileDto>(json);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }

            if (file == null ||
                !string.Equals(file.format, Format, StringComparison.Ordinal))
            {
                error = "not a Menu Redirect draft";
                return false;
            }

            if (file.version != CurrentVersion)
            {
                error = "unsupported draft version: " + file.version;
                return false;
            }

            if (string.IsNullOrWhiteSpace(file.savedRulesJson) ||
                string.IsNullOrWhiteSpace(file.draftRulesJson))
            {
                error = "draft is missing rule data";
                return false;
            }

            draft = new MenuRedirectDraftData
            {
                savedRulesJson = file.savedRulesJson!,
                savedGeneratedCodeFolder = file.savedGeneratedCodeFolder ?? string.Empty,
                draftRulesJson = file.draftRulesJson!,
                draftGeneratedCodeFolder = file.draftGeneratedCodeFolder ?? string.Empty,
                savedAtUtc = file.savedAtUtc ?? string.Empty
            };

            return true;
        }

        internal static void Delete()
        {
            try
            {
                string path = GetPath();
                if (File.Exists(path))
                    File.Delete(path);

                string tempPath = path + ".tmp";
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // 草稿是尽力而为的保护；删除失败不应影响设置页或应用流程。
            }
        }
    }
}
