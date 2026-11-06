using UnityEngine;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 一条重定向规则：把 fromPath 指向的菜单命令"搬"到 toPath。
    /// 实现方式 = 隐藏 fromPath（运行时反射移除）+ 生成 toPath 的菜单代理（编译期 MenuItem 包装）。
    /// </summary>
    [System.Serializable]
    public sealed class MenuRedirectRule
    {
        [Tooltip("原菜单路径，如 Tools/Test/Build")]
        public string fromPath = string.Empty;

        [Tooltip("新菜单路径，如 Tools/MUD/Build")]
        public string toPath = string.Empty;

        [Tooltip("新菜单的显示优先级")]
        public int priority = 0;

        [Tooltip("是否启用该规则")]
        public bool enabled = true;
    }
}
