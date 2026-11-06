using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using L = MUD.Modules.MenuRedirect.MenuRedirectLocalization;
using RuleValidation = MUD.Modules.MenuRedirect.MenuRedirectRuleValidation;
using RuleValidationLevel = MUD.Modules.MenuRedirect.MenuRedirectIssueLevel;

namespace MUD.Modules.MenuRedirect
{
    /// <summary>
    /// Project Settings 页面：左侧紧凑规则列表 + 右侧规则详情。
    /// 修改后点“应用”才写入 ProjectSettings/MenuRedirect.asset 并生成包装代码。
    /// </summary>
    internal sealed class MenuRedirectSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Project/MUD/Menu Redirect";
        // Unity ReorderableList 的默认 elementHeight 为 21。
        private const float RuleRowHeight = 21f;
        private const int FeedbackMaxLines = 6;
        private const int FeedbackMaxChars = 600;
        private const double ValidationDebounceSeconds = 0.15;
        private const double DraftSaveDebounceSeconds = 0.75;
        private const double PendingCodeChangesCheckSeconds = 1.0;

        // 左右分栏的分隔条。分隔条本身是一个真实的布局列，宽度由它预留。
        internal const float SplitterWidth = 6f;
        internal const float MinLeftPaneWidth = 220f;
        internal const float MinRightPaneWidth = 240f;
        internal const float MinSplitViewWidth =
            MinLeftPaneWidth + MinRightPaneWidth + SplitterWidth;

        private sealed class RuleRowDisplay
        {
            public string Source = string.Empty;
            public string Target = string.Empty;
            public string Preview = string.Empty;
            public string PreviewTooltip = string.Empty;
            public string StatusGlyph = string.Empty;
            public string StatusTooltip = string.Empty;
            public float SourceWidth;
            public GUIContent StatusContent = GUIContent.none;
            public GUIContent SourceContent = GUIContent.none;
            public GUIContent TargetContent = GUIContent.none;
            public GUIContent PreviewContent = GUIContent.none;
            public GUIContent PriorityContent = GUIContent.none;
        }

        private sealed class ValidationPanelDisplay
        {
            public GUIContent Title = GUIContent.none;
            public readonly List<GUIContent> Issues = new List<GUIContent>();
        }

        private MenuRedirectSettings? _settings;
        private ReorderableList? _ruleList;
        private MenuRedirectRule? _selectedRule;
        private MenuRedirectRule? _pendingRemoveRule;
        private MenuRedirectRule? _pendingRemoveSelection;
        private MenuRedirectRule? _selectionAtFrameStart;
        private List<MenuRedirectRule> _savedRules = new List<MenuRedirectRule>();
        private string _savedGeneratedCodeFolder = MenuRedirectSettings.DefaultGeneratedCodeFolder;
        private static List<MenuRedirectRule>? _savedBaselineRules;
        private static string _savedBaselineFolder = MenuRedirectSettings.DefaultGeneratedCodeFolder;
        private MenuRedirectValidationResult? _validationResult;
        private List<RuleValidation> _ruleValidations = new List<RuleValidation>();
        private int _enabledRuleCount;
        private int _passRuleCount;
        private int _warningRuleCount;
        private int _errorRuleCount;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private MenuRedirectText? _lastApplyMessage;
        private MessageType _lastApplyType = MessageType.Info;
        private string? _lastLoggedFeedback;
        private bool _dirty;
        private bool _showHelp;
        private bool _validationsDirty = true;
        private bool _pendingCodeChanges;
        private bool _pendingCodeChangesValid;
        private DateTime _pendingCodeChangesFileStamp;
        private double _pendingCodeChangesNextCheckAt;
        private readonly Dictionary<MenuRedirectRule, RuleRowDisplay> _rowDisplayCache =
            new Dictionary<MenuRedirectRule, RuleRowDisplay>();

        private bool _stylesInitialized;
        private bool _stylesProSkin;
        private GUIStyle _paneStyle = null!;
        private GUIStyle _paneTitleStyle = null!;
        private GUIStyle _sectionTitleStyle = null!;
        private GUIStyle _descriptionStyle = null!;
        private GUIStyle _detailLabelStyle = null!;
        private GUIStyle _detailPreviewStyle = null!;
        private GUIStyle _detailStatusStyle = null!;
        private GUIStyle _statusGlyphStyle = null!;
        private GUIStyle _rowTargetStyle = null!;
        private GUIStyle _rowSourceStyle = null!;
        private GUIStyle _warningBodyStyle = null!;
        private GUIStyle _errorBodyStyle = null!;
        private GUIStyle _okBodyStyle = null!;
        private GUIStyle _hintStyle = null!;
        private GUIStyle _mutedLabelStyle = null!;
        private GUIStyle _mutedRightLabelStyle = null!;
        private GUIStyle _priorityStyle = null!;
        private GUIStyle _emptyTitleStyle = null!;
        private GUIStyle _emptyHintStyle = null!;
        private GUIStyle _countStyle = null!;
        private GUIStyle _rowRemoveButtonStyle = null!;
        private GUIStyle _miniButtonStyle = null!;
        private GUIStyle _toolbarButtonStyle = null!;
        private GUIStyle _primaryButtonStyle = null!;
        private GUIStyle _emptyButtonStyle = null!;
        private Color _accentColor;
        private Color _separatorColor;
        private Color _primaryTextColor;
        private Color _passTextColor;
        private Color _warningTextColor;
        private Color _errorTextColor;
        private Color _disabledTextColor;

        private MenuRedirectLanguage _widthCacheLanguage;
        private bool _widthCacheValid;
        private readonly Dictionary<string, float> _miniButtonWidths = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _toolbarButtonWidths = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _toolbarCountWidths = new Dictionary<string, float>();
        private readonly Dictionary<(GUIStyle, Color), GUIStyle> _coloredStyles =
            new Dictionary<(GUIStyle, Color), GUIStyle>();
        private string[]? _languageOptions;
        private MenuRedirectLanguage _languageOptionsLanguage;
        private string _generatedCodePath = string.Empty;
        private string _generatedCodePathFolder = "\u0000";
        private string _generatedCodeFullPath = string.Empty;
        private string _generatedCodeFullPathFolder = "\u0000";
        private float _configLabelWidth;
        private float _languageLabelWidth;
        private float _splitLeftWidth;
        private float _measuredSplitViewWidth;
        private bool _splitterDragging;
        private float _splitterDragStartMouseX;
        private float _splitterDragStartWidth;
        private int _splitterControlId;
        private readonly int _splitterControlHint = "MUD.MenuRedirect.Splitter".GetHashCode();

        private readonly Dictionary<(string, string), GUIContent> _contentCache =
            new Dictionary<(string, string), GUIContent>();
        private readonly Dictionary<(string, int), string> _intFormatCache =
            new Dictionary<(string, int), string>();
        private readonly Dictionary<RuleValidation, ValidationPanelDisplay> _validationDisplayCache =
            new Dictionary<RuleValidation, ValidationPanelDisplay>();

        private string _generatedTooltipPath = "\u0000";
        private GUIContent _generatedLabelContent = GUIContent.none;
        private int _ruleCountCache = -1;
        private GUIContent _ruleCountContent = GUIContent.none;

        private string _ruleSearch = string.Empty;
        private bool _showOnlyIssues;
        private readonly List<MenuRedirectRule> _visibleRules = new List<MenuRedirectRule>();
        private bool _visibleRulesDirty = true;
        private int _visibleRuleCountCache = -1;
        private double _validationDueAt;
        private double _draftDueAt;
        private bool _draftSaveScheduled;

        private string? _feedbackApplyText;
        private MessageType _feedbackApplyType;
        private string? _feedbackFolderError;
        private string? _feedbackLastErrors;
        private bool _feedbackCached;
        private string _feedbackText = string.Empty;
        private MessageType _feedbackType = MessageType.None;
        private MenuRedirectText? _feedbackMessage;
        private string? _feedbackResolvedMessage;
        private MenuRedirectValidationResult? _feedbackValidationResult;

