using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 移除包之前的清理入口。包被移除后其代码不再运行，因此必须在移除前手动清理；
    /// 这里只删除当前/上次生成路径中带本工具生成头的包装文件，保留规则配置。
    /// </summary>
    internal static class MenuRedirectCleanup
    {
        [MenuItem("Tools/MUD/Menu Redirect/Clean Up Generated Files", false, 100)]
        private static void CleanUpGeneratedFiles()
        {
            bool hasConfig = File.Exists(MenuRedirectSettings.ProjectSettingsPath);
            MenuRedirectSettings? settings = hasConfig ? MenuRedirectSettings.instance : null;
            List<string> candidates = CollectCandidatePaths(settings);

            var ownedFiles = new List<string>();
            var keptFiles = new List<string>();

            for (int i = 0; i < candidates.Count; i++)
            {
                string assetPath = candidates[i];
                if (!File.Exists(MenuRedirectEngine.ToFullPath(assetPath)))
                    continue;

                if (IsOwnedGeneratedFile(assetPath))
                    ownedFiles.Add(assetPath);
                else
                    keptFiles.Add(assetPath);
            }

            if (ownedFiles.Count == 0)
            {
                string nothingBody = L.T("cleanup.nothing.body");
                if (keptFiles.Count > 0)
                {
                    nothingBody += "\n\n" + L.F(
                        "cleanup.kept.body",
                        FormatPathList(keptFiles));
                }

                EditorUtility.DisplayDialog(
                    L.T("cleanup.nothing.title"),
                    nothingBody,
                    L.T("cleanup.result.ok"));
                return;
            }

            string confirmation = L.F(
                "cleanup.confirm.body",
                FormatPathList(ownedFiles));

            confirmation += "\n\n" + (hasConfig
                ? L.F("cleanup.confirm.configKept", MenuRedirectSettings.ProjectSettingsPath)
                : L.T("cleanup.confirm.noConfig"));

            if (keptFiles.Count > 0)
            {
                confirmation += "\n\n" + L.F(
                    "cleanup.kept.body",
                    FormatPathList(keptFiles));
            }

            if (!EditorUtility.DisplayDialog(
                    L.T("cleanup.confirm.title"),
                    confirmation,
                    L.T("cleanup.confirm.ok"),
                    L.T("cleanup.confirm.cancel")))
            {
                return;
            }

            var deletedFiles = new List<string>();
            var failures = new List<string>();

            for (int i = 0; i < ownedFiles.Count; i++)
            {
                string assetPath = ownedFiles[i];
                if (TryDeleteOwnedGeneratedFile(assetPath, out string error))
                    deletedFiles.Add(assetPath);
                else
                    failures.Add("• " + assetPath + " — " + error);
            }

            string result = L.F(
                "cleanup.result.body",
                deletedFiles.Count,
                FormatPathList(deletedFiles));

            if (hasConfig)
            {
                result += "\n\n" + L.F(
                    "cleanup.confirm.configKept",
                    MenuRedirectSettings.ProjectSettingsPath);
            }

            if (failures.Count > 0)
            {
                result += "\n\n" + L.F(
                    "cleanup.result.failures",
                    failures.Count,
                    string.Join("\n", failures));
            }

            // 先显示结果再刷新资产库：删除 .cs 会触发编译和域重载，
            // 刷新放在对话框之后可避免用户还没看到结果就被重载打断。
            EditorUtility.DisplayDialog(
                L.T("cleanup.result.title"),
                result,
                L.T("cleanup.result.ok"));

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 当前配置路径 + 上次生成路径；没有配置时回退到默认路径。
        /// 只返回路径字符串，不读取或创建配置文件。
        /// </summary>
        internal static List<string> CollectCandidatePaths(MenuRedirectSettings? settings)
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!ReferenceEquals(settings, null))
            {
                AddCandidate(paths, seen, MenuRedirectEngine.GetGeneratedPath(settings));
                AddCandidate(paths, seen, settings!.lastGeneratedCodePath);
            }
            else
            {
                AddCandidate(paths, seen, MenuRedirectSettings.DefaultGeneratedCodePath);
            }

            return paths;
        }

        /// <summary>文件存在、位于 Assets 下、文件名正确且首行为本工具生成头时才算可删除。</summary>
        internal static bool IsOwnedGeneratedFile(string assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            if (!MenuRedirectEngine.IsAssetPath(normalized) ||
                !HasGeneratedFileName(normalized))
            {
                return false;
            }

            string fullPath = MenuRedirectEngine.ToFullPath(normalized);
            return File.Exists(fullPath) && MenuRedirectEngine.IsGeneratedCodeFile(fullPath);
        }

        /// <summary>
        /// 删除前再次确认生成头，避免文件在确认框期间被替换成用户文件。
        /// 文件已不存在视为成功；返回 false 时 error 为可直接显示的本地化文本。
        /// </summary>
        internal static bool TryDeleteOwnedGeneratedFile(string assetPath, out string error)
        {
            error = string.Empty;
            string normalized = NormalizeAssetPath(assetPath);

            if (!MenuRedirectEngine.IsAssetPath(normalized))
            {
                error = L.F("cleanup.error.notAsset", assetPath ?? string.Empty);
                return false;
            }

            if (!HasGeneratedFileName(normalized))
            {
                error = L.F("cleanup.error.unexpectedFileName", normalized);
                return false;
            }

            string fullPath = MenuRedirectEngine.ToFullPath(normalized);
            if (!File.Exists(fullPath))
                return true;

            if (!MenuRedirectEngine.IsGeneratedCodeFile(fullPath))
            {
                error = L.F("cleanup.error.notGenerated", normalized);
                return false;
            }

            try
            {
                bool deleted = AssetDatabase.DeleteAsset(normalized);
                if (!deleted && File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    string metaPath = fullPath + ".meta";
                    if (File.Exists(metaPath))
                        File.Delete(metaPath);

                    deleted = !File.Exists(fullPath);
                }

                if (deleted)
                    return true;

                error = L.F("cleanup.error.deleteFailed", normalized);
                return false;
            }
            catch (Exception exception)
            {
                error = L.F(
                    "cleanup.error.deleteFailed",
                    normalized + " (" + exception.Message + ")");
                return false;
            }
        }

        private static void AddCandidate(
            List<string> paths,
            HashSet<string> seen,
            string? assetPath)
        {
            string normalized = NormalizeAssetPath(assetPath);
            if (normalized.Length == 0 || !seen.Add(normalized))
                return;

            paths.Add(normalized);
        }

        private static string NormalizeAssetPath(string? assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return string.Empty;

            return assetPath!.Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static bool HasGeneratedFileName(string assetPath)
        {
            int slash = assetPath.LastIndexOf('/');
            string fileName = slash >= 0 ? assetPath.Substring(slash + 1) : assetPath;
            return string.Equals(
                fileName,
                MenuRedirectSettings.GeneratedCodeFileName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatPathList(List<string> paths)
        {
            var lines = new List<string>(paths.Count);
            for (int i = 0; i < paths.Count; i++)
                lines.Add("• " + paths[i]);

            return string.Join("\n", lines);
        }
    }
}
