using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 对 Unity 内部菜单 API 的唯一反射收口。任何一步失败都只降级、不影响重定向之外的编辑器功能。
    ///
    /// 关键约定（与已验证可用的移除方式保持一致）：
    /// 1. 移除条目：<c>UnityEditor.Menu.RemoveMenuItem(string)</c>（internal，需反射）。
    /// 2. 刷新界面：<c>UnityEditor.EditorUtility.Internal_UpdateAllMenus()</c>。
    /// 3. **禁止**使用 <c>Menu.RebuildAllMenus()</c>：它会按 [MenuItem] 特性把所有条目重新注册一遍，
    ///    把刚移除的菜单原样恢复 —— 这正是"隐藏完全不生效"的根因。
    /// 4. <c>RemoveMenuItem</c> 在多数 Unity 版本里返回 void，因此"调用未抛异常"即视为成功；
    ///    个别版本返回 bool 时按其返回值判定。
    /// </summary>
    internal static class MenuRedirectBridge
    {
        private static bool _probed;
        private static MethodInfo? _removeMenuItem;
        private static MethodInfo? _updateAllMenus;
        private static MethodInfo? _menuItemExists;

        public static MenuRedirectText? UnavailableReason { get; private set; }

        /// <summary>能否移除菜单条目。</summary>
        public static bool Available
        {
            get
            {
                Probe();
                return _removeMenuItem != null;
            }
        }

        /// <summary>能否强制刷新编辑器菜单 UI。</summary>
        public static bool CanRefreshUi
        {
            get
            {
                Probe();
                return _updateAllMenus != null;
            }
        }

        /// <summary>能否检测条目是否仍存在（用于复核隐藏是否真的生效）。</summary>
        public static bool CanCheckExists
        {
            get
            {
                Probe();
                return _menuItemExists != null;
            }
        }

        /// <summary>
        /// 移除一个菜单条目。返回 true 表示移除调用已成功执行。
        /// （不抛异常即成功；返回 bool 的版本按其返回值判定。）
        /// </summary>
        public static bool RemoveMenuItem(string menuPath)
        {
            Probe();

            if (_removeMenuItem == null || string.IsNullOrWhiteSpace(menuPath))
                return false;

            try
            {
                object? result = _removeMenuItem.Invoke(null, new object[] { menuPath });

                // 多数版本返回 void（Invoke 结果为 null）→ 视为成功；返回 bool 的版本按返回值判定。
                if (result is bool flag)
                    return flag;

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(L.F("bridge.removeFailed", menuPath, exception.Message));
                return false;
            }
        }

        /// <summary>
        /// 刷新编辑器菜单 UI，让移除结果立刻可见。
        /// 只做界面刷新，绝不重建菜单注册表（重建会把被移除的条目恢复）。
        /// </summary>
        public static bool RefreshMenus()
        {
            Probe();

            if (_updateAllMenus == null)
                return false;

            try
            {
                _updateAllMenus.Invoke(null, null);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(L.F("bridge.refreshFailed", exception.Message));
                return false;
            }
        }

        /// <summary>检测菜单条目当前是否仍存在；无法检测时返回 true（视为未消失，不阻断流程）。</summary>
        public static bool MenuItemExists(string menuPath)
        {
            Probe();

            if (_menuItemExists == null || string.IsNullOrWhiteSpace(menuPath))
                return true;

            try
            {
                object? result = _menuItemExists.Invoke(null, new object[] { menuPath });
                return result is bool && (bool)result;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(L.F("bridge.existsFailed", menuPath, exception.Message));
                return true;
            }
        }

        private static void Probe()
        {
            if (_probed)
                return;

            _probed = true;

            try
            {
                // 使用与验证时相同的类型；方法本身是 internal，仍需反射调用。
                // 这里优先直接引用类型，找不到时再按程序集名兜底，兼容未来 Unity 版本。
                Type? menuType = typeof(UnityEditor.Menu);

                if (menuType == null)
                {
                    menuType = typeof(EditorApplication).Assembly.GetType("UnityEditor.Menu", false);
                }

                if (menuType == null)
                {
                    menuType = Type.GetType("UnityEditor.Menu, UnityEditor", false);
                }

                if (menuType == null)
                {
                    UnavailableReason = L.Text("bridge.menuTypeMissing");
                    return;
                }

                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                _removeMenuItem = menuType.GetMethod("RemoveMenuItem", flags, null, new[] { typeof(string) }, null);
                _menuItemExists = menuType.GetMethod("MenuItemExists", flags, null, new[] { typeof(string) }, null);

                // 刷新 UI：EditorUtility.Internal_UpdateAllMenus()（internal static）。
                _updateAllMenus = typeof(EditorUtility).GetMethod(
                    "Internal_UpdateAllMenus",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                if (_removeMenuItem == null)
                    UnavailableReason = L.Text("bridge.removeMissing");

                if (_updateAllMenus == null)
                    Debug.LogWarning(L.T("bridge.refreshMissing"));
            }
            catch (Exception exception)
            {
                UnavailableReason = L.Text("bridge.probeFailed");
                Debug.LogWarning(exception.Message);
            }
        }
    }
}