        private MenuRedirectSettingsProvider(string path, SettingsScope scope)
            : base(path, scope)
        {
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new MenuRedirectSettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Menu Redirect",
                keywords = new[] { "MUD", "menu", "redirect", "menuredirect", "菜单", "重定向" }
            };
        }

        [MenuItem("Tools/MUD/Menu Redirect")]
        private static void Open()
        {
            SettingsService.OpenProjectSettings(SettingsPath);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            base.OnActivate(searchContext, rootElement);

            // 域重载/退出编辑器前没有机会走 OnDeactivate，必须同步落草稿。
            AssemblyReloadEvents.beforeAssemblyReload -= SaveDraftBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += SaveDraftBeforeAssemblyReload;
            EditorApplication.quitting -= SaveDraftBeforeQuit;
            EditorApplication.quitting += SaveDraftBeforeQuit;

            InvalidatePendingCodeChanges();
            ScheduleSettingsLoad();
        }

        public override void OnDeactivate()
        {
            // 草稿先于状态清理写入；读取 settings 的引用在 OnDeactivate 后仍有效。
            SaveDraftNow();
            CancelDeferredValidation();

            AssemblyReloadEvents.beforeAssemblyReload -= SaveDraftBeforeAssemblyReload;
            EditorApplication.quitting -= SaveDraftBeforeQuit;

            // 宽度不持久化：离开页面后恢复默认；同时释放可能仍被捕获的拖动，
            // 避免下次进入页面时卡在拖动状态。
            _splitLeftWidth = 0f;
            _splitterDragging = false;

            if (GUIUtility.hotControl == _splitterControlId)
                GUIUtility.hotControl = 0;

            _splitterControlId = 0;

            base.OnDeactivate();
        }

        private void SaveDraftBeforeAssemblyReload()
        {
            SaveDraftNow();
        }

        private void SaveDraftBeforeQuit()
        {
            SaveDraftNow();
        }

        /// <summary>
        /// 延迟到下一次编辑器 tick 再访问 ScriptableSingleton。
        /// 页面在域重载期间仍打开时，立即访问会与 Unity 的 singleton 恢复过程冲突。
        /// </summary>
        private void ScheduleSettingsLoad()
        {
            EditorApplication.delayCall -= LoadSettings;
            EditorApplication.delayCall += LoadSettings;
        }

        private void LoadSettings()
        {
            EditorApplication.delayCall -= LoadSettings;

            if (_settings != null)
            {
                if (_settings.rules == null)
                    _settings.rules = new List<MenuRedirectRule>();

                InternalEditorUtility.RepaintAllViews();
                return;
            }

            _settings = MenuRedirectSettings.instance;
            if (_settings.rules == null)
                _settings.rules = new List<MenuRedirectRule>();

            _settings.generatedCodeFolder =
                MenuRedirectEngine.NormalizeGeneratedFolder(_settings.generatedCodeFolder);
            if (string.IsNullOrWhiteSpace(_settings.lastGeneratedCodePath))
                _settings.lastGeneratedCodePath = MenuRedirectSettings.DefaultGeneratedCodePath;

            bool firstLoad = _savedBaselineRules == null;
            if (firstLoad)
            {
                _savedBaselineRules = CloneRules(_settings.rules);
                _savedBaselineFolder = _settings.generatedCodeFolder;
            }

            _savedRules = CloneRules(_savedBaselineRules!);
            _savedGeneratedCodeFolder = _savedBaselineFolder;
            _ruleList = null;
            _selectedRule = null;
            _listScroll = Vector2.zero;
            _detailScroll = Vector2.zero;
            _dirty = false;
            _visibleRulesDirty = true;
            _visibleRuleCountCache = -1;
            SetApplyFeedback(null);
            InvalidatePendingCodeChanges();

            // 只有本域第一次打开设置页时才尝试恢复草稿；同一 provider 重新激活时
            // 内存里的规则就是草稿，不需要覆盖。
            if (firstLoad)
                TryRestoreDraft(_settings);

            BuildRuleValidations();

            InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>
        /// 恢复上次会话未保存的草稿。草稿自带的“已保存基线”必须仍与当前配置一致；
        /// 否则说明配置已在其它会话中应用或回退，旧草稿不再适用。
        /// </summary>
        private bool TryRestoreDraft(MenuRedirectSettings settings)
        {
            if (_savedBaselineRules == null)
                return false;

            if (!MenuRedirectDraft.TryLoad(
                    out MenuRedirectDraftData? draft,
                    out string? loadError))
            {
                if (!string.IsNullOrEmpty(loadError))
                    Debug.LogWarning("[MenuRedirect] " + loadError);

                MenuRedirectDraft.Delete();
                return false;
            }

            string baselineRulesJson = MenuRedirectRuleFile.ToJson(_savedBaselineRules);
            string baselineFolder =
                MenuRedirectEngine.NormalizeGeneratedFolder(_savedBaselineFolder);
            if (draft == null || !draft.MatchesBaseline(baselineRulesJson, baselineFolder))
            {
                MenuRedirectDraft.Delete();
                return false;
            }

            string draftFolder =
                MenuRedirectEngine.NormalizeGeneratedFolder(draft.draftGeneratedCodeFolder);
            if (string.Equals(draft.draftRulesJson, baselineRulesJson, StringComparison.Ordinal) &&
                string.Equals(draftFolder, baselineFolder, StringComparison.Ordinal))
            {
                // 崩溃前刚好改回原样；没有未保存内容可恢复。
                MenuRedirectDraft.Delete();
                return false;
            }

            if (!MenuRedirectRuleFile.TryParseDraft(
                    draft.draftRulesJson,
                    out List<MenuRedirectRule> draftRules,
                    out MenuRedirectText? parseError))
            {
                Debug.LogWarning(
                    "[MenuRedirect] " +
                    (parseError?.Resolve() ?? "invalid draft rule data"));
                MenuRedirectDraft.Delete();
                return false;
            }

            settings.rules.Clear();
            settings.rules.AddRange(draftRules);
            settings.generatedCodeFolder = draftFolder;
            _dirty = true;
            _selectedRule = null;
            _ruleList = null;
            _visibleRulesDirty = true;
            SetApplyFeedback(L.Text("ui.draft.restored"), MessageType.Info);
            return true;
        }

        public override void OnGUI(string searchContext)
        {
            Event current = Event.current;
            if (_settings != null &&
                _splitterDragging &&
                (current.type == EventType.MouseDrag ||
                 current.type == EventType.MouseUp) &&
                TryHandleSplitterDragEvent(current))
            {
                return;
            }

            if (_settings == null)
            {
                EditorGUILayout.HelpBox(L.T("ui.loading"), MessageType.Info);
                ScheduleSettingsLoad();
                return;
            }

            EnsureStyles();

            MenuRedirectSettings settings = _settings;
            if (settings.rules == null)
                settings.rules = new List<MenuRedirectRule>();

            if (HasNullRule(settings.rules))
            {
                settings.rules.RemoveAll(RuleIsNull);
                _selectedRule = null;
                MarkDirty();
            }

            if (!_splitterDragging &&
                _validationsDirty &&
                EditorApplication.timeSinceStartup >= _validationDueAt)
            {
                BuildRuleValidations();
            }

            RefreshVisibleRulesIfNeeded(settings);
            ReorderableList ruleList = EnsureRuleList(settings);
            SyncRuleSelection(settings, ruleList);
            _selectionAtFrameStart = _selectedRule;

            DrawHeader(settings);
            DrawToolbar(settings);
            EditorGUILayout.Space(2);

            DrawSplitView(settings, ruleList);
            DrawFeedback();
            ProcessPendingRuleRemoval(settings);
        }

        private void DrawHeader(MenuRedirectSettings settings)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(
                GetContent(L.T("ui.header.description")),
                _descriptionStyle,
                GUILayout.ExpandWidth(true));

            float helpWidth = GetMiniButtonWidth(
                64f,
                "ui.header.help.show",
                "ui.header.help.hide");

            if (_languageLabelWidth <= 0f)
            {
                _languageLabelWidth = Mathf.Max(
                    58f,
                    Mathf.Ceil(_mutedRightLabelStyle.CalcSize(
                        new GUIContent(L.T("ui.language.label"))).x) + 6f);
            }

            float languageLabelWidth = _languageLabelWidth;
            float rightGroupWidth = helpWidth + 24f + languageLabelWidth + 4f + 112f;

            EditorGUILayout.BeginHorizontal(GUILayout.Width(rightGroupWidth));

            if (GUILayout.Button(
                    L.T(_showHelp ? "ui.header.help.hide" : "ui.header.help.show"),
                    _miniButtonStyle,
                    GUILayout.Width(helpWidth)))
            {
                _showHelp = !_showHelp;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                GetContent(
                    L.T("ui.language.label"),
                    L.T("ui.language.tooltip")),
                _mutedRightLabelStyle,
                GUILayout.Width(languageLabelWidth));
            GUILayout.Space(4f);
            DrawLanguageSelector();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndHorizontal();

            if (_showHelp)
                EditorGUILayout.HelpBox(L.T("ui.help.text"), MessageType.None, true);

            DrawConfigPathRows(settings);

            EditorGUILayout.Space(4);
            DrawSeparator();
            EditorGUILayout.Space(4);
        }

        private void DrawLanguageSelector()
        {
            string[] options = GetLanguageOptions();
            MenuRedirectLanguage mode = L.Mode;
            int selected = EditorGUILayout.Popup(
                (int)mode,
                options,
                GUILayout.Width(112f));

            if (selected != (int)mode)
                ChangeLanguage((MenuRedirectLanguage)selected);
        }

        /// <summary>语言下拉选项随当前界面语言变化，缓存数组避免每个事件重复分配。</summary>
        private string[] GetLanguageOptions()
        {
            MenuRedirectLanguage language = L.CurrentLanguage;
            if (_languageOptions == null || _languageOptionsLanguage != language)
            {
                _languageOptions = new[]
                {
                    L.T("ui.language.auto"),
                    L.T("ui.language.chinese"),
                    L.T("ui.language.english")
                };
                _languageOptionsLanguage = language;
            }

            return _languageOptions;
        }

        private void ChangeLanguage(MenuRedirectLanguage mode)
        {
            L.SetMode(mode);
            _rowDisplayCache.Clear();
            _validationDisplayCache.Clear();
            InvalidateCachesIfNeeded();
            BuildRuleValidations();
            InternalEditorUtility.RepaintAllViews();
        }

        private void DrawConfigPathRows(MenuRedirectSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            InvalidateCachesIfNeeded();
            if (_configLabelWidth <= 0f)
            {
                _configLabelWidth = Mathf.Max(
                    52f,
                    Mathf.Ceil(_mutedLabelStyle.CalcSize(
                        new GUIContent(L.T("ui.config.label"))).x) + 8f,
                    Mathf.Ceil(_mutedLabelStyle.CalcSize(
                        new GUIContent(L.T("ui.generated.label"))).x) + 8f);
            }

            float labelWidth = _configLabelWidth;

            DrawConfigPathRow(labelWidth);
            GUILayout.Space(2f);
            DrawGeneratedCodePathRow(settings, labelWidth);

            EditorGUILayout.EndVertical();
        }

        private void DrawConfigPathRow(float labelWidth)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(
                L.T("ui.config.label"),
                _mutedLabelStyle,
                GUILayout.Width(labelWidth));
            EditorGUILayout.SelectableLabel(
                MenuRedirectSettings.ProjectSettingsPath,
                EditorStyles.miniLabel,
                GUILayout.Height(18));

            float copyWidth = GetMiniButtonWidth(44f, "ui.config.copy");
            if (GUILayout.Button(
                    L.T("ui.config.copy"),
                    _miniButtonStyle,
                    GUILayout.Width(copyWidth)))
            {
                EditorGUIUtility.systemCopyBuffer = MenuRedirectSettings.ProjectSettingsPath;
            }

            float openWidth = GetMiniButtonWidth(76f, "ui.config.open");
            if (GUILayout.Button(
                    L.T("ui.config.open"),
                    _miniButtonStyle,
                    GUILayout.Width(openWidth)))
            {
                OpenConfigDirectory();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>生成代码路径标签随配置路径变化，按路径缓存 GUIContent。</summary>
        private GUIContent GetGeneratedLabelContent(MenuRedirectSettings settings)
        {
            string path = GetGeneratedCodePath(settings);
            if (!string.Equals(path, _generatedTooltipPath, StringComparison.Ordinal))
            {
                _generatedTooltipPath = path;
                _generatedLabelContent = new GUIContent(
                    L.T("ui.generated.label"),
                    L.F("ui.generated.tooltip", path));
            }

            return _generatedLabelContent;
        }

        private void DrawGeneratedCodePathRow(
            MenuRedirectSettings settings,
            float labelWidth)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(
                GetGeneratedLabelContent(settings),
                _mutedLabelStyle,
                GUILayout.Width(labelWidth));

            EditorGUI.BeginChangeCheck();
            string folder = EditorGUILayout.TextField(
                settings.generatedCodeFolder ?? string.Empty,
                GUILayout.Height(18f));
            if (EditorGUI.EndChangeCheck())
            {
                settings.generatedCodeFolder = folder;
                MarkDirty();
            }

            float browseWidth = GetMiniButtonWidth(66f, "ui.generated.browse");
            if (GUILayout.Button(
                    GetContent(
                        L.T("ui.generated.browse"),
                        L.T("ui.generated.browse.tooltip")),
                    _miniButtonStyle,
                    GUILayout.Width(browseWidth)))
            {
                BrowseGeneratedCodeFolder(settings);
            }

            float openWidth = GetMiniButtonWidth(44f, "ui.generated.open");
            if (GUILayout.Button(
                    GetContent(
                        L.T("ui.generated.open"),
                        L.T("ui.generated.open.tooltip")),
                    _miniButtonStyle,
                    GUILayout.Width(openWidth)))
            {
                OpenGeneratedCodeDirectory(settings);
            }

            float defaultWidth = GetMiniButtonWidth(50f, "ui.generated.default");
            if (GUILayout.Button(
                    GetContent(
                        L.T("ui.generated.default"),
                        L.T("ui.generated.default.tooltip")),
                    _miniButtonStyle,
                    GUILayout.Width(defaultWidth)))
            {
                ResetGeneratedCodeFolder(settings);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar(MenuRedirectSettings settings)
        {
            EditorGUILayout.BeginHorizontal();

            DrawToolbarCount(GetF("ui.toolbar.enabled", _enabledRuleCount), _primaryTextColor);
            GUILayout.Space(10f);
            DrawToolbarCount(GetF("ui.toolbar.passed", _passRuleCount), _passTextColor);
            GUILayout.Space(10f);
            DrawToolbarCount(GetF("ui.toolbar.warnings", _warningRuleCount), _warningTextColor);
            GUILayout.Space(10f);
            DrawToolbarCount(GetF("ui.toolbar.errors", _errorRuleCount), _errorTextColor);

            if (_dirty)
            {
                GUILayout.Space(10f);
                DrawToolbarCount(L.T("ui.toolbar.unsaved"), _primaryTextColor);
            }
            else if (GetPendingCodeChanges(settings))
            {
                GUILayout.Space(10f);
                DrawToolbarCount(L.T("ui.toolbar.pending"), _warningTextColor);
            }

            GUILayout.FlexibleSpace();

            float importWidth = GetToolbarButtonWidth(58f, "ui.toolbar.import");
            float exportWidth = GetToolbarButtonWidth(58f, "ui.toolbar.export");

            if (GUILayout.Button(
                    GetContent(
                        L.T("ui.toolbar.import"),
                        L.T("ui.toolbar.import.tooltip")),
                    _toolbarButtonStyle,
                    GUILayout.Width(importWidth)))
            {
                ImportRules(settings);
            }

            GUILayout.Space(4);

            if (GUILayout.Button(
                    GetContent(
                        L.T("ui.toolbar.export"),
                        L.T("ui.toolbar.export.tooltip")),
                    _toolbarButtonStyle,
                    GUILayout.Width(exportWidth)))
            {
                ExportRules(settings);
            }

            GUILayout.Space(8);

            using (new EditorGUI.DisabledScope(!_dirty))
            {
                if (GUILayout.Button(
                        GetContent(
                            L.T("ui.toolbar.revert"),
                            L.T("ui.toolbar.revert.tooltip")),
                        _toolbarButtonStyle,
                        GUILayout.Width(60)))
                {
                    Revert(settings);
                }
            }

            GUILayout.Space(4);
            DrawApplyButton(settings);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbarCount(string text, Color color)
        {
            GUIContent content = GetContent(text);
            if (!_toolbarCountWidths.TryGetValue(text, out float width))
            {
                width = _countStyle.CalcSize(content).x + 4f;
                _toolbarCountWidths[text] = width;
            }

            DrawColoredEditorLabel(content, _countStyle, color, GUILayout.Width(width));
        }

        private void DrawApplyButton(MenuRedirectSettings settings)
        {
            bool hasErrors = _validationResult?.HasErrors == true;
            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = hasErrors
                ? Color.Lerp(_accentColor, Color.gray, 0.7f)
                : _accentColor;

            using (new EditorGUI.DisabledScope(hasErrors))
            {
                if (GUILayout.Button(
                        GetContent(
                            L.T("ui.toolbar.apply"),
                            L.T(hasErrors
                                ? "ui.toolbar.apply.errorTooltip"
                                : "ui.toolbar.apply.tooltip")),
                        _primaryButtonStyle,
                        GUILayout.Width(76)))
                {
                    Apply(settings);
                }
            }

            GUI.backgroundColor = previousColor;
        }

        private void ImportRules(MenuRedirectSettings settings)
        {
            string selected = EditorUtility.OpenFilePanel(
                L.T("ui.import.title"),
                GetProjectRoot(),
                "json");
            if (string.IsNullOrEmpty(selected))
                return;

            string json;

            try
            {
                json = File.ReadAllText(selected);
            }
            catch (Exception exception)
            {
                SetApplyFeedback(
                    L.Text("ui.import.error.read", exception.Message),
                    MessageType.Error);
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            if (!MenuRedirectRuleFile.TryParse(
                    json,
                    out List<MenuRedirectRule> importedRules,
                    out MenuRedirectText? error))
            {
                SetApplyFeedback(
                    error ?? L.Text("ui.import.error.json", string.Empty),
                    MessageType.Error);
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    L.T("ui.import.confirm.title"),
                    L.F(
                        "ui.import.confirm.message",
                        settings.rules.Count,
                        importedRules.Count),
                    L.T("ui.import.confirm.ok"),
                    L.T("ui.import.confirm.cancel")))
            {
                return;
            }

            GUI.FocusControl(null);
            settings.rules.Clear();
            settings.rules.AddRange(importedRules);
            _selectedRule = settings.rules.Count > 0 ? settings.rules[0] : null;
            _listScroll = Vector2.zero;
            _detailScroll = Vector2.zero;

            ClearRuleFilter();

            if (_ruleList != null)
                _ruleList.index = settings.rules.Count > 0 ? 0 : -1;

            MarkDirty();
            BuildRuleValidations();
            RefreshVisibleRulesIfNeeded(settings);
            SetApplyFeedback(L.Text("ui.import.success", importedRules.Count));
            InternalEditorUtility.RepaintAllViews();
        }

        private void ExportRules(MenuRedirectSettings settings)
        {
            string selected = EditorUtility.SaveFilePanel(
                L.T("ui.export.title"),
                GetProjectRoot(),
                "MenuRedirectRules",
                "json");
            if (string.IsNullOrEmpty(selected))
                return;

            try
            {
                string json = MenuRedirectRuleFile.ToJson(settings.rules);
                File.WriteAllText(selected, json);
            }
            catch (Exception exception)
            {
                SetApplyFeedback(
                    L.Text("ui.export.error", exception.Message),
                    MessageType.Error);
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            SetApplyFeedback(L.Text(
                "ui.export.success",
                settings.rules.Count,
                Path.GetFileName(selected)));
            InternalEditorUtility.RepaintAllViews();
        }

        private void DrawFeedback()
        {
            // Resolve() 会格式化字符串；消息/校验结果未变时复用上次的解析结果。
            if (!ReferenceEquals(_lastApplyMessage, _feedbackMessage))
            {
                _feedbackMessage = _lastApplyMessage;
                _feedbackResolvedMessage = _lastApplyMessage?.Resolve();
            }

            if (!ReferenceEquals(_validationResult, _feedbackValidationResult))
            {
                _feedbackValidationResult = _validationResult;
                _feedbackFolderError = _validationResult?.GeneratedFolderError?.Resolve();
            }

            string? applyText = _feedbackResolvedMessage;
            string? folderError = _feedbackFolderError;
            string? hideMessages = _dirty ? null : MenuRedirectEngine.LastErrors;

            if (string.IsNullOrWhiteSpace(applyText) &&
                string.IsNullOrWhiteSpace(folderError) &&
                string.IsNullOrWhiteSpace(hideMessages))
            {
                _feedbackCached = false;
                return;
            }

            if (!_feedbackCached ||
                !string.Equals(applyText, _feedbackApplyText, StringComparison.Ordinal) ||
                _lastApplyType != _feedbackApplyType ||
                !string.Equals(folderError, _feedbackFolderError, StringComparison.Ordinal) ||
                !string.Equals(hideMessages, _feedbackLastErrors, StringComparison.Ordinal))
            {
                if (!TryBuildFeedback(
                        applyText,
                        _lastApplyType,
                        folderError,
                        hideMessages,
                        out string text,
                        out MessageType type))
                {
                    _feedbackCached = false;
                    return;
                }

                string capped = CapFeedback(
                    text,
                    FeedbackMaxLines,
                    FeedbackMaxChars,
                    count => L.F("ui.feedback.more", count),
                    out bool truncated);

                _feedbackApplyText = applyText;
                _feedbackApplyType = _lastApplyType;
                _feedbackFolderError = folderError;
                _feedbackLastErrors = hideMessages;
                _feedbackText = capped;
                _feedbackType = type;
                _feedbackCached = true;

                if (truncated &&
                    !string.Equals(text, _lastLoggedFeedback, StringComparison.Ordinal))
                {
                    // 反馈区只显示前几行；完整内容写一次 Console，便于排查。
                    _lastLoggedFeedback = text;
                    Debug.LogWarning("[MenuRedirect] " + text);
                }
            }

            EditorGUILayout.HelpBox(_feedbackText, _feedbackType, true);
        }

        private void SetApplyFeedback(
            MenuRedirectText? message,
            MessageType type = MessageType.Info)
        {
            _lastApplyMessage = message;
            _lastApplyType = message == null ? MessageType.Info : type;
        }

        /// <summary>
        /// 合并应用反馈、生成目录错误和隐藏告警：按行去重，取最高级别。
        /// </summary>
        internal static bool TryBuildFeedback(
            string? applyText,
            MessageType applyType,
            string? generatedFolderError,
            string? lastErrors,
            out string text,
            out MessageType type)
        {
            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            type = MessageType.None;

            AppendFeedback(applyText, applyType, lines, seen, ref type);
            AppendFeedback(generatedFolderError, MessageType.Warning, lines, seen, ref type);
            AppendFeedback(lastErrors, MessageType.Warning, lines, seen, ref type);

            if (lines.Count == 0)
            {
                text = string.Empty;
                return false;
            }

            if (type == MessageType.None)
                type = MessageType.Info;

            text = string.Join("\n", lines);
            return true;
        }

        /// <summary>
        /// 将反馈限制在指定行数/字符数内；超出部分由 moreFormatter 生成提示。
        /// </summary>
        internal static string CapFeedback(
            string? text,
            int maxLines,
            int maxChars,
            Func<int, string> moreFormatter,
            out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string normalized = text!.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');

            if (lines.Length <= maxLines && normalized.Length <= maxChars)
                return normalized;

            if (maxLines < 1)
                maxLines = 1;
            if (maxChars < 1)
                maxChars = 1;

            var kept = new List<string>();
            int usedChars = 0;
            int index = 0;

            while (index < lines.Length && kept.Count < maxLines)
            {
                string line = lines[index];
                int extra = kept.Count == 0 ? line.Length : line.Length + 1;
                if (usedChars + extra > maxChars)
                    break;

                kept.Add(line);
                usedChars += extra;
                index++;
            }

            if (kept.Count == 0)
            {
                // 单行本身超长时保留完整首行，避免把一条错误截成不可读片段；
                // Console 中仍有完整内容。
                kept.Add(lines[0]);
                index = 1;
            }

            truncated = true;
            string result = string.Join("\n", kept);
            int remaining = lines.Length - index;
            if (remaining <= 0)
                return result;

            string more = moreFormatter?.Invoke(remaining) ?? string.Empty;
            return string.IsNullOrEmpty(more) ? result : result + "\n" + more;
        }

        private static void AppendFeedback(
            string? message,
            MessageType messageType,
            List<string> lines,
            HashSet<string> seen,
            ref MessageType highestType)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            highestType = HigherFeedbackType(highestType, messageType);

            string normalized = message!.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] messageLines = normalized.Split('\n');
            for (int i = 0; i < messageLines.Length; i++)
            {
                string line = messageLines[i].Trim();
                if (line.Length == 0 || !seen.Add(line))
                    continue;

                lines.Add(line);
            }
        }

        private static MessageType HigherFeedbackType(MessageType left, MessageType right)
        {
            return FeedbackSeverity(left) >= FeedbackSeverity(right) ? left : right;
        }

        private static int FeedbackSeverity(MessageType type)
        {
            switch (type)
            {
                case MessageType.Error:
                    return 3;
                case MessageType.Warning:
                    return 2;
                case MessageType.Info:
                    return 1;
                default:
                    return 0;
            }
        }

        private void DrawSplitView(MenuRedirectSettings settings, ReorderableList ruleList)
        {
            float availableWidth = GetAvailableSplitWidth();
            float defaultLeft = GetDefaultSplitLeftWidth(availableWidth);
            if (_splitLeftWidth <= 0f)
                _splitLeftWidth = defaultLeft;

            // availableWidth 为 0 表示当前事件里还拿不到可靠宽度（例如首帧、
            // 或 currentViewWidth 不可用）。此时不要夹紧，否则会把宽度锁在
            // “两栏最小宽度”上，表现为分隔条完全拖不动。
            if (availableWidth > 0f)
            {
                float maxLeft = GetMaxSplitLeftWidth(availableWidth);
                _splitLeftWidth = ClampSplitLeftWidth(
                    _splitLeftWidth,
                    availableWidth,
                    MinLeftPaneWidth,
                    maxLeft);
            }

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawRuleListPane(settings, ruleList, _splitLeftWidth);

            // 分隔条是一个真实的布局列：先由 GUILayout 预留宽高，再拿到与分栏区域
            // 一致的 Rect。GetRect 返回的 rect 与 Event.current.mousePosition 处于同一
            // GUI 坐标系，Contains 命中测试可靠。
            Rect splitterRect = GUILayoutUtility.GetRect(
                SplitterWidth,
                SplitterWidth,
                GUILayout.Width(SplitterWidth),
                GUILayout.ExpandHeight(true));

            Event current = Event.current;
            Vector2 mouse = current.mousePosition;
            bool splitterHovered = splitterRect.Contains(mouse);

            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(
                splitterRect,
                GetSplitterColor(splitterHovered || _splitterDragging));

            if (splitterHovered || _splitterDragging)
            {
                // 悬停/拖动时在中间画一条高亮线，让可拖动的区域更明显。
                Color highlight = _accentColor;
                highlight.a = 0.9f;
                EditorGUI.DrawRect(
                    new Rect(
                        splitterRect.x + splitterRect.width * 0.5f - 1f,
                        splitterRect.y,
                        2f,
                        splitterRect.height),
                    highlight);
            }

            HandleSplitterDrag(splitterRect, current, mouse, splitterHovered);

            DrawRuleDetailsPane(settings);
            EditorGUILayout.EndHorizontal();

            // GetLastRect 只在 Repaint 阶段可靠；Layout 等阶段可能返回 1×1。
            // 如果把这些临时值当成真实宽度，下一次拖动就会被夹回最小宽度。
            if (current.type == EventType.Repaint)
            {
                Rect rowRect = GUILayoutUtility.GetLastRect();
                if (rowRect.width >= MinSplitViewWidth)
                    _measuredSplitViewWidth = rowRect.width;
            }
        }

        /// <summary>
        /// 处理分隔条拖动。使用 GUIUtility.GetControlID + hotControl 追踪拖动状态，
        /// GetTypeForControl 获取该控件的原始事件类型，不受 current.Use() 影响。
        /// </summary>
        private void HandleSplitterDrag(
            Rect splitterRect,
            Event current,
            Vector2 mouse,
            bool mouseOverSplitter)
        {
            if (current == null)
                return;

            int controlId = GUIUtility.GetControlID(_splitterControlHint, FocusType.Passive);
            // 拖动期间改用按下时记录的 control id，避免布局变化导致 id 漂移后
            // 收不到 MouseDrag / MouseUp，从而卡在拖动状态。
            int eventControlId = _splitterDragging ? _splitterControlId : controlId;

            switch (current.GetTypeForControl(eventControlId))
            {
                case EventType.MouseDown:
                    if (current.button != 0 || !mouseOverSplitter)
                        break;

                    if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != _splitterControlId)
                        break;

                    _splitterDragging = true;
                    _splitterControlId = controlId;
                    _splitterDragStartMouseX = mouse.x;
                    _splitterDragStartWidth = _splitLeftWidth;
                    GUIUtility.hotControl = controlId;
                    current.Use();
                    break;

                case EventType.MouseDrag:
                    if (!_splitterDragging || GUIUtility.hotControl != _splitterControlId)
                        break;

                    if (ApplySplitterDrag(mouse.x))
                        Repaint();
                    current.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != _splitterControlId)
                        break;

                    GUIUtility.hotControl = 0;
                    _splitterDragging = false;
                    _splitterControlId = 0;
                    current.Use();
                    break;
            }
        }

        /// <summary>
        /// 分隔条拖动期间的快速事件路径。拖动的事件阶段本身不会显示在屏幕上，
        /// 不需要重建整页；只更新宽度并请求 Repaint，由随后的 Layout/Repaint
        /// 按新宽度完整绘制，因此仍然保持完全实时变宽。
        /// </summary>
        private bool TryHandleSplitterDragEvent(Event current)
        {
            if (GUIUtility.hotControl != _splitterControlId)
            {
                // 焦点切换或其他控件抢走 hotControl 时复位，避免页面卡在拖动状态。
                _splitterDragging = false;
                _splitterControlId = 0;
                Repaint();
                return false;
            }

            if (current.type == EventType.MouseDrag)
            {
                if (ApplySplitterDrag(current.mousePosition.x))
                    Repaint();
                current.Use();
                return true;
            }

            if (current.type == EventType.MouseUp)
            {
                GUIUtility.hotControl = 0;
                _splitterDragging = false;
                _splitterControlId = 0;
                current.Use();
                Repaint();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 根据拖动起点和当前鼠标位置计算左栏宽度。返回宽度是否真的发生变化，
        /// 夹紧在极限位置时不再请求无意义的 Repaint。
        /// </summary>
        private bool ApplySplitterDrag(float mouseX)
        {
            float requestedWidth =
                _splitterDragStartWidth + mouseX - _splitterDragStartMouseX;
            float availableWidth = GetAvailableSplitWidth();
            float width;

            if (availableWidth > 0f)
            {
                width = ClampSplitLeftWidth(
                    requestedWidth,
                    availableWidth,
                    MinLeftPaneWidth,
                    GetMaxSplitLeftWidth(availableWidth));
            }
            else
            {
                // 宽度尚未测量时至少保证左栏不会小于最小宽度。
                width = Mathf.Max(MinLeftPaneWidth, requestedWidth);
            }

            if (Mathf.Abs(width - _splitLeftWidth) < 0.01f)
                return false;

            _splitLeftWidth = width;
            return true;
        }

        private float GetAvailableSplitWidth()
        {
            return ResolveAvailableSplitWidth(_measuredSplitViewWidth, GetSplitViewWidth());
        }

        /// <summary>
        /// 选择本次事件可用的分栏总宽度。布局阶段 GetLastRect 可能返回 1×1，
        /// 这种小于两栏最小宽度之和的值不能参与夹紧，否则拖动会被锁死在
        /// 最小左栏宽度上，只能回退到 currentViewWidth。
        /// </summary>
        internal static float ResolveAvailableSplitWidth(
            float measuredWidth,
            float currentViewWidth)
        {
            if (measuredWidth >= MinSplitViewWidth)
                return measuredWidth;

            return Mathf.Max(0f, currentViewWidth);
        }

        /// <summary>当前设置页可用于左右分栏的总宽度。</summary>
        internal static float GetSplitViewWidth()
        {
            // SettingsProvider 不是 EditorWindow，没有 position；currentViewWidth
            // 就是该设置页可用的视图宽度。
            return Mathf.Max(0f, EditorGUIUtility.currentViewWidth);
        }

        /// <summary>默认左栏宽度：约 32%，并限制在 300～340。</summary>
        internal static float GetDefaultSplitLeftWidth(float availableWidth)
        {
            return Mathf.Clamp(availableWidth * 0.32f, 300f, 340f);
        }

        /// <summary>左栏最大宽度：给右栏和分隔条留出空间。</summary>
        internal static float GetMaxSplitLeftWidth(float availableWidth)
        {
            return Mathf.Max(
                MinLeftPaneWidth,
                availableWidth - MinRightPaneWidth - SplitterWidth);
        }

        /// <summary>
        /// 把请求的左栏宽度夹紧到当前可用宽度内；窗口窄于两栏最小宽度时
        /// 也不会产生负宽度或左右重叠。
        /// </summary>
        internal static float ClampSplitLeftWidth(
            float requested,
            float availableWidth,
            float minLeft,
            float maxLeft)
        {
            float effectiveMax = Mathf.Max(0f, maxLeft);
            float effectiveMin = Mathf.Min(minLeft, effectiveMax);
            return Mathf.Clamp(requested, effectiveMin, effectiveMax);
        }

        private Color GetSplitterColor(bool highlighted)
        {
            if (highlighted)
            {
                Color accent = _accentColor;
                accent.a = EditorGUIUtility.isProSkin ? 0.45f : 0.35f;
                return accent;
            }

            return EditorGUIUtility.isProSkin
                ? new Color(0.5f, 0.5f, 0.5f, 0.5f)
                : new Color(0.4f, 0.4f, 0.4f, 0.5f);
        }

        private void DrawRuleListPane(MenuRedirectSettings settings, ReorderableList ruleList, float width)
        {
            EditorGUILayout.BeginVertical(_paneStyle, GUILayout.Width(width), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L.T("ui.list.title"), _paneTitleStyle, GUILayout.Width(48f));
            EditorGUILayout.LabelField(_ruleCountContent, _mutedLabelStyle, GUILayout.Width(36f));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(
                    GetContent("＋", L.T("ui.list.add.tooltip")),
                    _miniButtonStyle,
                    GUILayout.Width(24f)))
            {
                AddRule(settings);
            }

            EditorGUILayout.EndHorizontal();
            DrawSeparator();
            DrawRuleFilterRow();
            EditorGUILayout.Space(2f);

            _listScroll = EditorGUILayout.BeginScrollView(
                _listScroll,
                false,
                false,
                GUILayout.ExpandHeight(true));

            if (settings.rules.Count == 0)
                DrawListEmptyState(settings);
            else if (_visibleRules.Count == 0)
                DrawFilteredEmptyState();
            else
                ruleList.DoLayoutList();

            EditorGUILayout.EndScrollView();

            string hintKey = settings.rules.Count == 0
                ? "ui.list.hint.empty"
                : IsRuleFilterActive
                    ? "ui.list.hint.filtered"
                    : "ui.list.hint.reorder";

            EditorGUILayout.LabelField(
                IsRuleFilterActive && settings.rules.Count > 0
                    ? L.F(hintKey, _visibleRules.Count, settings.rules.Count)
                    : L.T(hintKey),
                _hintStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawRuleFilterRow()
        {
            EditorGUILayout.BeginHorizontal();

            string search = EditorGUILayout.TextField(
                _ruleSearch,
                EditorStyles.toolbarSearchField,
                GUILayout.ExpandWidth(true));

            if (!string.Equals(search, _ruleSearch, StringComparison.Ordinal))
            {
                _ruleSearch = search;
                _visibleRulesDirty = true;
                Repaint();
            }

            bool onlyIssues = GUILayout.Toggle(
                _showOnlyIssues,
                GetContent("!", L.T("ui.list.filter.issues.tooltip")),
                EditorStyles.toolbarButton,
                GUILayout.Width(22f));

            if (onlyIssues != _showOnlyIssues)
            {
                _showOnlyIssues = onlyIssues;
                _visibleRulesDirty = true;
                Repaint();
            }

            using (new EditorGUI.DisabledScope(!IsRuleFilterActive))
            {
                if (GUILayout.Button(
                        GetContent("✕", L.T("ui.list.filter.clear.tooltip")),
                        _miniButtonStyle,
                        GUILayout.Width(20f)))
                {
                    ClearRuleFilter();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFilteredEmptyState()
        {
            GUILayout.Space(20f);
            EditorGUILayout.LabelField(L.T("ui.list.empty.filtered.title"), _emptyTitleStyle);
            EditorGUILayout.LabelField(L.T("ui.list.empty.filtered.hint"), _emptyHintStyle);
            GUILayout.Space(8f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(L.T("ui.list.filter.clear"), _emptyButtonStyle))
                ClearRuleFilter();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawListEmptyState(MenuRedirectSettings settings)
        {
            GUILayout.Space(20f);
            EditorGUILayout.LabelField(L.T("ui.list.empty.title"), _emptyTitleStyle);
            EditorGUILayout.LabelField(L.T("ui.list.empty.hint"), _emptyHintStyle);
            GUILayout.Space(8f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = _accentColor;
            if (GUILayout.Button(L.T("ui.list.empty.add"), _emptyButtonStyle))
                AddRule(settings);
            GUI.backgroundColor = previousColor;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRuleDetailsPane(MenuRedirectSettings settings)
        {
            EditorGUILayout.BeginVertical(_paneStyle, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(L.T("ui.detail.title"), _paneTitleStyle, GUILayout.Width(88f));
            GUILayout.FlexibleSpace();

            if (_selectedRule != null &&
                GUILayout.Button(
                    GetContent("✕", L.T("ui.detail.delete.tooltip")),
                    _miniButtonStyle,
                    GUILayout.Width(24f)))
            {
                DeleteRule(settings, _selectedRule);
            }

            EditorGUILayout.EndHorizontal();
            DrawSeparator();
            EditorGUILayout.Space(2f);

            if (_selectedRule == null)
            {
                DrawDetailEmptyState();
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(
                _detailScroll,
                false,
                false,
                GUILayout.ExpandHeight(true));

            DrawRuleDetails(settings, _selectedRule);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetailEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(L.T("ui.detail.empty.title"), _emptyTitleStyle);
            EditorGUILayout.LabelField(L.T("ui.detail.empty.hint"), _emptyHintStyle);
            GUILayout.FlexibleSpace();
        }

        private void DrawRuleDetails(MenuRedirectSettings settings, MenuRedirectRule rule)
        {
            int index = settings.rules.IndexOf(rule);
            RuleValidation? validation = index >= 0 ? GetRuleValidationAt(index) : null;
            Color statusColor = GetRuleStatusColor(rule, validation);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            RuleRowDisplay display = GetRuleRowDisplay(rule, validation);
            DrawColoredEditorLabel(
                display.StatusContent,
                _statusGlyphStyle,
                statusColor,
                GUILayout.Width(18f));
            DrawColoredEditorLabel(
                display.PreviewContent,
                _detailPreviewStyle,
                statusColor);
            EditorGUILayout.EndHorizontal();

            DrawSectionTitle(L.T("ui.detail.section.basic"));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            bool enabled = EditorGUILayout.ToggleLeft(
                L.T("ui.detail.enabled"),
                rule.enabled,
                GUILayout.Width(82f));
            GUILayout.Space(8f);
            EditorGUILayout.LabelField(
                L.T("ui.detail.priority"),
                _detailLabelStyle,
                GUILayout.Width(56f));
            int priority = EditorGUILayout.IntField(rule.priority, GUILayout.Width(80f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                rule.enabled = enabled;
                rule.priority = priority;
                MarkDirty();
                BuildRuleValidations();
            }

            EditorGUILayout.LabelField(L.T("ui.detail.priority.hint"), _hintStyle);

            DrawSectionTitle(L.T("ui.detail.section.paths"));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField(L.T("ui.detail.from"), _detailLabelStyle);
            EditorGUILayout.BeginHorizontal();
            string from = EditorGUILayout.TextField(rule.fromPath);
            if (GUILayout.Button(
                    GetContent(
                        L.T("ui.detail.from.pick"),
                        L.T("ui.detail.from.pick.tooltip")),
                    _miniButtonStyle,
                    GUILayout.Width(68f)))
            {
                OpenSourcePicker(rule);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(L.T("ui.detail.to"), _detailLabelStyle);
            EditorGUILayout.BeginHorizontal();
            string to = EditorGUILayout.TextField(rule.toPath);
            if (GUILayout.Button(
                    GetContent(
                        L.T("ui.detail.to.pick"),
                        L.T("ui.detail.to.pick.tooltip")),
                    _miniButtonStyle,
                    GUILayout.Width(68f)))
            {
                OpenTargetParentPicker(rule);
            }

            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                rule.fromPath = from;
                rule.toPath = to;
                MarkDirty();
            }

            EditorGUILayout.LabelField(L.T("ui.detail.paths.hint"), _hintStyle);

            DrawSectionTitle(L.T("ui.detail.section.validation"));
            DrawValidationPanel(rule, validation);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(L.T("ui.detail.apply.hint"), _hintStyle);
        }

        private void OpenSourcePicker(MenuRedirectRule rule)
        {
            MenuRedirectPickerWindow.Open(
                MenuRedirectEngine.Registry,
                rule.fromPath ?? string.Empty,
                selectedPath =>
                {
                    if (_settings == null || !_settings.rules.Contains(rule))
                        return;

                    if (string.Equals(rule.fromPath, selectedPath, StringComparison.Ordinal))
                        return;

                    rule.fromPath = selectedPath;
                    MarkDirty();
                    BuildRuleValidations();
                    Repaint();
                });
        }

        private void OpenTargetParentPicker(MenuRedirectRule rule)
        {
            MenuRegistry registry = MenuRedirectEngine.Registry;
            string source = MenuRegistry.NormalizePath(rule.fromPath);
            string target = MenuRegistry.NormalizePath(rule.toPath);
            string currentParent = registry.IsBranchPath(source)
                ? target
                : GetParentPath(target);

            MenuRedirectPickerWindow.OpenTargetParent(
                registry,
                currentParent,
                selectedParent =>
                {
                    if (_settings == null || !_settings.rules.Contains(rule))
                        return;

                    string currentSource = MenuRegistry.NormalizePath(rule.fromPath);
                    MenuRegistry currentRegistry = MenuRedirectEngine.Registry;
                    bool sourceIsBranch = currentRegistry.IsBranchPath(currentSource);
                    string newTarget = MenuRedirectPickerData.BuildTargetPath(
                        selectedParent,
                        currentSource,
                        rule.toPath,
                        sourceIsBranch);

                    if (!string.Equals(rule.toPath, newTarget, StringComparison.Ordinal))
                    {
                        rule.toPath = newTarget;
                        MarkDirty();
                        BuildRuleValidations();
                    }

                    if (currentSource.Length == 0)
                        SetApplyFeedback(L.Text("ui.detail.to.parentOnly"));

                    Repaint();
                });
        }

        private static string GetParentPath(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? string.Empty : path.Substring(0, slash);
        }

        private void DrawValidationPanel(MenuRedirectRule rule, RuleValidation? validation)
        {
            if (!rule.enabled)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawColoredEditorLabel(
                    L.T("ui.detail.validation.disabled"),
                    _hintStyle,
                    _disabledTextColor);
                EditorGUILayout.EndVertical();
                return;
            }

            if (validation == null || validation.Level == RuleValidationLevel.None)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawColoredEditorLabel(
                    L.T("ui.detail.validation.passed"),
                    _okBodyStyle,
                    _passTextColor);
                EditorGUILayout.EndVertical();
                return;
            }

            bool isError = validation.Level == RuleValidationLevel.Error;
            Color color = isError ? _errorTextColor : _warningTextColor;
            GUIStyle bodyStyle = isError ? _errorBodyStyle : _warningBodyStyle;

            if (!_validationDisplayCache.TryGetValue(
                    validation,
                    out ValidationPanelDisplay? panel))
            {
                panel = new ValidationPanelDisplay();
                panel.Title = new GUIContent(GetF(
                    isError ? "ui.detail.validation.errors" : "ui.detail.validation.warnings",
                    validation.Issues.Count));
                for (int i = 0; i < validation.Issues.Count; i++)
                    panel.Issues.Add(new GUIContent(validation.Issues[i].Message.Resolve()));

                _validationDisplayCache.Add(validation, panel);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawColoredEditorLabel(panel.Title, _detailStatusStyle, color);

            for (int i = 0; i < panel.Issues.Count; i++)
                DrawColoredEditorLabel(panel.Issues[i], bodyStyle, color);

            EditorGUILayout.EndVertical();
        }

        private void DrawColoredEditorLabel(string text, GUIStyle style, Color color)
        {
            DrawColoredEditorLabel(GetContent(text), style, color);
        }

        private void DrawColoredEditorLabel(GUIContent content, GUIStyle style, Color color, params GUILayoutOption[] options)
        {
            EditorGUILayout.LabelField(content, GetColoredStyle(style, color), options);
        }

        private void DrawSectionTitle(string title)
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(title, _sectionTitleStyle);
        }

        private void RefreshVisibleRulesIfNeeded(MenuRedirectSettings settings)
        {
            if (!_visibleRulesDirty)
                return;

            _visibleRulesDirty = false;
            FilterRules(
                settings.rules,
                _ruleValidations,
                _ruleSearch,
                _showOnlyIssues,
                _visibleRules);

            int total = settings.rules.Count;
            int visible = _visibleRules.Count;
            if (total != _ruleCountCache || visible != _visibleRuleCountCache)
            {
                _ruleCountCache = total;
                _visibleRuleCountCache = visible;
                _ruleCountContent = new GUIContent(
                    IsRuleFilterActive ? visible + "/" + total : total.ToString());
            }
        }

        private bool IsRuleFilterActive =>
            _showOnlyIssues || !string.IsNullOrEmpty(_ruleSearch);

        /// <summary>
        /// 按搜索词和“仅问题”过滤规则。搜索同时匹配原路径与新路径，
        /// 不区分大小写；验证级别为警告/错误的规则才通过“仅问题”过滤。
        /// </summary>
        internal static void FilterRules(
            IReadOnlyList<MenuRedirectRule> rules,
            IReadOnlyList<RuleValidation>? validations,
            string? search,
            bool onlyIssues,
            List<MenuRedirectRule> results)
        {
            results.Clear();
            if (rules == null)
                return;

            string term = search?.Trim() ?? string.Empty;

            for (int i = 0; i < rules.Count; i++)
            {
                MenuRedirectRule? rule = rules[i];
                if (rule == null)
                    continue;

                if (onlyIssues)
                {
                    RuleValidation? validation =
                        validations != null && i < validations.Count ? validations[i] : null;
                    if (validation == null ||
                        validation.Level == RuleValidationLevel.None)
                    {
                        continue;
                    }
                }

                if (term.Length > 0 &&
                    (rule.fromPath ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (rule.toPath ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                results.Add(rule);
            }
        }

        private void ClearRuleFilter()
        {
            _ruleSearch = string.Empty;
            _showOnlyIssues = false;
            _visibleRulesDirty = true;
            GUI.FocusControl(null);
            Repaint();
        }

        private ReorderableList EnsureRuleList(MenuRedirectSettings settings)
        {
            if (_ruleList != null && ReferenceEquals(_ruleList.list, _visibleRules))
            {
                _ruleList.draggable = !IsRuleFilterActive;
                return _ruleList;
            }

            _ruleList = new ReorderableList(
                _visibleRules,
                typeof(MenuRedirectRule),
                true,
                false,
                false,
                false)
            {
                showDefaultBackground = true,
                drawElementCallback = DrawRuleElement,
                elementHeightCallback = GetRuleElementHeight,
                onSelectCallback = OnRuleSelected,
                onReorderCallback = OnRulesReordered,
                draggable = !IsRuleFilterActive
            };

            return _ruleList;
        }

        private void DrawRuleElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index < 0 || index >= _visibleRules.Count)
                return;

            MenuRedirectRule? rule = _visibleRules[index];
            if (rule == null)
                return;

            int sourceIndex = _settings == null ? -1 : _settings.rules.IndexOf(rule);
            RuleValidation? validation =
                sourceIndex >= 0 ? GetRuleValidationAt(sourceIndex) : null;
            Color statusColor = GetRuleStatusColor(rule, validation);
            RuleRowDisplay display = GetRuleRowDisplay(rule, validation);

            if (_ruleList != null && _ruleList.index == index)
            {
                Color selection = _accentColor;
                selection.a = _stylesProSkin ? 0.28f : 0.14f;
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, rect.height - 1f), selection);
            }

            if (index < _visibleRules.Count - 1)
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), _separatorColor);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float y = rect.y + (rect.height - lineHeight) * 0.5f;

            const float statusWidth = 18f;
            const float priorityWidth = 28f;
            const float priorityGap = 8f;
            const float removeWidth = 18f;
            const float removeHeight = 16f;
            float removeX = rect.xMax - removeWidth;
            float priorityX = removeX - priorityGap - priorityWidth;
            float textRight = priorityX - 3f;
            float textX = rect.x + statusWidth + 2f;
            float availableTextWidth = Mathf.Max(20f, textRight - textX);

            DrawColoredLabel(
                new Rect(rect.x, y, statusWidth, lineHeight),
                display.StatusContent,
                _statusGlyphStyle,
                statusColor);

            string from = display.Source;

            if (from.Length == 0 || availableTextWidth < 110f)
            {
                DrawColoredLabel(
                    new Rect(textX, y, availableTextWidth, lineHeight),
                    display.PreviewContent,
                    _rowTargetStyle,
                    statusColor);
            }
            else
            {
                float sourceWidth = display.SourceWidth;
                sourceWidth = Mathf.Min(sourceWidth, availableTextWidth * 0.44f);
                sourceWidth = Mathf.Clamp(
                    sourceWidth,
                    Mathf.Min(28f, availableTextWidth),
                    availableTextWidth);

                const float arrowWidth = 18f;
                float targetWidth = availableTextWidth - sourceWidth - arrowWidth - 4f;

                if (targetWidth < 24f)
                {
                    DrawColoredLabel(
                        new Rect(textX, y, availableTextWidth, lineHeight),
                        display.PreviewContent,
                        _rowTargetStyle,
                        statusColor);
                }
                else
                {
                    DrawColoredLabel(
                        new Rect(textX, y, sourceWidth, lineHeight),
                        display.SourceContent,
                        _rowSourceStyle,
                        statusColor);

                    DrawColoredLabel(
                        new Rect(textX + sourceWidth, y, arrowWidth, lineHeight),
                        GetContent("→"),
                        _rowSourceStyle,
                        statusColor);

                    DrawColoredLabel(
                        new Rect(textX + sourceWidth + arrowWidth + 4f, y, targetWidth, lineHeight),
                        display.TargetContent,
                        _rowTargetStyle,
                        statusColor);
                }
            }

            Rect priorityRect = new Rect(priorityX, y, priorityWidth, lineHeight);
            GUI.Label(priorityRect, display.PriorityContent, _priorityStyle);

            if (GUI.Button(
                    new Rect(
                        removeX,
                        rect.y + (rect.height - removeHeight) * 0.5f,
                        removeWidth,
                        removeHeight),
                    GetContent("✕", L.T("ui.row.remove.tooltip")),
                    _rowRemoveButtonStyle))
            {
                _pendingRemoveRule = rule;
                _pendingRemoveSelection = _selectionAtFrameStart;
                GUI.changed = true;
            }
        }

        private RuleRowDisplay GetRuleRowDisplay(MenuRedirectRule rule, RuleValidation? validation)
        {
            if (_rowDisplayCache.TryGetValue(rule, out RuleRowDisplay? display))
                return display;

            display = new RuleRowDisplay
            {
                Source = GetRuleSourceDisplay(rule),
                Target = GetRuleTargetDisplay(rule),
                Preview = GetRulePreviewText(rule),
                PreviewTooltip = GetRulePreviewTooltip(rule),
                StatusGlyph = GetRuleStatusGlyph(rule, validation),
                StatusTooltip = GetRuleStatusTooltip(rule, validation)
            };
            display.StatusContent = new GUIContent(display.StatusGlyph, display.StatusTooltip);
            display.SourceContent = new GUIContent(display.Source, display.PreviewTooltip);
            display.TargetContent = new GUIContent(display.Target, display.PreviewTooltip);
            display.PreviewContent = new GUIContent(display.Preview, display.PreviewTooltip);
            display.PriorityContent = new GUIContent(
                rule.priority.ToString(),
                L.F("ui.row.priority.tooltip", rule.priority));
            display.SourceWidth = _rowSourceStyle.CalcSize(display.SourceContent).x + 2f;

            _rowDisplayCache.Add(rule, display);
            return display;
        }

        private void DrawColoredLabel(Rect rect, GUIContent content, GUIStyle style, Color color)
        {
            EditorGUI.LabelField(rect, content, GetColoredStyle(style, color));
        }

        /// <summary>
        /// 颜色样式按 (基础样式, 颜色) 缓存。原先每个标签都临时改写
        /// GUIStyle.normal.textColor 再还原，规则较多时每帧会产生大量原生样式写操作。
        /// </summary>
        private GUIStyle GetColoredStyle(GUIStyle style, Color color)
        {
            (GUIStyle, Color) key = (style, color);
            if (!_coloredStyles.TryGetValue(key, out GUIStyle? colored))
            {
                colored = new GUIStyle(style);
                colored.normal.textColor = color;
                _coloredStyles.Add(key, colored);
            }

            return colored;
        }

        private float GetRuleElementHeight(int index)
        {
            return RuleRowHeight;
        }

        private void SyncRuleSelection(MenuRedirectSettings settings, ReorderableList list)
        {
            if (_visibleRules.Count == 0)
            {
                if (settings.rules.Count == 0)
                    _selectedRule = null;

                list.index = -1;
                return;
            }

            if (_selectedRule == null)
                _selectedRule = _visibleRules[0];

            int index = _visibleRules.IndexOf(_selectedRule);
            if (index < 0)
            {
                // 当前规则被筛选隐藏时保留详情，只取消列表选中。
                list.index = -1;
                return;
            }

            if (list.index != index)
                list.index = index;
        }

        private void OnRuleSelected(ReorderableList list)
        {
            if (list.index < 0 || list.index >= _visibleRules.Count)
            {
                _selectedRule = null;
                return;
            }

            MenuRedirectRule? selected = _visibleRules[list.index];
            if (!ReferenceEquals(_selectedRule, selected))
                _detailScroll = Vector2.zero;

            _selectedRule = selected;
        }

        private void AddRule(MenuRedirectSettings settings)
        {
            ClearRuleFilter();

            MenuRedirectRule rule = new MenuRedirectRule();
            settings.rules.Add(rule);
            _selectedRule = rule;
            _detailScroll = Vector2.zero;
            _listScroll.y = settings.rules.Count * RuleRowHeight;

            MarkDirty();
            BuildRuleValidations();
        }

        private void DeleteRule(MenuRedirectSettings settings, MenuRedirectRule rule)
        {
            int index = settings.rules.IndexOf(rule);
            if (index < 0)
                return;

            GUI.FocusControl(null);
            settings.rules.RemoveAt(index);

            if (settings.rules.Count == 0)
            {
                _selectedRule = null;
            }
            else
            {
                int nextIndex = Mathf.Clamp(index, 0, settings.rules.Count - 1);
                _selectedRule = settings.rules[nextIndex];
            }

            _detailScroll = Vector2.zero;
            _visibleRulesDirty = true;

            MarkDirty();
            BuildRuleValidations();
        }

        private void ProcessPendingRuleRemoval(MenuRedirectSettings settings)
        {
            MenuRedirectRule? rule = _pendingRemoveRule;
            if (rule == null)
                return;

            MenuRedirectRule? previousSelection = _pendingRemoveSelection;
            _pendingRemoveRule = null;
            _pendingRemoveSelection = null;

            if (!settings.rules.Contains(rule))
                return;

            bool wasSelected = ReferenceEquals(previousSelection, rule);
            DeleteRule(settings, rule);

            if (!wasSelected && previousSelection != null && settings.rules.Contains(previousSelection))
            {
                _selectedRule = previousSelection;
                _visibleRulesDirty = true;
            }

            InternalEditorUtility.RepaintAllViews();
        }

        private void OnRulesReordered(ReorderableList list)
        {
            if (_settings != null && !IsRuleFilterActive)
            {
                // ReorderableList 绑定的是 _visibleRules；无筛选时它就是全部规则。
                // 把新顺序写回配置列表，后续保存/验证仍使用 settings.rules。
                _settings.rules.Clear();
                _settings.rules.AddRange(_visibleRules);

                if (_selectedRule != null)
                {
                    int index = _visibleRules.IndexOf(_selectedRule);
                    if (index >= 0)
                        list.index = index;
                }
            }

            _visibleRulesDirty = true;
            MarkDirty();
            BuildRuleValidations();
        }

        private void Revert(MenuRedirectSettings settings)
        {
            GUI.FocusControl(null);
            settings.rules.Clear();
            for (int i = 0; i < _savedRules.Count; i++)
                settings.rules.Add(CloneRule(_savedRules[i]));

            settings.generatedCodeFolder = _savedGeneratedCodeFolder;
            _selectedRule = null;
            _listScroll = Vector2.zero;
            _detailScroll = Vector2.zero;
            if (_ruleList != null)
                _ruleList.index = -1;

            ClearRuleFilter();
            _dirty = false;
            DiscardDraft();
            SetApplyFeedback(L.Text("apply.reverted"));
            InvalidatePendingCodeChanges();
            BuildRuleValidations();
            RefreshVisibleRulesIfNeeded(settings);
            InternalEditorUtility.RepaintAllViews();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _validationsDirty = true;
            _visibleRulesDirty = true;
            _validationDueAt = EditorApplication.timeSinceStartup + ValidationDebounceSeconds;
            EditorApplication.update -= FlushDeferredValidation;
            EditorApplication.update += FlushDeferredValidation;
            ScheduleDraftSave();
            InvalidatePendingCodeChanges();
            SetApplyFeedback(null);
        }

        private void FlushDeferredValidation()
        {
            // 拖动分隔条时不做整表校验，等拖动结束后再执行，避免中途出现一次性卡顿。
            if (_splitterDragging)
                return;

            if (EditorApplication.timeSinceStartup < _validationDueAt)
                return;

            CancelDeferredValidation();
            if (!_validationsDirty)
                return;

            BuildRuleValidations();
            Repaint();
        }

        private void CancelDeferredValidation()
        {
            EditorApplication.update -= FlushDeferredValidation;
        }

        private void ScheduleDraftSave()
        {
            _draftDueAt = EditorApplication.timeSinceStartup + DraftSaveDebounceSeconds;

            if (_draftSaveScheduled)
                return;

            _draftSaveScheduled = true;
            EditorApplication.update += FlushDraftSave;
        }

        private void FlushDraftSave()
        {
            // 草稿写盘是同步 IO，拖动期间推迟到松开鼠标后，避免打断拖动。
            if (_splitterDragging)
                return;

            if (EditorApplication.timeSinceStartup < _draftDueAt)
                return;

            SaveDraftNow();
        }

        private void CancelDraftSaveTimer()
        {
            if (!_draftSaveScheduled)
                return;

            _draftSaveScheduled = false;
            EditorApplication.update -= FlushDraftSave;
        }

        /// <summary>把当前未保存状态写入 Library；无修改时不保留任何草稿文件。</summary>
        private void SaveDraftNow()
        {
            CancelDraftSaveTimer();

            if (_settings == null || _savedBaselineRules == null)
                return;

            if (!_dirty)
            {
                MenuRedirectDraft.Delete();
                return;
            }

            string savedRulesJson = MenuRedirectRuleFile.ToJson(_savedBaselineRules);
            string generatedFolder = MenuRedirectEngine.NormalizeGeneratedFolder(
                _settings.generatedCodeFolder);

            if (!MenuRedirectDraft.TrySave(
                    savedRulesJson,
                    MenuRedirectEngine.NormalizeGeneratedFolder(_savedBaselineFolder),
                    MenuRedirectRuleFile.ToJson(_settings.rules),
                    generatedFolder,
                    out string? error))
            {
                Debug.LogWarning("[MenuRedirect] draft save failed: " + error);
            }
        }

        private static void SetSavedBaseline(MenuRedirectSettings settings)
        {
            _savedBaselineRules = CloneRules(settings.rules);
            _savedBaselineFolder =
                MenuRedirectEngine.NormalizeGeneratedFolder(settings.generatedCodeFolder);
        }

        private void DiscardDraft()
        {
            CancelDraftSaveTimer();
            MenuRedirectDraft.Delete();
        }

        private void InvalidatePendingCodeChanges()
        {
            _pendingCodeChangesValid = false;
            _pendingCodeChangesNextCheckAt = 0d;
        }

        private bool GetPendingCodeChanges(MenuRedirectSettings settings)
        {
            double now = EditorApplication.timeSinceStartup;
            if (_pendingCodeChangesValid &&
                now < _pendingCodeChangesNextCheckAt)
            {
                return _pendingCodeChanges;
            }

            _pendingCodeChangesNextCheckAt = now + PendingCodeChangesCheckSeconds;
            DateTime fileStamp = GetGeneratedFileStamp(settings);
            if (!_pendingCodeChangesValid || fileStamp != _pendingCodeChangesFileStamp)
            {
                _pendingCodeChanges = MenuRedirectEngine.HasPendingCodeChanges(settings);
                _pendingCodeChangesFileStamp = fileStamp;
                _pendingCodeChangesValid = true;
            }

            return _pendingCodeChanges;
        }

        private DateTime GetGeneratedFileStamp(MenuRedirectSettings settings)
        {
            try
            {
                string path = GetGeneratedCodeFullPath(settings);
                if (path.Length == 0)
                    return DateTime.MinValue;

                return File.Exists(path)
                    ? File.GetLastWriteTimeUtc(path)
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 缓存生成代码的相对路径。保存/应用流程仍直接走引擎，这里只服务于设置页
        /// 的标签与文件时间戳查询，避免每个事件都重新规范化目录字符串。
        /// </summary>
        private string GetGeneratedCodePath(MenuRedirectSettings settings)
        {
            string folder = settings.generatedCodeFolder ?? string.Empty;
            if (!string.Equals(folder, _generatedCodePathFolder, StringComparison.Ordinal))
            {
                _generatedCodePathFolder = folder;
                _generatedCodePath = MenuRedirectEngine.GetGeneratedPath(settings);
            }

            return _generatedCodePath;
        }

        private string GetGeneratedCodeFullPath(MenuRedirectSettings settings)
        {
            string folder = settings.generatedCodeFolder ?? string.Empty;
            if (!string.Equals(
                    folder,
                    _generatedCodeFullPathFolder,
                    StringComparison.Ordinal))
            {
                _generatedCodeFullPathFolder = folder;
                try
                {
                    string projectRoot = GetProjectRoot();
                    _generatedCodeFullPath = projectRoot.Length == 0
                        ? string.Empty
                        : Path.Combine(
                            projectRoot,
                            GetGeneratedCodePath(settings)
                                .Replace('/', Path.DirectorySeparatorChar));
                }
                catch
                {
                    _generatedCodeFullPath = string.Empty;
                }
            }

            return _generatedCodeFullPath;
        }

        private void BuildRuleValidations()
        {
            CancelDeferredValidation();
            _validationsDirty = false;
            _visibleRulesDirty = true;
            _rowDisplayCache.Clear();
            _validationDisplayCache.Clear();
            _validationResult = MenuRedirectValidator.Validate(_settings);
            _ruleValidations = _validationResult.Rules;
            RefreshToolbarStatistics();
        }

        /// <summary>
        /// 工具栏计数只在验证结果变化时更新，避免每个 OnGUI 事件都遍历全部规则。
        /// </summary>
        private void RefreshToolbarStatistics()
        {
            _enabledRuleCount = 0;
            _passRuleCount = 0;
            _warningRuleCount = 0;
            _errorRuleCount = 0;

            List<MenuRedirectRule>? rules = _settings?.rules;
            if (rules == null)
                return;

            for (int i = 0; i < rules.Count; i++)
            {
                MenuRedirectRule? rule = rules[i];
                if (rule == null || !rule.enabled)
                    continue;

                _enabledRuleCount++;
                RuleValidationLevel level =
                    GetRuleValidationAt(i)?.Level ?? RuleValidationLevel.None;
                switch (level)
                {
                    case RuleValidationLevel.Error:
                        _errorRuleCount++;
                        break;
                    case RuleValidationLevel.Warning:
                        _warningRuleCount++;
                        break;
                    default:
                        _passRuleCount++;
                        break;
                }
            }
        }

        private RuleValidation? GetRuleValidationAt(int index)
        {
            return index >= 0 && index < _ruleValidations.Count ? _ruleValidations[index] : null;
        }

        private static string GetRuleSourceDisplay(MenuRedirectRule rule)
        {
            string from = MenuRegistry.NormalizePath(rule.fromPath);
            if (from.Length == 0)
                return string.Empty;

            return MenuRedirectEngine.Registry.IsBranchPath(from) ? from + "/*" : from;
        }

        private static string GetRuleTargetDisplay(MenuRedirectRule rule)
        {
            string from = MenuRegistry.NormalizePath(rule.fromPath);
            if (from.Length == 0)
                return string.Empty;

            string to = MenuRegistry.NormalizePath(rule.toPath);
            if (to.Length == 0)
                return L.T("ui.row.hide");

            return MenuRedirectEngine.Registry.IsBranchPath(from) ? to + "/*" : to;
        }

        private static string GetRulePreviewText(MenuRedirectRule rule)
        {
            string source = GetRuleSourceDisplay(rule);
            if (source.Length == 0)
                return L.T("ui.row.emptySource");

            return source + "  →  " + GetRuleTargetDisplay(rule);
        }

        private static string GetRulePreviewTooltip(MenuRedirectRule rule)
        {
            string from = MenuRegistry.NormalizePath(rule.fromPath);
            if (from.Length == 0)
                return L.T("ui.row.emptySource.tooltip");

            bool isBranch = MenuRedirectEngine.Registry.IsBranchPath(from);
            string fromDisplay = isBranch ? from + "/*" : from;
            string to = MenuRegistry.NormalizePath(rule.toPath);

            if (to.Length == 0)
            {
                return isBranch
                    ? L.F("ui.row.hideBranch.tooltip", from)
                    : L.F("ui.row.hideLeaf.tooltip", from);
            }

            string toDisplay = isBranch ? to + "/*" : to;
            return L.F("ui.row.paths.tooltip", fromDisplay, toDisplay);
        }

        private static string GetRuleStatusGlyph(MenuRedirectRule rule, RuleValidation? validation)
        {
            if (!rule.enabled)
                return "—";

            if (validation == null || validation.Level == RuleValidationLevel.None)
                return "✓";

            return validation.Level == RuleValidationLevel.Error ? "✕" : "!";
        }

        private static string GetRuleStatusTooltip(MenuRedirectRule rule, RuleValidation? validation)
        {
            if (!rule.enabled)
                return L.T("ui.row.status.disabled");

            if (validation == null || validation.Level == RuleValidationLevel.None)
                return L.T("ui.row.status.passed");

            int visibleCount = validation.Issues.Count;
            var messages = new List<string>(visibleCount);

            for (int i = 0; i < validation.Issues.Count; i++)
                messages.Add(validation.Issues[i].Message.Resolve());

            string header = validation.Level == RuleValidationLevel.Error
                ? L.F("ui.row.status.errors", visibleCount)
                : L.F("ui.row.status.warnings", visibleCount);

            return messages.Count == 0
                ? header
                : header + L.T("ui.row.status.details") + string.Join("\n", messages);
        }

        private Color GetRuleStatusColor(MenuRedirectRule rule, RuleValidation? validation)
        {
            if (!rule.enabled)
                return _disabledTextColor;

            if (validation == null || validation.Level == RuleValidationLevel.None)
                return _passTextColor;

            return validation.Level == RuleValidationLevel.Error
                ? _errorTextColor
                : _warningTextColor;
        }

        private static bool RuleIsNull(MenuRedirectRule? rule)
        {
            return rule == null;
        }

        private static bool HasNullRule(List<MenuRedirectRule> rules)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null)
                    return true;
            }

            return false;
        }

        private static MenuRedirectRule CloneRule(MenuRedirectRule source)
        {
            return new MenuRedirectRule
            {
                fromPath = source.fromPath,
                toPath = source.toPath,
                priority = source.priority,
                enabled = source.enabled
            };
        }

        private static List<MenuRedirectRule> CloneRules(List<MenuRedirectRule> source)
        {
            var copy = new List<MenuRedirectRule>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                MenuRedirectRule rule = source[i];
                if (rule != null)
                    copy.Add(CloneRule(rule));
            }

            return copy;
        }

        private void InvalidateCachesIfNeeded()
        {
            if (_widthCacheValid && _widthCacheLanguage == L.CurrentLanguage)
                return;

            _miniButtonWidths.Clear();
            _toolbarButtonWidths.Clear();
            _toolbarCountWidths.Clear();
            _configLabelWidth = 0f;
            _languageLabelWidth = 0f;
            _contentCache.Clear();
            _intFormatCache.Clear();
            _generatedTooltipPath = "\u0000";
            _widthCacheValid = true;
            _widthCacheLanguage = L.CurrentLanguage;
        }

        /// <summary>按本地化 key 缓存 GUIContent；语言切换时整体失效。</summary>
        private GUIContent GetContent(string label)
        {
            return GetContent(label, string.Empty);
        }

        private GUIContent GetContent(string label, string tooltip)
        {
            (string, string) key = (label, tooltip);
            if (!_contentCache.TryGetValue(key, out GUIContent? content))
            {
                content = string.IsNullOrEmpty(tooltip)
                    ? new GUIContent(label)
                    : new GUIContent(label, tooltip);
                _contentCache.Add(key, content);
            }

            return content;
        }

        /// <summary>按 (key, 数值) 缓存本地化格式化结果，避免每帧 string.Format。</summary>
        private string GetF(string key, int value)
        {
            (string, int) cacheKey = (key, value);
            if (!_intFormatCache.TryGetValue(cacheKey, out string? text))
            {
                text = L.F(key, value);
                _intFormatCache.Add(cacheKey, text);
            }

            return text;
        }

        private float GetMiniButtonWidth(float minimum, params string[] keys)
        {
            InvalidateCachesIfNeeded();
            float width = minimum;
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (!_miniButtonWidths.TryGetValue(key, out float cached))
                {
                    cached = Mathf.Ceil(_miniButtonStyle.CalcSize(
                        new GUIContent(L.T(key))).x) + 18f;
                    _miniButtonWidths[key] = cached;
                }

                width = Mathf.Max(width, cached);
            }

            return width;
        }

        private float GetToolbarButtonWidth(float minimum, params string[] keys)
        {
            InvalidateCachesIfNeeded();
            float width = minimum;
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (!_toolbarButtonWidths.TryGetValue(key, out float cached))
                {
                    cached = Mathf.Ceil(_toolbarButtonStyle.CalcSize(
                        new GUIContent(L.T(key))).x) + 18f;
                    _toolbarButtonWidths[key] = cached;
                }

                width = Mathf.Max(width, cached);
            }

            return width;
        }

        private void BrowseGeneratedCodeFolder(MenuRedirectSettings settings)
        {
            string currentFolder = MenuRedirectEngine.NormalizeGeneratedFolder(
                settings.generatedCodeFolder);
            string projectRoot = GetProjectRoot();
            string initialFolder = projectRoot.Length == 0
                ? currentFolder
                : Path.Combine(
                    projectRoot,
                    currentFolder.Replace('/', Path.DirectorySeparatorChar));

            string selected = EditorUtility.OpenFolderPanel(
                L.T("ui.generated.browse.title"),
                initialFolder,
                string.Empty);
            if (string.IsNullOrEmpty(selected))
                return;

            string relative = FileUtil.GetProjectRelativePath(selected);
            if (string.IsNullOrEmpty(relative))
            {
                SetApplyFeedback(
                    L.Text("apply.error.generatedFolder.outsideAssets"),
                    MessageType.Error);
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            relative = relative.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(
                    relative,
                    settings.generatedCodeFolder,
                    StringComparison.Ordinal))
            {
                GUI.FocusControl(null);
                return;
            }

            settings.generatedCodeFolder = relative;
            MarkDirty();

            MenuRedirectText? error = MenuRedirectEngine.ValidateGeneratedFolder(relative);
            if (error != null)
                SetApplyFeedback(error, MessageType.Error);

            GUI.FocusControl(null);
            InternalEditorUtility.RepaintAllViews();
        }

        private void OpenGeneratedCodeDirectory(MenuRedirectSettings settings)
        {
            string projectRoot = GetProjectRoot();
            if (projectRoot.Length == 0)
                return;

            string generatedPath = MenuRedirectEngine.GetGeneratedPath(settings);
            string fullGeneratedPath = Path.Combine(
                projectRoot,
                generatedPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullGeneratedPath))
            {
                EditorUtility.RevealInFinder(fullGeneratedPath);
                return;
            }

            string folder = MenuRedirectEngine.GetGeneratedFolder(settings);
            string fullFolder = Path.Combine(
                projectRoot,
                folder.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(fullFolder))
            {
                EditorUtility.RevealInFinder(fullFolder);
                return;
            }

            string assetsFolder = Path.Combine(projectRoot, "Assets");
            if (Directory.Exists(assetsFolder))
                EditorUtility.RevealInFinder(assetsFolder);
        }

        private void ResetGeneratedCodeFolder(MenuRedirectSettings settings)
        {
            if (string.Equals(
                    settings.generatedCodeFolder,
                    MenuRedirectSettings.DefaultGeneratedCodeFolder,
                    StringComparison.Ordinal))
            {
                return;
            }

            settings.generatedCodeFolder = MenuRedirectSettings.DefaultGeneratedCodeFolder;
            MarkDirty();
            GUI.FocusControl(null);
            InternalEditorUtility.RepaintAllViews();
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        }

        private static void OpenConfigDirectory()
        {
            string projectRoot = GetProjectRoot();
            if (projectRoot.Length == 0)
                return;

            string fullPath = Path.Combine(projectRoot, MenuRedirectSettings.ProjectSettingsPath);
            if (File.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
                return;
            }

            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                EditorUtility.RevealInFinder(directory);
        }

        private void EnsureStyles()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (_stylesInitialized && _stylesProSkin == proSkin)
                return;

            _stylesInitialized = true;
            _stylesProSkin = proSkin;
            _rowDisplayCache.Clear();
            _validationDisplayCache.Clear();
            _coloredStyles.Clear();
            _widthCacheValid = false;

            Color mutedText = proSkin
                ? new Color(0.62f, 0.62f, 0.62f)
                : new Color(0.38f, 0.38f, 0.38f);

            _passTextColor = proSkin
                ? new Color(0.48f, 0.84f, 0.50f)
                : new Color(0.10f, 0.48f, 0.18f);
            _warningTextColor = proSkin
                ? new Color(0.96f, 0.73f, 0.34f)
                : new Color(0.72f, 0.42f, 0.05f);
            _errorTextColor = proSkin
                ? new Color(0.98f, 0.43f, 0.40f)
                : new Color(0.72f, 0.12f, 0.12f);
            _disabledTextColor = proSkin
                ? new Color(0.48f, 0.48f, 0.48f)
                : new Color(0.55f, 0.55f, 0.55f);

            _paneStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 6, 6, 6),
                margin = new RectOffset(0, 0, 0, 0)
            };

            _paneTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12
            };

            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11
            };

            _descriptionStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                wordWrap = true
            };

            _detailLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _detailPreviewStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = false,
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Normal
            };

            _detailStatusStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _statusGlyphStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                padding = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip
            };

            _rowTargetStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                fontStyle = FontStyle.Bold
            };

            _rowSourceStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0)
            };
            _rowSourceStyle.normal.textColor = mutedText;

            _warningBodyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true
            };
            _warningBodyStyle.normal.textColor = _warningTextColor;

            _errorBodyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true
            };
            _errorBodyStyle.normal.textColor = _errorTextColor;

            _okBodyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true
            };
            _okBodyStyle.normal.textColor = _passTextColor;

            _hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
            _hintStyle.normal.textColor = mutedText;

            _mutedLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
            _mutedLabelStyle.normal.textColor = mutedText;

            _mutedRightLabelStyle = new GUIStyle(_mutedLabelStyle)
            {
                alignment = TextAnchor.MiddleRight
            };

            _priorityStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold
            };
            _priorityStyle.normal.textColor = mutedText;

            _emptyTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12
            };

            _emptyHintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            _emptyHintStyle.normal.textColor = mutedText;

            _countStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _rowRemoveButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                fixedHeight = 0,
                fontSize = 10
            };

            _primaryTextColor = proSkin
                ? new Color(0.96f, 0.96f, 0.96f)
                : new Color(0.12f, 0.12f, 0.12f);

            _miniButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 22
            };

            _toolbarButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 22
            };

            _primaryButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 22,
                fontStyle = FontStyle.Bold
            };

            _emptyButtonStyle = new GUIStyle(_primaryButtonStyle)
            {
                fixedHeight = 28,
                fixedWidth = 170,
                fontSize = 12
            };

            _accentColor = proSkin
                ? new Color(0.22f, 0.48f, 0.86f)
                : new Color(0.30f, 0.55f, 0.92f);
            _separatorColor = proSkin
                ? new Color(1f, 1f, 1f, 0.08f)
                : new Color(0f, 0f, 0f, 0.10f);
        }

        private void DrawSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, _separatorColor);
        }

        private void Apply(MenuRedirectSettings settings)
        {
            if (settings.rules == null)
                settings.rules = new List<MenuRedirectRule>();

            if (settings.rules.RemoveAll(RuleIsNull) > 0)
                BuildRuleValidations();

            MenuRedirectEngine.NormalizeRulePaths(settings);
            settings.generatedCodeFolder = MenuRedirectEngine.NormalizeGeneratedFolder(
                settings.generatedCodeFolder);

            // UI、详情面板与 Apply 共用同一份验证结果。
            BuildRuleValidations();
            if (_validationResult == null || _validationResult.HasErrors)
            {
                SetApplyFeedback(
                    _validationResult?.FirstError ??
                    L.Text("apply.error.validation"),
                    MessageType.Error);
                return;
            }

            // 先写包装代码但不隐藏；保存配置成功后再提交文件事务。
            MenuRedirectEngine.GeneratedCodeTransaction? transaction =
                MenuRedirectEngine.PrepareApply(settings);
            if (transaction == null)
            {
                SetApplyFeedback(
                    MenuRedirectEngine.LastCodeGenError ??
                    L.Text("apply.error.codegenFailed"),
                    MessageType.Error);
                InvalidatePendingCodeChanges();
                BuildRuleValidations();
                return;
            }

            try
            {
                settings.Save();
            }
            catch (Exception exception)
            {
                transaction.Rollback();
                SetApplyFeedback(
                    L.Text("apply.error.saveFailed", exception.Message),
                    MessageType.Error);
                InvalidatePendingCodeChanges();
                BuildRuleValidations();
                return;
            }

            // 配置已落盘，更新“还原”快照与跨 provider 的已保存基线，并删除草稿。
            SetSavedBaseline(settings);
            _savedRules = CloneRules(_savedBaselineRules!);
            _savedGeneratedCodeFolder = _savedBaselineFolder;
            _dirty = false;
            DiscardDraft();
            SetApplyFeedback(null);

            if (!transaction.Commit())
            {
                MenuRedirectText commitError = MenuRedirectEngine.LastCodeGenError ??
                                                L.Text("apply.error.codegenFailed");

                // 旧包装文件删除失败时配置已经保存；必须删除本次写入的新文件并把
                // lastGeneratedCodePath 指回旧文件，再保存一次，避免两个包装类共存。
                transaction.RollbackAfterCommitFailure();
                try
                {
                    settings.Save();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(L.F("apply.error.saveFailed", exception.Message));
                }

                // 等路径重新落盘后再刷新资产库，避免刷新触发域重载时丢失回滚后的路径。
                AssetDatabase.Refresh();
                SetApplyFeedback(commitError, MessageType.Error);
                InvalidatePendingCodeChanges();
                BuildRuleValidations();
                return;
            }

            InvalidatePendingCodeChanges();

            MenuRedirectText primaryFeedback;
            MessageType primaryType = MessageType.Info;

            if (MenuRedirectEngine.LastCodeGenChanged)
            {
                // 包装代码刚变化，必须等 Unity 编译和域重载后再隐藏原菜单；
                // Bootstrap 会在确认生成代码与规则一致后重放隐藏。
                primaryFeedback = L.Text(
                    "apply.saved.codegen.deferred",
                    MenuRedirectEngine.GetGeneratedPath(settings));
            }
            else
            {
                MenuRedirectEngine.HideOriginals(settings);

                if (MenuRedirectEngine.LastHiddenCount > 0)
                {
                    primaryFeedback = L.Text(
                        "apply.saved.hidden",
                        MenuRedirectEngine.LastHiddenCount);
                }
                else if (MenuRedirectEngine.LastErrors != null)
                {
                    primaryFeedback = L.Text("apply.saved.withWarnings");
                    primaryType = MessageType.Warning;
                }
                else
                {
                    primaryFeedback = L.Text("apply.saved.done");
                }
            }

            SetApplyFeedback(WithShortcutHint(primaryFeedback), primaryType);
            BuildRuleValidations();
        }

        /// <summary>
        /// 原菜单带快捷键时，在主反馈后追加"快捷键需手动迁移"的提示；
        /// 没有快捷键时原样返回，保持既有反馈文案。
        /// </summary>
        private static MenuRedirectText WithShortcutHint(MenuRedirectText primary)
        {
            if (MenuRedirectEngine.LastShortcutCount == 0)
                return primary;

            return L.Text(
                "apply.feedback.hotkey",
                primary,
                L.Text(
                    "apply.hotkey.hint",
                    MenuRedirectEngine.LastShortcutCount,
                    string.Join(", ", MenuRedirectEngine.LastShortcutTokens)));
        }
    }
}
