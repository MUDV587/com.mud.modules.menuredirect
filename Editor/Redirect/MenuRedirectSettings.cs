using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 重定向规则清单。项目唯一配置，存放于 ProjectSettings/MenuRedirect.asset，
    /// 由 Unity 的 ScriptableSingleton 管理，可提交进版本库。
    /// </summary>
    [FilePath("ProjectSettings/MenuRedirect.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class MenuRedirectSettings : ScriptableSingleton<MenuRedirectSettings>
    {
        public const string ProjectSettingsPath = "ProjectSettings/MenuRedirect.asset";
        public const string DefaultGeneratedCodeFolder = "Assets/MenuRedirects/Editor";
        public const string GeneratedCodeFileName = "MenuRedirects.g.cs";
        public const string DefaultGeneratedCodePath =
            DefaultGeneratedCodeFolder + "/" + GeneratedCodeFileName;

        public List<MenuRedirectRule> rules = new List<MenuRedirectRule>();

        /// <summary>包装代码输出目录；必须位于 Assets 下并包含 Editor 目录。</summary>
        public string generatedCodeFolder = DefaultGeneratedCodeFolder;

        /// <summary>
        /// 上一次实际写入的包装代码路径。用于切换目录时清理旧文件，
        /// 不参与界面编辑；旧配置没有该字段时回退到默认路径。
        /// </summary>
        [HideInInspector]
        public string lastGeneratedCodePath = DefaultGeneratedCodePath;

        /// <summary>把当前规则写入 ProjectSettings/MenuRedirect.asset（文本格式，便于提交版本库）。</summary>
        public void Save()
        {
            Save(true);
        }

        private void OnEnable()
        {
            if (rules == null)
                rules = new List<MenuRedirectRule>();

            if (string.IsNullOrWhiteSpace(generatedCodeFolder))
                generatedCodeFolder = DefaultGeneratedCodeFolder;

            if (string.IsNullOrWhiteSpace(lastGeneratedCodePath))
                lastGeneratedCodePath = DefaultGeneratedCodePath;
        }
    }
}
