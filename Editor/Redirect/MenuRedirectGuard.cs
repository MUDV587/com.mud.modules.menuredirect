using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;
using Assembly = System.Reflection.Assembly;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 内置菜单保护：Unity 自带的菜单（File / Edit / Assets / GameObject / Window 等顶级菜单，
    /// 以及它们下面由 Unity 程序集声明的子项）**不允许**被移除或搬移；
    /// 只有自定义菜单项与第三方插件菜单项可以被移除/搬移。
    /// 注意：Tools 是第三方/自定义工具常用的根菜单，**不在**保护列表中。
    /// 命中保护时一律拒绝或跳过，并给出可本地化的说明。
    /// </summary>
    internal static class MenuRedirectGuard
    {
        /// <summary>
        /// Unity 编辑器主菜单的内置顶级菜单名。
        /// 这些菜单由 Unity 自身维护，整体移除会破坏编辑器（例如 File/Save、Edit/Undo）。
        /// </summary>
        private static readonly HashSet<string> BuiltInRootMenus = new HashSet<string>(StringComparer.Ordinal)
        {
            "File",
            "Edit",
            "Assets",
            "GameObject",
            "Component",
            "Window",
            "Help",
            "Jobs",
            "Analyze",
            "Extensions",
            "Services",
            "Internal",
        };

        /// <summary>是否为 Unity 内置的顶级菜单（整体不可移除/搬移）。</summary>
        public static bool IsBuiltInRootMenu(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // 只有"没有子级"的路径才算顶级菜单本身（File 是内置；Tools/Foo 不是）。
            if (path.IndexOf('/') >= 0)
                return false;

            return BuiltInRootMenus.Contains(path);
        }

        /// <summary>
        /// 判断某个路径是否属于 Unity 内置菜单（不可移除/搬移）。
        /// 命中时 reason 给出可读的说明文案。
        /// </summary>
        public static bool IsBuiltIn(string path, MenuRegistry registry, out MenuRedirectText? reason)
        {
            reason = null;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (IsBuiltInRootMenu(path))
            {
                reason = L.Text("guard.root", path);
                return true;
            }

            string? unityOwner = registry?.FindUnityOwner(path);

            if (!string.IsNullOrEmpty(unityOwner))
            {
                reason = L.Text("guard.item", path, unityOwner!);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 查找某个路径下由 Unity 程序集声明的条目。
        /// 容器本身可能没有 [MenuItem]，但只要有 Unity 子项，就不能整体移除/搬移该容器。
        /// </summary>
        public static bool TryFindBuiltInDescendant(
            string path,
            MenuRegistry registry,
            out string? childPath,
            out string? ownerAssembly)
        {
            childPath = null;
            ownerAssembly = null;

            return registry != null &&
                   registry.TryGetUnityOwnedDescendant(path, out childPath, out ownerAssembly);
        }

        /// <summary>
        /// 判断一个嵌套容器是否因包含 Unity 内置子项而整体受保护。
        /// 命中时 reason 给出容器与具体内置子项的说明。
        /// </summary>
        public static bool IsProtectedContainer(
            string path,
            MenuRegistry registry,
            out MenuRedirectText? reason)
        {
            reason = null;

            if (!TryFindBuiltInDescendant(path, registry, out string? childPath, out string? ownerAssembly))
                return false;

            reason = L.Text(
                "guard.container",
                path,
                childPath ?? path,
                ownerAssembly ?? "Unity");
            return true;
        }

        /// <summary>
        /// 判断程序集名是否属于 Unity 自身（内置菜单 / Unity 官方包）。
        /// 仅作为无法解析程序集来源时的回退规则。
        /// </summary>
        public static bool IsUnityAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
                return false;

            if (assemblyName.StartsWith("UnityEditor", StringComparison.Ordinal))
                return true;

            if (assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal))
                return true;

            // Unity 官方包程序集：Unity.TextMeshPro / Unity.RenderPipelines.* / Unity.ScriptableBuildPipeline ...
            if (assemblyName.StartsWith("Unity.", StringComparison.Ordinal))
                return true;

            return string.Equals(assemblyName, "Unity", StringComparison.Ordinal);
        }

        /// <summary>
        /// 判断已加载程序集的真实归属：Unity 编辑器安装、com.unity.* 官方包、
        /// 第三方包或工程程序集。优先使用程序集位置、PackageInfo 和 asmdef 路径，
        /// 避免仅凭程序集名（例如第三方故意命名为 Unity.*）误判。
        /// asmref 包含的脚本跟随目标程序集归属。
        /// </summary>
        internal static bool IsUnityAssembly(Assembly assembly, string assemblyName)
        {
            return IsUnityOwnedAssembly(
                assemblyName,
                GetAssemblyLocation(assembly),
                GetAssemblyDefinitionPath(assemblyName),
                GetPackageName(assembly),
                GetProjectRoot(),
                GetUnityEditorRoot(),
                GetUnityContentsRoot());
        }

        /// <summary>
        /// 纯判定入口，便于用合成路径覆盖 Unity 安装、包缓存、工程和 asmdef 场景。
        /// </summary>
        internal static bool IsUnityOwnedAssembly(
            string assemblyName,
            string? assemblyLocation,
            string? assemblyDefinitionPath,
            string? packageName,
            string? projectRoot,
            string? unityEditorRoot,
            string? unityContentsRoot)
        {
            string location = NormalizePath(assemblyLocation);
            string definitionPath = NormalizePath(assemblyDefinitionPath);
            string package = packageName?.Trim() ?? string.Empty;

            if (IsPathUnder(location, unityEditorRoot) ||
                IsPathUnder(location, unityContentsRoot))
            {
                return true;
            }

            // PackageInfo 能直接识别包来源；非 com.unity.* 包即使程序集名以 Unity 开头也不算 Unity。
            if (package.Length > 0)
                return IsUnityPackageName(package);

            bool? assetOwned = ClassifyAssetPath(definitionPath, projectRoot);
            if (assetOwned.HasValue)
                return assetOwned.Value;

            if (location.Length > 0)
            {
                assetOwned = ClassifyAssetPath(location, projectRoot);
                if (assetOwned.HasValue)
                    return assetOwned.Value;

                // 工程内没有 asmdef / PackageInfo 的程序集（Assembly-CSharp、Assets 插件 DLL）属于工程。
                if (IsPathUnder(location, projectRoot))
                    return false;
            }

            // 来源无法解析（例如内存程序集）时保留旧规则，避免漏保护 Unity 内置菜单。
            return IsUnityAssembly(assemblyName);
        }

        private static bool? ClassifyAssetPath(string path, string? projectRoot)
        {
            if (path.Length == 0)
                return null;

            string candidate = ToProjectRelativePath(path, projectRoot);
            if (candidate.Length == 0)
                candidate = path;

            if (candidate.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (candidate.StartsWith("Packages/com.unity.", StringComparison.OrdinalIgnoreCase))
                return true;

            if (candidate.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (candidate.StartsWith(
                    "Library/PackageCache/com.unity.",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (candidate.StartsWith("Library/PackageCache/", StringComparison.OrdinalIgnoreCase))
                return false;

            return null;
        }

        private static bool IsUnityPackageName(string packageName)
        {
            return string.Equals(packageName, "com.unity", StringComparison.OrdinalIgnoreCase) ||
                   packageName.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetAssemblyLocation(Assembly assembly)
        {
            try
            {
                return assembly.Location ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetAssemblyDefinitionPath(string assemblyName)
        {
            try
            {
                return CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                           assemblyName) ??
                       string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetPackageName(Assembly assembly)
        {
            try
            {
                UnityEditor.PackageManager.PackageInfo package =
                    UnityEditor.PackageManager.PackageInfo.FindForAssembly(assembly);
                return package?.name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetProjectRoot()
        {
            try
            {
                return Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetUnityEditorRoot()
        {
            try
            {
                return Path.GetDirectoryName(EditorApplication.applicationPath) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetUnityContentsRoot()
        {
            string editorRoot = GetUnityEditorRoot();
            if (editorRoot.Length == 0)
                return string.Empty;

            try
            {
                return Path.GetDirectoryName(editorRoot) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsPathUnder(string path, string? root)
        {
            string normalizedRoot = NormalizePath(root);
            if (path.Length == 0 || normalizedRoot.Length == 0)
                return false;

            return path.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToProjectRelativePath(string path, string? projectRoot)
        {
            string root = NormalizePath(projectRoot);
            if (root.Length == 0 || !IsPathUnder(path, root))
                return string.Empty;

            return path.Substring(root.Length).TrimStart('/');
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path!.Trim().Replace('\\', '/').TrimEnd('/');
        }
    }
}
