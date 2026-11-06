using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>选择器中的一行：菜单路径、可执行状态与 Unity 保护状态。</summary>
    internal sealed class MenuRedirectPickerEntry
    {
        internal string Path = string.Empty;
        internal bool IsBranch;
        internal bool IsProtected;
        internal bool IsProtectedContainer;
        internal bool HasExecutable;
        internal MenuRedirectText? ProtectedReason;
        internal MenuRedirectText? ContainerReason;

        internal string GetTooltip()
        {
            if (IsProtected)
                return ProtectedReason?.Resolve() ?? L.T("validation.source.builtIn");

            string status = IsBranch
                ? (HasExecutable
                    ? L.T("ui.picker.tooltip.branchExecutable")
                    : L.T("ui.picker.tooltip.branchHideOnly"))
                : (HasExecutable
                    ? L.T("ui.picker.tooltip.executable")
                    : L.T("ui.picker.tooltip.hideOnly"));

            if (IsProtectedContainer && ContainerReason != null)
                status += "\n" + ContainerReason.Resolve();

            return Path + "\n" + status;
        }
    }

    /// <summary>
    /// 选择器数据：把注册表路径变成可搜索的条目，并复用保护判断。
    /// 除已注册路径外，也加入所有中间容器（如 Window/Unity CLI Loop）；
    /// 动态注册的菜单不在注册表中，仍需手输。
    /// </summary>
    internal static class MenuRedirectPickerData
    {
        internal static List<MenuRedirectPickerEntry> Build(MenuRegistry registry)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);

            foreach (string path in registry.GetAllPaths())
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                string current = path;

                while (current.Length > 0)
                {
                    paths.Add(current);

                    int slash = current.LastIndexOf('/');
                    if (slash <= 0)
                        break;

                    current = current.Substring(0, slash);
                }
            }

            var entries = new List<MenuRedirectPickerEntry>(paths.Count);

            foreach (string path in paths)
            {
                bool isBranch = registry.IsBranchPath(path);
                bool isProtected = MenuRedirectGuard.IsBuiltIn(
                    path,
                    registry,
                    out MenuRedirectText? protectedReason);
                MenuRedirectText? containerReason = null;
                bool isProtectedContainer =
                    !isProtected &&
                    MenuRedirectGuard.IsProtectedContainer(
                        path,
                        registry,
                        out containerReason);

                entries.Add(new MenuRedirectPickerEntry
                {
                    Path = path,
                    IsBranch = isBranch,
                    IsProtected = isProtected,
                    IsProtectedContainer = isProtectedContainer,
                    HasExecutable = isBranch
                        ? HasExecutableBranchChild(registry, path)
                        : registry.GetExecutableCount(path) > 0,
                    ProtectedReason = protectedReason,
                    ContainerReason = containerReason
                });
            }

            entries.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.Path, right.Path));

            return entries;
        }

        /// <summary>
        /// 目标父级候选：只保留顶级路径和有子项的容器，避免把叶子菜单当成父级。
        /// 受保护条目仍保留在列表中，由选择器置灰并阻止选择。
        /// </summary>
        internal static List<MenuRedirectPickerEntry> BuildParentPaths(MenuRegistry registry)
        {
            List<MenuRedirectPickerEntry> entries = Build(registry);

            entries.RemoveAll(entry =>
                entry.Path.IndexOf('/') >= 0 &&
                registry.ChildrenOf(entry.Path).Count == 0);

            return entries;
        }

        /// <summary>
        /// 根据选中的父级生成新路径：原路径为空时只填入父级；分支源直接使用父级；
        /// 叶子源保留当前新路径的最后一段，为空时回退到原路径的最后一段。
        /// </summary>
        internal static string BuildTargetPath(
            string selectedParent,
            string sourcePath,
            string currentTargetPath,
            bool sourceIsBranch)
        {
            string parent = MenuRegistry.NormalizePath(selectedParent);

            if (parent.Length == 0)
                return string.Empty;

            string source = MenuRegistry.NormalizePath(sourcePath);

            if (source.Length == 0)
                return parent;

            if (sourceIsBranch)
                return parent;

            string leaf = GetLastSegment(MenuRegistry.NormalizePath(currentTargetPath));

            if (leaf.Length == 0)
                leaf = GetLastSegment(source);

            return leaf.Length == 0 ? parent : parent + "/" + leaf;
        }

        internal static bool Matches(string path, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            return path.IndexOf(query!.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetLastSegment(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        private static bool HasExecutableBranchChild(MenuRegistry registry, string root)
        {
            foreach (string child in registry.ChildrenOf(root))
            {
                if (MenuRedirectGuard.IsBuiltIn(child, registry, out _))
                    continue;

                if (MenuRedirectGuard.IsProtectedContainer(child, registry, out _))
                    continue;

                if (registry.GetExecutableCount(child) > 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>树形选择器中的一个节点；每个节点仍对应一个注册表路径条目。</summary>
    internal sealed class MenuRedirectPickerNode
    {
        internal MenuRedirectPickerEntry Entry = null!;
        internal string Segment = string.Empty;
        internal int Depth;
        internal readonly List<MenuRedirectPickerNode> Children =
            new List<MenuRedirectPickerNode>();

        internal string Path => Entry.Path;
        internal bool HasChildren => Children.Count > 0;
    }

    /// <summary>
    /// 把扁平的注册表条目组织成按路径层级排列的树，并负责搜索过滤与折叠状态。
    /// 所有祖先路径都来自 <see cref="MenuRedirectPickerData.Build"/>，因此树节点均可选择。
    /// </summary>
    internal sealed class MenuRedirectPickerTree
    {
        internal readonly List<MenuRedirectPickerNode> Roots =
            new List<MenuRedirectPickerNode>();
        internal readonly Dictionary<string, MenuRedirectPickerNode> ByPath =
            new Dictionary<string, MenuRedirectPickerNode>(StringComparer.Ordinal);

        internal static MenuRedirectPickerTree Build(
            IReadOnlyList<MenuRedirectPickerEntry> entries)
        {
            var tree = new MenuRedirectPickerTree();
            var nodes = new List<MenuRedirectPickerNode>(entries.Count);

            foreach (MenuRedirectPickerEntry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Path))
                    continue;

                var node = new MenuRedirectPickerNode
                {
                    Entry = entry,
                    Segment = GetLastSegment(entry.Path)
                };

                tree.ByPath[entry.Path] = node;
                nodes.Add(node);
            }

            foreach (MenuRedirectPickerNode node in nodes)
            {
                int slash = node.Path.LastIndexOf('/');

                if (slash > 0 &&
                    tree.ByPath.TryGetValue(
                        node.Path.Substring(0, slash),
                        out MenuRedirectPickerNode? parent))
                {
                    parent.Children.Add(node);
                }
                else
                {
                    tree.Roots.Add(node);
                }
            }

            AssignDepth(tree.Roots, 0);
            return tree;
        }

        /// <summary>按展开/折叠状态和搜索词收集当前可见行，顺序为先父后子。</summary>
        internal void CollectVisible(
            string? query,
            HashSet<string> collapsed,
            List<MenuRedirectPickerNode> visible)
        {
            visible.Clear();

            var matching = new HashSet<string>(StringComparer.Ordinal);

            foreach (MenuRedirectPickerNode root in Roots)
                MarkMatchingNodes(root, query, matching);

            foreach (MenuRedirectPickerNode root in Roots)
                CollectVisible(root, matching, collapsed, visible);
        }

        /// <summary>展开指定路径的所有祖先，确保当前路径可见。</summary>
        internal void ExpandAncestors(string path, HashSet<string> collapsed)
        {
            string current = path;

            while (true)
            {
                int slash = current.LastIndexOf('/');

                if (slash <= 0)
                    return;

                current = current.Substring(0, slash);
                collapsed.Remove(current);
            }
        }

        /// <summary>搜索时展开命中节点及其祖先，让所有匹配项直接可见。</summary>
        internal void ExpandMatches(string? query, HashSet<string> collapsed)
        {
            if (string.IsNullOrWhiteSpace(query))
                return;

            var matching = new HashSet<string>(StringComparer.Ordinal);

            foreach (MenuRedirectPickerNode root in Roots)
                MarkMatchingNodes(root, query, matching);

            collapsed.RemoveWhere(matching.Contains);
        }

        internal MenuRedirectPickerNode? Find(string path)
        {
            return ByPath.TryGetValue(path, out MenuRedirectPickerNode? node)
                ? node
                : null;
        }

        private static bool MarkMatchingNodes(
            MenuRedirectPickerNode node,
            string? query,
            HashSet<string> matching)
        {
            bool matches = MenuRedirectPickerData.Matches(node.Path, query);

            foreach (MenuRedirectPickerNode child in node.Children)
                matches |= MarkMatchingNodes(child, query, matching);

            if (matches)
                matching.Add(node.Path);

            return matches;
        }

        private static void CollectVisible(
            MenuRedirectPickerNode node,
            HashSet<string> matching,
            HashSet<string> collapsed,
            List<MenuRedirectPickerNode> visible)
        {
            if (!matching.Contains(node.Path))
                return;

            visible.Add(node);

            if (collapsed.Contains(node.Path))
                return;

            foreach (MenuRedirectPickerNode child in node.Children)
                CollectVisible(child, matching, collapsed, visible);
        }

        private static void AssignDepth(
            List<MenuRedirectPickerNode> nodes,
            int depth)
        {
            foreach (MenuRedirectPickerNode node in nodes)
            {
                node.Depth = depth;
                AssignDepth(node.Children, depth + 1);
            }
        }

        private static string GetLastSegment(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }
    }

    /// <summary>
    /// 原路径选择器：搜索已注册菜单，受保护条目显示但不可选。
    /// 选择只回调路径，由设置页标记未保存；窗口本身不写配置、不生成代码。
    /// </summary>
    internal sealed class MenuRedirectPickerWindow : EditorWindow
    {
        private const float RowHeight = 21f;
        private const float GlyphWidth = 18f;
        private const float TreeIndentWidth = 12f;
        private const float FoldoutWidth = 14f;
        private const string SearchControlName = "MenuRedirectPickerSearch";

        private static readonly Color PassColor = new Color(0.35f, 0.78f, 0.42f);
        private static readonly Color WarningColor = new Color(0.95f, 0.68f, 0.25f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.35f, 0.35f);

        private readonly List<MenuRedirectPickerEntry> _entries =
            new List<MenuRedirectPickerEntry>();
        private readonly List<MenuRedirectPickerNode> _visible =
            new List<MenuRedirectPickerNode>();
        private readonly HashSet<string> _collapsed =
            new HashSet<string>(StringComparer.Ordinal);

        private MenuRegistry? _registry;
        private MenuRedirectPickerTree? _tree;
        private string? _togglePath;
        private string _search = string.Empty;
        private string _currentPath = string.Empty;
        private int _selectedIndex = -1;
        private Vector2 _scroll;
        private bool _focusSearch = true;
        private bool _scrollToSelection;
        private bool _parentsOnly;
        private Action<string>? _onSelected;

        private GUIStyle _glyphStyle = null!;
        private GUIStyle _pathStyle = null!;
        private GUIStyle _mutedPathStyle = null!;
        private GUIStyle _hintStyle = null!;
        private GUIStyle _countStyle = null!;

        internal static void Open(
            MenuRegistry registry,
            string currentPath,
            Action<string> onSelected)
        {
            OpenInternal(registry, currentPath, onSelected, false);
        }

        /// <summary>打开目标父级选择器；选择结果仍是路径，由设置页负责拼接。</summary>
        internal static void OpenTargetParent(
            MenuRegistry registry,
            string currentParentPath,
            Action<string> onSelected)
        {
            OpenInternal(registry, currentParentPath, onSelected, true);
        }

        private static void OpenInternal(
            MenuRegistry registry,
            string currentPath,
            Action<string> onSelected,
            bool parentsOnly)
        {
            var window = CreateInstance<MenuRedirectPickerWindow>();
            window._registry = registry;
            window._currentPath = currentPath ?? string.Empty;
            window._parentsOnly = parentsOnly;
            window._onSelected = onSelected;
            window.titleContent = new GUIContent(window.GetTitle());
            window.minSize = new Vector2(360f, 360f);
            window.Rebuild();

            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                main.center.x - 220f,
                main.center.y - 260f,
                440f,
                520f);
            window.ShowUtility();
        }

        private void OnEnable()
        {
            if (_registry != null)
                Rebuild();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
                Repaint();

            HandleKeyboard();

            EnsureStyles();
            titleContent = new GUIContent(GetTitle());

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(
                L.T("ui.picker.search"),
                EditorStyles.miniLabel,
                GUILayout.Width(38f));
            GUI.SetNextControlName(SearchControlName);
            string search = EditorGUILayout.TextField(
                _search,
                EditorStyles.toolbarSearchField,
                GUILayout.ExpandWidth(true));

            if (!string.Equals(search, _search, StringComparison.Ordinal))
            {
                _search = search;
                ApplyFilter(false);
            }

            if (GUILayout.Button(
                    new GUIContent(
                        L.T("ui.picker.refresh"),
                        L.T("ui.picker.refresh.tooltip")),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(58f)))
            {
                RefreshRegistry();
            }

            EditorGUILayout.EndHorizontal();

            if (_focusSearch)
            {
                _focusSearch = false;
                EditorGUI.FocusTextInControl(SearchControlName);
                Repaint();
            }

            EditorGUILayout.Space(2f);

            if (_visible.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    L.T(_parentsOnly ? "ui.picker.empty.parent" : "ui.picker.empty"),
                    MessageType.Info);
            }
            else
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                for (int i = 0; i < _visible.Count; i++)
                    DrawRow(i);

                EditorGUILayout.EndScrollView();

                if (_togglePath != null)
                {
                    string path = _togglePath;
                    _togglePath = null;
                    ToggleExpanded(path);
                }

                if (_scrollToSelection)
                {
                    // 列表高度 = 窗口内容高度减去工具栏和底部说明；用于让当前项首次打开时可见。
                    ScrollSelectionIntoView(Mathf.Max(80f, position.height - 100f));
                    Repaint();
                }
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(
                L.F("ui.picker.count", _visible.Count, _entries.Count),
                _countStyle);
            EditorGUILayout.LabelField(
                L.T(_parentsOnly ? "ui.picker.legend.parent" : "ui.picker.legend"),
                _hintStyle);
            EditorGUILayout.LabelField(
                L.T(_parentsOnly ? "ui.picker.hint.parent" : "ui.picker.hint"),
                _hintStyle);
        }

        private void Rebuild()
        {
            if (_registry == null)
                return;

            _entries.Clear();
            _entries.AddRange(
                _parentsOnly
                    ? MenuRedirectPickerData.BuildParentPaths(_registry)
                    : MenuRedirectPickerData.Build(_registry));
            _tree = MenuRedirectPickerTree.Build(_entries);
            ApplyFilter(true);
            Repaint();
        }

        private string GetTitle()
        {
            return L.T(_parentsOnly ? "ui.picker.title.parent" : "ui.picker.title");
        }

        private void RefreshRegistry()
        {
            MenuRedirectEngine.InvalidateMenuPathCache();
            _registry = MenuRedirectEngine.Registry;
            Rebuild();
        }

        private void ApplyFilter(bool preferCurrentPath)
        {
            if (_tree != null)
            {
                if (preferCurrentPath && _currentPath.Length > 0)
                    _tree.ExpandAncestors(_currentPath, _collapsed);

                if (!string.IsNullOrWhiteSpace(_search))
                    _tree.ExpandMatches(_search, _collapsed);
            }

            RefreshVisible(preferCurrentPath);
        }

        /// <summary>
        /// 按当前折叠状态重建可见行，并尽量保留原选中路径；不改变折叠状态。
        /// </summary>
        private void RefreshVisible(
            bool preferCurrentPath,
            string? preferredPath = null)
        {
            string? previous = GetSelectedPath();

            _tree?.CollectVisible(_search, _collapsed, _visible);

            int index = -1;

            if (preferCurrentPath && _currentPath.Length > 0)
                index = IndexOfSelectable(_currentPath);

            if (index < 0 && previous != null)
                index = IndexOfSelectable(previous);

            if (index < 0 && preferredPath != null)
                index = IndexOfSelectable(preferredPath);

            if (index < 0)
                index = FindFirstSelectable();

            _selectedIndex = index;
            _scrollToSelection = index >= 0;
            Repaint();
        }

        private void ToggleExpanded(string path)
        {
            if (!_collapsed.Remove(path))
                _collapsed.Add(path);

            RefreshVisible(false, path);

            // 折叠/展开保持当前滚动位置，不把视野外的选中项滚进来。
            _scrollToSelection = false;
        }

        private void ScrollSelectionIntoView(float viewHeight)
        {
            if (!_scrollToSelection || _selectedIndex < 0)
                return;

            _scrollToSelection = false;

            if (viewHeight <= 0f)
                return;
            float contentHeight = _visible.Count * RowHeight;
            float top = _selectedIndex * RowHeight;
            float bottom = top + RowHeight;

            if (top < _scroll.y)
                _scroll.y = top;
            else if (bottom > _scroll.y + viewHeight)
                _scroll.y = bottom - viewHeight;

            _scroll.y = Mathf.Clamp(
                _scroll.y,
                0f,
                Mathf.Max(0f, contentHeight - viewHeight));
        }

        private int IndexOfSelectable(string path)
        {
            for (int i = 0; i < _visible.Count; i++)
            {
                MenuRedirectPickerNode node = _visible[i];

                if (!node.Entry.IsProtected &&
                    string.Equals(node.Path, path, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private string? GetSelectedPath()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _visible.Count)
                return null;

            return _visible[_selectedIndex].Path;
        }

        private int FindFirstSelectable()
        {
            for (int i = 0; i < _visible.Count; i++)
            {
                if (!_visible[i].Entry.IsProtected)
                    return i;
            }

            return -1;
        }

        private void HandleKeyboard()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown)
                return;

            switch (current.keyCode)
            {
                case KeyCode.Escape:
                    current.Use();
                    Close();
                    GUIUtility.ExitGUI();
                    return;

                case KeyCode.DownArrow:
                    current.Use();
                    MoveSelection(1);
                    return;

                case KeyCode.UpArrow:
                    current.Use();
                    MoveSelection(-1);
                    return;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    current.Use();
                    ConfirmSelection();
                    return;
            }
        }

        private void MoveSelection(int delta)
        {
            if (_visible.Count == 0)
            {
                _selectedIndex = -1;
                return;
            }

            int index = _selectedIndex;
            if (index < 0)
                index = delta > 0 ? -1 : 0;

            for (int step = 0; step < _visible.Count; step++)
            {
                index = (index + delta + _visible.Count) % _visible.Count;

                if (_visible[index].Entry.IsProtected)
                    continue;

                _selectedIndex = index;
                _scrollToSelection = true;
                Repaint();
                return;
            }
        }

        private void DrawRow(int index)
        {
            MenuRedirectPickerNode node = _visible[index];
            MenuRedirectPickerEntry entry = node.Entry;
            Rect row = GUILayoutUtility.GetRect(
                0f,
                RowHeight,
                GUILayout.ExpandWidth(true));

            bool isSelected = !entry.IsProtected &&
                string.Equals(node.Path, _currentPath, StringComparison.Ordinal);
            bool isFocused = index == _selectedIndex && !entry.IsProtected;
            bool hovered = row.Contains(Event.current.mousePosition);

            if (isSelected)
                EditorGUI.DrawRect(row, GetSelectionColor());
            else if (isFocused || hovered)
                EditorGUI.DrawRect(row, GetFocusColor());

            // 整行点击：单击只显示悬停效果（并折叠/展开分支），双击选中；
            // 必须先于 Foldout 处理并 Use() 掉事件，折叠控件只作纯视觉显示。
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                row.Contains(Event.current.mousePosition))
            {
                if (Event.current.clickCount >= 2)
                {
                    // 双击选中；受保护条目只吞掉事件，不可选。
                    if (!entry.IsProtected)
                    {
                        _selectedIndex = index;
                        Event.current.Use();
                        ConfirmSelection();
                        return;
                    }
                }
                else
                {
                    // 单击只显示悬停效果，不移动焦点（焦点只由键盘 ↑/↓ 改变）；
                    // 分支仍可折叠。
                    if (node.HasChildren)
                        _togglePath = node.Path;
                }

                Event.current.Use();
            }

            float indent = 4f + node.Depth * TreeIndentWidth;
            var foldoutRect = new Rect(
                row.x + indent,
                row.y,
                FoldoutWidth,
                row.height);

            if (node.HasChildren)
            {
                EditorGUI.Foldout(
                    foldoutRect,
                    !_collapsed.Contains(node.Path),
                    GUIContent.none,
                    false);
            }

            float glyphX = row.x + indent + FoldoutWidth;

            Color previousColor = GUI.color;
            GUI.color = entry.IsProtected
                ? ErrorColor
                : entry.IsProtectedContainer
                    ? WarningColor
                    : entry.HasExecutable
                        ? PassColor
                        : WarningColor;

            GUI.Label(
                new Rect(glyphX, row.y, GlyphWidth, row.height),
                GetStatusGlyph(entry),
                _glyphStyle);

            GUI.color = previousColor;

            GUI.Label(
                new Rect(
                    glyphX + GlyphWidth + 6f,
                    row.y,
                    Mathf.Max(0f, row.xMax - glyphX - GlyphWidth - 10f),
                    row.height),
                new GUIContent(node.Segment, entry.GetTooltip()),
                entry.IsProtected ? _mutedPathStyle : _pathStyle);
        }

        internal static string GetStatusGlyph(MenuRedirectPickerEntry entry)
        {
            if (entry.IsProtected)
                return "✕";

            if (entry.IsProtectedContainer)
                return "!";

            return entry.HasExecutable ? "✓" : "!";
        }

        private void ConfirmSelection()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _visible.Count)
                return;

            MenuRedirectPickerEntry entry = _visible[_selectedIndex].Entry;
            if (entry.IsProtected)
                return;

            _onSelected?.Invoke(entry.Path);
            Close();
            GUIUtility.ExitGUI();
        }

        private void EnsureStyles()
        {
            if (_glyphStyle != null)
                return;

            _glyphStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };

            _pathStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            _mutedPathStyle = new GUIStyle(_pathStyle);
            _mutedPathStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.62f, 0.62f, 0.62f)
                : new Color(0.48f, 0.48f, 0.48f);

            _hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true
            };

            _countStyle = new GUIStyle(EditorStyles.miniLabel);
        }

        private static Color GetSelectionColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.24f, 0.37f, 0.58f, 0.75f)
                : new Color(0.24f, 0.49f, 0.90f, 0.30f);
        }

        private static Color GetHoverColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.06f)
                : new Color(0f, 0f, 0f, 0.05f);
        }

        private static Color GetFocusColor()
        {
            // 焦点（待确认）：比选中更浅更透明，提示此处将被双击/Enter 确认。
            return EditorGUIUtility.isProSkin
                ? new Color(0.40f, 0.55f, 0.78f, 0.42f)
                : new Color(0.24f, 0.49f, 0.90f, 0.22f);
        }
    }
}
