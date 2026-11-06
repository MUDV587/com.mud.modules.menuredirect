using System.IO;
using UnityEditor;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 域重载后重放隐藏规则。包装代码会在重编译后自然生效，这里只需要重放"隐藏"部分，
    /// 让原菜单条目不会因为菜单树重建而重新出现。
    /// </summary>
    [InitializeOnLoad]
    internal static class MenuRedirectBootstrap
    {
        static MenuRedirectBootstrap()
        {
            EditorApplication.delayCall += OnEditorLoaded;
        }

        private static void OnEditorLoaded()
        {
            // 只检查磁盘文件，避免访问 ScriptableSingleton.instance 时创建默认实例。
            if (!File.Exists(MenuRedirectSettings.ProjectSettingsPath))
                return; // 用户从未点过应用/没有配置，不产生任何副作用。

            MenuRedirectSettings settings = MenuRedirectSettings.instance;

            // 评估一次规则后同时完成“包装代码是否就绪”的复核与隐藏重放，
            // 避免域重载时重复构建生成代码。包装代码尚未生成、与规则不一致或
            // 编译未完成时不隐藏原菜单，否则一旦编译失败，用户会丢失入口。
            if (!MenuRedirectEngine.TryHideOriginals(settings, out MenuRedirectText? reason))
            {
                UnityEngine.Debug.LogWarning(L.F(
                    "engine.hideDeferred",
                    reason?.Resolve() ?? "generated code is not ready"));
            }
        }
    }
}
