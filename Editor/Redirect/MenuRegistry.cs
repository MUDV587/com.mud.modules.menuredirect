using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// 菜单注册表：扫描所有已加载程序集里的 [MenuItem] 声明，
    /// 记录"路径 → 声明程序集 / 声明类型 / 方法 / validate / priority"，用于：
    /// 1. 找到某个父级菜单下的全部子条目（分支搬移 / 隐藏时逐个处理）。
    /// 2. 判断某个条目是不是 Unity 自带的（内置条目不允许被移除或搬移）。
    /// 3. 让重定向包装菜单通过反射执行原始 [MenuItem] 方法。
    ///
    /// 自己生成的包装代码（MUD.MenuRedirects 命名空间）会被排除，
    /// 否则重复应用规则会把上一轮生成的代理当成"原菜单"再搬一次。
    /// </summary>
    internal sealed class MenuRegistry
    {
        /// <summary>自身生成代码的命名空间前缀（用于排除自己生成的代理菜单）。</summary>
        private const string SelfNamespacePrefix = "MUD.MenuRedirects";

        /// <summary>MenuItem 快捷键后缀里可能出现的修饰符。</summary>
        private static readonly char[] ShortcutModifiers = { '%', '#', '&', '^' };

        private readonly Dictionary<string, List<MenuRegistration>> _byPath;

        private MenuRegistry(Dictionary<string, List<MenuRegistration>> byPath)
        {
            _byPath = byPath;
        }

        /// <summary>所有已注册的菜单路径（不含自身生成的代理）。</summary>
        public IEnumerable<string> Paths => _byPath.Keys;

        /// <summary>路径是否已注册（[MenuItem] 特性层面）。</summary>
        public bool Contains(string path)
        {
            string key = NormalizePath(path);
            return key.Length > 0 && _byPath.ContainsKey(key);
        }

        /// <summary>所有已注册菜单路径（不含自身生成的代理），按字典序排序。</summary>
        public List<string> GetAllPaths()
        {
            var paths = new List<string>(_byPath.Keys);
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>
        /// 该路径是否按分支处理：顶级路径始终是分支；中间路径只有在自身没有
        /// [MenuItem] 注册、但存在已注册子项时才是分支。自身已注册的中间路径按叶子处理，
        /// 避免把“菜单项 + 子菜单”的容器误当成整棵子树。
        /// </summary>
        public bool IsBranchPath(string path)
        {
            string key = NormalizePath(path);

            if (key.Length == 0)
                return false;

            if (key.IndexOf('/') < 0)
                return true;

            if (_byPath.ContainsKey(key))
                return false;

            string prefix = key + "/";

            foreach (string registered in _byPath.Keys)
            {
                if (registered.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 找出该路径下由 Unity 程序集声明的注册（内置条目）。
        /// 返回 null 表示没有内置声明（自定义 / 第三方插件条目）。
        /// </summary>
        public string? FindUnityOwner(string path)
        {
            string key = NormalizePath(path);

            if (key.Length == 0 || !_byPath.TryGetValue(key, out List<MenuRegistration>? list))
                return null;

            foreach (MenuRegistration registration in list)
            {
                if (registration.IsUnityOwned)
                    return registration.AssemblyName;
            }

            return null;
        }

        /// <summary>
        /// 把显示路径转换成实际可传给 RemoveMenuItem 的原始路径。
        /// Unity 的菜单显示路径会去掉助记符下划线，但 RemoveMenuItem 对带下划线的原始路径更可靠；
        /// 因此注册表同时保存了特性里的原始路径。父级容器没有自己的 [MenuItem] 时，
        /// 从任一子项的原始路径按层级数截取前缀。
        /// </summary>
        public string GetRemovalPath(string path)
        {
            string key = NormalizePath(path);

            if (key.Length == 0)
                return path;

            if (_byPath.TryGetValue(key, out List<MenuRegistration>? registrations) &&
                registrations.Count > 0)
            {
                return registrations[0].RawPath;
            }

            string prefix = key + "/";

            foreach (KeyValuePair<string, List<MenuRegistration>> pair in _byPath)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                foreach (MenuRegistration registration in pair.Value)
                {
                    string? rawPrefix = DeriveRawPrefix(registration.RawPath, key);

                    if (!string.IsNullOrEmpty(rawPrefix))
                        return rawPrefix!;
                }
            }

            // 动态注册、注册表里没有原始路径时退回显示路径。
            return path;
        }

        private static string? DeriveRawPrefix(string rawPath, string displayPrefix)
        {
            if (string.IsNullOrWhiteSpace(rawPath) || string.IsNullOrWhiteSpace(displayPrefix))
                return null;

            int segmentCount = displayPrefix.Split('/').Length;
            string[] rawSegments = rawPath.Split('/');

            if (rawSegments.Length <= segmentCount)
                return null;

            return string.Join("/", rawSegments, 0, segmentCount);
        }

        /// <summary>某个父级路径下的全部子条目路径（含多级子项），按字典序排序。</summary>
        public List<string> ChildrenOf(string root)
        {
            var children = new List<string>();

            string key = NormalizePath(root);

            if (key.Length == 0)
                return children;

            string prefix = key + "/";

            foreach (string path in _byPath.Keys)
            {
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                    children.Add(path);
            }

            children.Sort(StringComparer.Ordinal);
            return children;
        }

        /// <summary>找到某个路径对应的全部可执行原始方法（排除 validate 声明）。</summary>
        public IReadOnlyList<MenuRegistration> GetExecutables(string path)
        {
            string key = NormalizePath(path);
            var result = new List<MenuRegistration>();

            if (key.Length == 0 || !_byPath.TryGetValue(key, out List<MenuRegistration>? list))
                return result;

            foreach (MenuRegistration candidate in list)
            {
                if (!candidate.IsValidate && candidate.IsInvocable)
                    result.Add(candidate);
            }

            return result;
        }

        /// <summary>某个路径声明的可执行方法数量；用于检测重复菜单声明。</summary>
        public int GetExecutableCount(string path)
        {
            return GetExecutables(path).Count;
        }

        /// <summary>找到某个路径对应的可执行原始方法（排除 validate 声明）。</summary>
        public bool TryGetExecutable(string path, out MenuRegistration? registration)
        {
            registration = null;

            IReadOnlyList<MenuRegistration> executables = GetExecutables(path);
            if (executables.Count == 0)
                return false;

            registration = executables[0];
            return true;
        }

        /// <summary>
        /// 查找某个分支下是否包含 Unity 程序集声明的菜单。
        /// 用于把"嵌套在自定义父级里的内置菜单"整体视为受保护容器。
        /// </summary>
        public bool TryGetUnityOwnedDescendant(
            string path,
            out string? childPath,
            out string? ownerAssembly)
        {
            childPath = null;
            ownerAssembly = null;

            string key = NormalizePath(path);

            if (key.Length == 0)
                return false;

            string prefix = key + "/";

            foreach (KeyValuePair<string, List<MenuRegistration>> pair in _byPath)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                foreach (MenuRegistration registration in pair.Value)
                {
                    if (!registration.IsUnityOwned)
                        continue;

                    childPath = pair.Key;
                    ownerAssembly = registration.AssemblyName;
                    return true;
                }
            }

            return false;
        }

        /// <summary>该路径是否存在 validate 声明。</summary>
        public bool HasValidate(string path)
        {
            string key = NormalizePath(path);

            if (key.Length == 0 || !_byPath.TryGetValue(key, out List<MenuRegistration>? list))
                return false;

            foreach (MenuRegistration registration in list)
            {
                if (registration.IsValidate && registration.IsInvocable)
                    return true;
            }

            return false;
        }

        /// <summary>该路径的全部 validate 声明。</summary>
        public IReadOnlyList<MenuRegistration> GetValidateMethods(string path)
        {
            var result = new List<MenuRegistration>();
            string key = NormalizePath(path);

            if (key.Length == 0 || !_byPath.TryGetValue(key, out List<MenuRegistration>? list))
                return result;

            foreach (MenuRegistration registration in list)
            {
                if (registration.IsValidate && registration.IsInvocable)
                    result.Add(registration);
            }

            return result;
        }

        /// <summary>扫描所有已加载程序集，建立注册表。</summary>
        public static MenuRegistry Build()
        {
            const BindingFlags methodFlags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            var byPath = new Dictionary<string, List<MenuRegistration>>(StringComparer.Ordinal);

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                    continue;

                string assemblyName = assembly.GetName().Name ?? string.Empty;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    var loadedTypes = new List<Type>();

                    foreach (Type? loadedType in exception.Types)
                    {
                        if (loadedType != null)
                            loadedTypes.Add(loadedType);
                    }

                    types = loadedTypes.ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    // 跳过自身生成的代理菜单，否则重复应用规则会把上一轮生成的代理当成"原菜单"。
                    if ((type.FullName ?? type.Name).StartsWith(SelfNamespacePrefix, StringComparison.Ordinal))
                        continue;

                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(methodFlags);
                    }
                    catch
                    {
                        continue;
                    }

                    bool ownershipResolved = false;
                    bool isUnityOwned = false;

                    foreach (MethodInfo method in methods)
                    {
                        if (!TryGetMenuItems(method, out List<MenuItemDeclaration> declarations))
                            continue;

                        if (!ownershipResolved)
                        {
                            // 每个程序集只解析一次归属：程序集位置、PackageInfo、asmdef 路径。
                            isUnityOwned = MenuRedirectGuard.IsUnityAssembly(assembly, assemblyName);
                            ownershipResolved = true;
                        }

                        foreach (MenuItemDeclaration declaration in declarations)
                        {
                            if (!byPath.TryGetValue(declaration.DisplayPath, out List<MenuRegistration>? list))
                            {
                                list = new List<MenuRegistration>();
                                byPath[declaration.DisplayPath] = list;
                            }

                            list.Add(new MenuRegistration(
                                declaration.DisplayPath,
                                declaration.RawPath,
                                assemblyName,
                                type.FullName ?? type.Name,
                                method,
                                declaration.IsValidate,
                                declaration.Priority,
                                isUnityOwned));
                        }
                    }
                }
            }

            return new MenuRegistry(byPath);
        }

        /// <summary>
        /// 把 MenuItem 特性上的原始路径规范化为菜单里显示的路径：
        /// 去掉末尾的快捷键（如 <c>#%F12</c>）与助记符下划线（<c>_</c>）。
        /// Unity 的 RemoveMenuItem/MenuItemExists 对带快捷键和不带快捷键的路径都能识别；
        /// 但带助记符下划线的菜单必须使用原始路径，因此注册表另存 RawPath，
        /// 移除时由 <see cref="GetRemovalPath"/> 取用。
        /// </summary>
        internal static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string result = path!.Trim();

            int space = result.LastIndexOf(' ');
            if (space > 0 && space < result.Length - 1)
            {
                string suffix = result.Substring(space + 1);
                if (IsShortcutToken(suffix))
                    result = result.Substring(0, space).TrimEnd();
            }

            // Unity 用下划线标记菜单助记符，实际显示路径中不包含它。
            return result.Replace("_", string.Empty);
        }

        private static bool IsShortcutToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            // Unity 的快捷键修饰符：% Ctrl/Cmd、# Shift、& Alt、^ Ctrl(Win)。
            if (token.IndexOfAny(ShortcutModifiers) >= 0)
                return true;

            // 无修饰符的快捷键（例如 "_g"）必须以 _ 开头。
            return token[0] == '_' && token.Length > 1;
        }

        /// <summary>
        /// 提取 MenuItem 原始路径末尾的快捷键后缀（如 <c>_g</c>、<c>#%F12</c>）；
        /// 没有快捷键时返回 null。识别规则与 <see cref="NormalizePath"/> 的剥离规则一致。
        /// </summary>
        internal static string? ExtractShortcut(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            string result = rawPath!.Trim();
            int space = result.LastIndexOf(' ');
            if (space <= 0 || space >= result.Length - 1)
                return null;

            string suffix = result.Substring(space + 1);
            return IsShortcutToken(suffix) ? suffix : null;
        }

        /// <summary>
        /// 按规范化路径查注册表条目原始路径中的快捷键后缀；
        /// 条目不存在或没有快捷键时返回 null。供生成包装代码时提示"快捷键不会自动复制"。
        /// </summary>
        internal string? FindShortcut(string? path)
        {
            string key = NormalizePath(path);
            if (key.Length == 0 ||
                !_byPath.TryGetValue(key, out List<MenuRegistration>? list) ||
                list.Count == 0)
            {
                return null;
            }

            return ExtractShortcut(list[0].RawPath);
        }

        /// <summary>
        /// 提取方法上所有 MenuItem 特性的路径、validate、priority。
        /// 一个方法可以声明多个菜单路径，全部都要进入注册表。
        /// 不直接引用 Unity 的特性类型：不同版本里特性类名可能是 MenuItem 或 MenuItemAttribute，
        /// 成员也可能是属性或字段，用反射统一兼容。
        /// </summary>
        private static bool TryGetMenuItems(
            MethodInfo method,
            out List<MenuItemDeclaration> declarations)
        {
            declarations = new List<MenuItemDeclaration>();

            object[] attributes;
            try
            {
                attributes = method.GetCustomAttributes(false);
            }
            catch
            {
                return false;
            }

            foreach (object attributeObject in attributes)
            {
                if (!(attributeObject is Attribute attribute))
                    continue;

                Type type = attribute.GetType();

                if (type.Namespace != "UnityEditor")
                    continue;

                if (type.Name != "MenuItem" && type.Name != "MenuItemAttribute")
                    continue;

                string? menuItemPath = ReadValue(attribute, type, "menuItem") as string;

                if (string.IsNullOrWhiteSpace(menuItemPath))
                    continue;

                string rawPath = menuItemPath!.Trim();
                string displayPath = NormalizePath(rawPath);

                if (displayPath.Length == 0)
                    continue;

                bool validate = false;
                object? validateValue = ReadValue(attribute, type, "validate");
                if (validateValue is bool validateFlag)
                    validate = validateFlag;

                int priority = 0;
                object? priorityValue = ReadValue(attribute, type, "priority");
                if (priorityValue is int priorityNumber)
                    priority = priorityNumber;
                else if (priorityValue is short shortPriority)
                    priority = shortPriority;

                declarations.Add(new MenuItemDeclaration(
                    displayPath,
                    rawPath,
                    validate,
                    priority));
            }

            return declarations.Count > 0;
        }

        private readonly struct MenuItemDeclaration
        {
            public MenuItemDeclaration(
                string displayPath,
                string rawPath,
                bool isValidate,
                int priority)
            {
                DisplayPath = displayPath;
                RawPath = rawPath;
                IsValidate = isValidate;
                Priority = priority;
            }

            public string DisplayPath { get; }
            public string RawPath { get; }
            public bool IsValidate { get; }
            public int Priority { get; }
        }

        private static object? ReadValue(Attribute attribute, Type type, string memberName)
        {
            try
            {
                PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                    return property.GetValue(attribute);
            }
            catch
            {
                // 继续尝试字段。
            }

            try
            {
                FieldInfo? field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                    return field.GetValue(attribute);
            }
            catch
            {
                // 忽略，按默认值处理。
            }

            return null;
        }

        /// <summary>一条 [MenuItem] 声明。</summary>
        internal sealed class MenuRegistration
        {
            public MenuRegistration(
                string path,
                string rawPath,
                string assemblyName,
                string typeFullName,
                MethodInfo method,
                bool isValidate,
                int priority,
                bool isUnityOwned)
            {
                Path = path;
                RawPath = rawPath;
                AssemblyName = assemblyName;
                TypeFullName = typeFullName;
                Method = method;
                IsValidate = isValidate;
                Priority = priority;
                IsUnityOwned = isUnityOwned;

                ParameterInfo[] parameters = method.GetParameters();
                IsInvocable = method.IsStatic &&
                              (parameters.Length == 0 ||
                               (parameters.Length == 1 && parameters[0].ParameterType == typeof(MenuCommand)));
            }

            /// <summary>菜单中显示的路径（已去快捷键、助记符下划线）。</summary>
            public string Path { get; }

            /// <summary>[MenuItem] 特性里的原始路径（保留助记符下划线，可能带快捷键）。</summary>
            public string RawPath { get; }

            public string AssemblyName { get; }
            public string TypeFullName { get; }
            public MethodInfo Method { get; }
            public bool IsValidate { get; }
            public int Priority { get; }
            public bool IsInvocable { get; }

            /// <summary>是否由 Unity 自己的程序集声明（内置菜单）。</summary>
            public bool IsUnityOwned { get; }
        }
    }
}
