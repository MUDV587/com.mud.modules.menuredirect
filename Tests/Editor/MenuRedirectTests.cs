using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MUD.Modules.MenuRedirect.Tests
{
    public sealed class MenuRedirectRegistryTests
    {
        [MenuItem("MUD Tests/Menu Redirect/Multiple First _g")]
        [MenuItem("MUD Tests/Menu Redirect/Multiple Second #%F12")]
        private static void MultipleDeclarations()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Duplicate Declarations")]
        private static void DuplicateFirst()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Duplicate Declarations")]
        private static void DuplicateSecond()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Branch/Child")]
        private static void BranchChild()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Existing Target")]
        private static void ExistingTarget()
        {
        }

        [SetUp]
        public void SetUp()
        {
            MenuRedirectEngine.InvalidateMenuPathCache();
        }

        [TearDown]
        public void TearDown()
        {
            MenuRedirectEngine.InvalidateMenuPathCache();
        }

        [Test]
        public void NormalizePath_StripsNoModifierShortcutAndMnemonic()
        {
            Assert.AreEqual(
                "Tools/Test",
                MenuRegistry.NormalizePath("Tools/Test _g"));
            Assert.AreEqual(
                "Tools/Test",
                MenuRegistry.NormalizePath("Tools/Test #%F12"));
            Assert.AreEqual(
                "Tools/MyItem",
                MenuRegistry.NormalizePath("Tools/My_Item"));
        }

        [Test]
        public void ExtractShortcut_RecognizesModifierAndPlainTokens()
        {
            Assert.AreEqual("_g", MenuRegistry.ExtractShortcut("Tools/Test _g"));
            Assert.AreEqual("#%F12", MenuRegistry.ExtractShortcut("Tools/Test #%F12"));
            Assert.AreEqual("%g", MenuRegistry.ExtractShortcut("Tools/Test %g"));
            Assert.IsNull(MenuRegistry.ExtractShortcut("Tools/Test"));
            Assert.IsNull(MenuRegistry.ExtractShortcut("Tools/My Item"));
            Assert.IsNull(MenuRegistry.ExtractShortcut(null));
        }

        [Test]
        public void FindShortcut_ReturnsRegisteredRawShortcutOrNull()
        {
            MenuRegistry registry = MenuRedirectEngine.Registry;

            Assert.AreEqual(
                "_g",
                registry.FindShortcut("MUD Tests/Menu Redirect/Multiple First"));
            Assert.AreEqual(
                "#%F12",
                registry.FindShortcut("MUD Tests/Menu Redirect/Multiple Second"));
            Assert.IsNull(registry.FindShortcut("MUD Tests/Menu Redirect/Branch/Child"));
            Assert.IsNull(registry.FindShortcut("MUD Tests/Menu Redirect/Unknown Path"));
        }

        [Test]
        public void Build_RegistersEveryMenuItemAttributeOnMethod()
        {
            MenuRegistry registry = MenuRegistry.Build();

            Assert.IsTrue(registry.Contains("MUD Tests/Menu Redirect/Multiple First"));
            Assert.IsTrue(registry.Contains("MUD Tests/Menu Redirect/Multiple Second"));
            Assert.IsTrue(registry.TryGetExecutable(
                "MUD Tests/Menu Redirect/Multiple First",
                out _));
            Assert.IsTrue(registry.TryGetExecutable(
                "MUD Tests/Menu Redirect/Multiple Second",
                out _));
        }

        [Test]
        public void GetExecutableCount_DetectsDuplicateDeclarations()
        {
            MenuRegistry registry = MenuRegistry.Build();

            Assert.AreEqual(
                2,
                registry.GetExecutableCount("MUD Tests/Menu Redirect/Duplicate Declarations"));
        }

        [Test]
        public void GetAllPaths_ReturnsSortedRegisteredPaths()
        {
            List<string> paths = MenuRegistry.Build().GetAllPaths();

            Assert.IsTrue(paths.Contains("MUD Tests/Menu Redirect/Multiple First"));

            for (int i = 1; i < paths.Count; i++)
                Assert.LessOrEqual(string.CompareOrdinal(paths[i - 1], paths[i]), 0);
        }

        [Test]
        public void IsBranchPath_DetectsTopLevelAndNestedContainers()
        {
            MenuRegistry registry = MenuRegistry.Build();

            Assert.IsTrue(registry.IsBranchPath("MUD Tests"));
            Assert.IsTrue(registry.IsBranchPath("MUD Tests/Menu Redirect/Picker Container"));
            Assert.IsFalse(registry.IsBranchPath(
                "MUD Tests/Menu Redirect/Registered Container"));
        }

        [Test]
        public void PickerData_MarksProtectedRootsAndKeepsToolsSelectable()
        {
            List<MenuRedirectPickerEntry> entries =
                MenuRedirectPickerData.Build(MenuRegistry.Build());

            MenuRedirectPickerEntry? tools = entries.Find(entry => entry.Path == "Tools");
            Assert.IsNotNull(tools);
            Assert.IsFalse(tools!.IsProtected);
            Assert.IsTrue(tools.HasExecutable);

            MenuRedirectPickerEntry? window = entries.Find(entry => entry.Path == "Window");
            Assert.IsNotNull(window);
            Assert.IsTrue(window!.IsProtected);
        }

        [Test]
        public void PickerData_IncludesIntermediateContainersAndTheirChildren()
        {
            List<MenuRedirectPickerEntry> entries =
                MenuRedirectPickerData.Build(MenuRegistry.Build());

            MenuRedirectPickerEntry? container = entries.Find(
                entry => entry.Path == "MUD Tests/Menu Redirect/Picker Container");
            Assert.IsNotNull(container);
            Assert.IsTrue(container!.IsBranch);
            Assert.IsFalse(container.IsProtected);
            Assert.IsTrue(container.HasExecutable);

            Assert.IsTrue(entries.Exists(
                entry => entry.Path == "MUD Tests/Menu Redirect/Picker Container/First"));
            Assert.IsTrue(entries.Exists(
                entry => entry.Path == "MUD Tests/Menu Redirect/Picker Container/Second"));
            Assert.IsTrue(entries.Exists(
                entry => entry.Path == "MUD Tests/Menu Redirect"));
        }

        [Test]
        public void PickerSearch_IsCaseInsensitiveSubstring()
        {
            Assert.IsTrue(MenuRedirectPickerData.Matches(
                "Tools/MUD/Menu Redirect",
                "mud"));
            Assert.IsTrue(MenuRedirectPickerData.Matches(
                "Tools/MUD/Menu Redirect",
                "  MENU  "));
            Assert.IsFalse(MenuRedirectPickerData.Matches(
                "Tools/MUD/Menu Redirect",
                "not-registered"));
            Assert.IsTrue(MenuRedirectPickerData.Matches(
                "Tools/MUD/Menu Redirect",
                string.Empty));
        }

        [Test]
        public void PickerParentPaths_KeepTopLevelAndContainersOnly()
        {
            List<MenuRedirectPickerEntry> parents =
                MenuRedirectPickerData.BuildParentPaths(MenuRegistry.Build());

            Assert.IsTrue(parents.Exists(entry => entry.Path == "MUD Tests"));
            Assert.IsTrue(parents.Exists(
                entry => entry.Path == "MUD Tests/Menu Redirect"));
            Assert.IsTrue(parents.Exists(
                entry => entry.Path == "MUD Tests/Menu Redirect/Picker Container"));
            Assert.IsTrue(parents.Exists(
                entry => entry.Path == "MUD Tests/Menu Redirect/Registered Container"));

            Assert.IsFalse(parents.Exists(
                entry => entry.Path == "MUD Tests/Menu Redirect/Multiple First"));
            Assert.IsFalse(parents.Exists(
                entry => entry.Path == "MUD Tests/Menu Redirect/Picker Container/First"));

            MenuRedirectPickerEntry? window =
                parents.Find(entry => entry.Path == "Window");
            Assert.IsNotNull(window);
            Assert.IsTrue(window!.IsProtected);
        }

        [Test]
        public void BuildTargetPath_UsesParentAndPreservesLeafName()
        {
            Assert.AreEqual(
                "Tools/MyTools",
                MenuRedirectPickerData.BuildTargetPath(
                    "Tools/MyTools",
                    "MUD Tests/Menu Redirect",
                    "Old/Path",
                    true));

            Assert.AreEqual(
                "Tools/MyTools/NewName",
                MenuRedirectPickerData.BuildTargetPath(
                    "Tools/MyTools",
                    "MUD Tests/Menu Redirect/Existing Target",
                    "Other/NewName",
                    false));

            Assert.AreEqual(
                "Tools/MyTools/Existing Target",
                MenuRedirectPickerData.BuildTargetPath(
                    "Tools/MyTools",
                    "MUD Tests/Menu Redirect/Existing Target",
                    string.Empty,
                    false));

            Assert.AreEqual(
                "Tools/MyTools",
                MenuRedirectPickerData.BuildTargetPath(
                    "Tools/MyTools",
                    string.Empty,
                    string.Empty,
                    false));

            Assert.AreEqual(
                "Tools/MyTools",
                MenuRedirectPickerData.BuildTargetPath(
                    "Tools/MyTools",
                    string.Empty,
                    "Old/Path",
                    false));

            Assert.AreEqual(
                "Tools/MyTools/Existing Target",
                MenuRedirectPickerData.BuildTargetPath(
                    "Tools/MyTools _g",
                    "MUD Tests/Menu Redirect/Existing Target",
                    string.Empty,
                    false));

            Assert.AreEqual(
                string.Empty,
                MenuRedirectPickerData.BuildTargetPath(
                    string.Empty,
                    "MUD Tests/Menu Redirect/Existing Target",
                    "Old/Path",
                    false));
        }

        [Test]
        public void PickerTree_BuildsHierarchyAndFlattensPreOrder()
        {
            MenuRedirectPickerTree tree = MenuRedirectPickerTree.Build(
                MenuRedirectPickerData.Build(MenuRegistry.Build()));

            MenuRedirectPickerNode? root = tree.Find("MUD Tests");
            Assert.IsNotNull(root);
            Assert.AreEqual(0, root!.Depth);
            Assert.IsTrue(root.Children.Exists(
                child => child.Path == "MUD Tests/Menu Redirect"));

            MenuRedirectPickerNode? container = tree.Find(
                "MUD Tests/Menu Redirect/Picker Container");
            Assert.IsNotNull(container);
            Assert.AreEqual(2, container!.Depth);
            Assert.IsTrue(container.Children.Exists(
                child => child.Path == "MUD Tests/Menu Redirect/Picker Container/First"));

            var collapsed = new HashSet<string>(StringComparer.Ordinal);
            var visible = new List<MenuRedirectPickerNode>();
            tree.CollectVisible(string.Empty, collapsed, visible);

            int rootIndex = visible.FindIndex(node => node.Path == "MUD Tests");
            int childIndex = visible.FindIndex(
                node => node.Path == "MUD Tests/Menu Redirect");
            int leafIndex = visible.FindIndex(
                node => node.Path == "MUD Tests/Menu Redirect/Existing Target");

            Assert.GreaterOrEqual(rootIndex, 0);
            Assert.Greater(childIndex, rootIndex);
            Assert.Greater(leafIndex, childIndex);
        }

        [Test]
        public void PickerTree_CollapsesAndSearchExpandsMatchingAncestors()
        {
            MenuRedirectPickerTree tree = MenuRedirectPickerTree.Build(
                MenuRedirectPickerData.Build(MenuRegistry.Build()));
            var collapsed = new HashSet<string>(StringComparer.Ordinal);
            var visible = new List<MenuRedirectPickerNode>();

            collapsed.Add("MUD Tests/Menu Redirect");
            tree.CollectVisible(string.Empty, collapsed, visible);

            Assert.IsTrue(visible.Exists(
                node => node.Path == "MUD Tests/Menu Redirect"));
            Assert.IsFalse(visible.Exists(
                node => node.Path == "MUD Tests/Menu Redirect/Existing Target"));

            collapsed.Add("MUD Tests");
            tree.ExpandMatches("Existing Target", collapsed);
            tree.CollectVisible("Existing Target", collapsed, visible);

            Assert.IsFalse(collapsed.Contains("MUD Tests"));
            Assert.IsTrue(visible.Exists(node => node.Path == "MUD Tests"));
            Assert.IsTrue(visible.Exists(
                node => node.Path == "MUD Tests/Menu Redirect"));
            Assert.IsTrue(visible.Exists(
                node => node.Path == "MUD Tests/Menu Redirect/Existing Target"));
            Assert.IsFalse(visible.Exists(
                node => node.Path == "MUD Tests/Menu Redirect/Multiple First"));
        }

        [Test]
        public void PickerParentTree_ExcludesNestedLeaves()
        {
            MenuRedirectPickerTree tree = MenuRedirectPickerTree.Build(
                MenuRedirectPickerData.BuildParentPaths(MenuRegistry.Build()));

            Assert.IsNotNull(tree.Find("MUD Tests"));
            Assert.IsNotNull(tree.Find("MUD Tests/Menu Redirect"));
            Assert.IsNotNull(tree.Find(
                "MUD Tests/Menu Redirect/Registered Container"));
            Assert.IsNull(tree.Find(
                "MUD Tests/Menu Redirect/Existing Target"));
            Assert.IsNull(tree.Find(
                "MUD Tests/Menu Redirect/Picker Container/First"));
        }

        [Test]
        public void RuleFile_RoundTripsRulesWithoutMachineSpecificSettings()
        {
            var rules = new List<MenuRedirectRule>
            {
                new MenuRedirectRule
                {
                    fromPath = "Tools/Source",
                    toPath = "Tools/Target",
                    priority = -3,
                    enabled = false
                },
                new MenuRedirectRule
                {
                    fromPath = "Window/Unity CLI Loop",
                    toPath = string.Empty,
                    priority = 7,
                    enabled = true
                }
            };

            string json = MenuRedirectRuleFile.ToJson(rules);

            StringAssert.DoesNotContain("generatedCodeFolder", json);
            StringAssert.DoesNotContain("lastGeneratedCodePath", json);
            Assert.IsTrue(MenuRedirectRuleFile.TryParse(
                json,
                out List<MenuRedirectRule> parsed,
                out MenuRedirectText? error));
            Assert.IsNull(error);
            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual("Tools/Source", parsed[0].fromPath);
            Assert.AreEqual("Tools/Target", parsed[0].toPath);
            Assert.AreEqual(-3, parsed[0].priority);
            Assert.IsFalse(parsed[0].enabled);
            Assert.AreEqual("Window/Unity CLI Loop", parsed[1].fromPath);
            Assert.AreEqual(string.Empty, parsed[1].toPath);
            Assert.AreEqual(7, parsed[1].priority);
            Assert.IsTrue(parsed[1].enabled);
        }

        [Test]
        public void RuleFile_RejectsInvalidJsonFormatVersionAndRules()
        {
            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{",
                out _,
                out MenuRedirectText? jsonError));
            Assert.IsNotNull(jsonError);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"Other\",\"version\":1,\"count\":0,\"rules\":[]}",
                out _,
                out MenuRedirectText? formatError));
            Assert.IsNotNull(formatError);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Rules\",\"version\":99,\"count\":0,\"rules\":[]}",
                out _,
                out MenuRedirectText? versionError));
            Assert.IsNotNull(versionError);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Rules\",\"version\":1}",
                out _,
                out MenuRedirectText? rulesError));
            Assert.IsNotNull(rulesError);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Rules\",\"version\":1,\"rules\":[]}",
                out _,
                out MenuRedirectText? countError));
            Assert.IsNotNull(countError);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Rules\",\"version\":1,\"count\":2,\"rules\":[]}",
                out _,
                out MenuRedirectText? countMismatchError));
            Assert.IsNotNull(countMismatchError);
        }

        [Test]
        public void RuleFile_RejectsInvalidPathsAndDuplicatesWithoutPartialImport()
        {
            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Rules\",\"version\":1,\"count\":1,\"rules\":[" +
                "{\"fromPath\":\"Tools//Source\",\"toPath\":\"Tools/Target\"}]}",
                out List<MenuRedirectRule> invalidPathRules,
                out MenuRedirectText? pathError));
            Assert.IsNotNull(pathError);
            Assert.AreEqual(0, invalidPathRules.Count);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Rules\",\"version\":1,\"count\":1,\"rules\":[" +
                "{\"fromPath\":\"Tools/Source\",\"toPath\":\"Tools//Target\"}]}",
                out List<MenuRedirectRule> invalidTargetRules,
                out MenuRedirectText? targetError));
            Assert.IsNotNull(targetError);
            Assert.AreEqual(0, invalidTargetRules.Count);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Rules\",\"version\":1,\"count\":2,\"rules\":[" +
                "{\"fromPath\":\"Tools/Source\",\"toPath\":\"Tools/Target\"}," +
                "{\"fromPath\":\"Tools/Source\",\"toPath\":\"Tools/Other\"}]}",
                out List<MenuRedirectRule> duplicateRules,
                out MenuRedirectText? duplicateError));
            Assert.IsNotNull(duplicateError);
            Assert.AreEqual(0, duplicateRules.Count);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Rules\",\"version\":1,\"count\":1,\"rules\":[" +
                "{\"fromPath\":\"Tools/Source\",\"toPath\":\"Tools/Source\"}]}",
                out List<MenuRedirectRule> samePathRules,
                out MenuRedirectText? samePathError));
            Assert.IsNotNull(samePathError);
            Assert.AreEqual(0, samePathRules.Count);
        }

        [Test]
        public void RuleFile_DraftParseKeepsIncompleteInput()
        {
            var rules = new List<MenuRedirectRule>
            {
                new MenuRedirectRule
                {
                    fromPath = "Tools//Incomplete",
                    toPath = "Tools/Target",
                    priority = 4,
                    enabled = false
                }
            };

            string json = MenuRedirectRuleFile.ToJson(rules);

            Assert.IsFalse(MenuRedirectRuleFile.TryParse(json, out _, out _));
            Assert.IsTrue(MenuRedirectRuleFile.TryParseDraft(
                json,
                out List<MenuRedirectRule> draft,
                out MenuRedirectText? error));
            Assert.IsNull(error);
            Assert.AreEqual(1, draft.Count);
            Assert.AreEqual("Tools//Incomplete", draft[0].fromPath);
            Assert.AreEqual("Tools/Target", draft[0].toPath);
            Assert.AreEqual(4, draft[0].priority);
            Assert.IsFalse(draft[0].enabled);
        }

        [Test]
        public void Draft_JsonRoundTripsAndMatchesBaseline()
        {
            string savedRules = MenuRedirectRuleFile.ToJson(new List<MenuRedirectRule>
            {
                new MenuRedirectRule { fromPath = "Tools/Saved", toPath = "Tools/Target" }
            });
            string draftRules = MenuRedirectRuleFile.ToJson(new List<MenuRedirectRule>
            {
                new MenuRedirectRule { fromPath = "Tools/Edited", toPath = "Tools/Target" }
            });

            string json = MenuRedirectDraft.ToJson(
                savedRules,
                "Assets/MenuRedirects/Editor",
                draftRules,
                "Assets/Other/Editor");

            Assert.IsTrue(MenuRedirectDraft.TryParse(
                json,
                out MenuRedirectDraftData? draft,
                out string? error));
            Assert.IsNull(error);
            Assert.IsNotNull(draft);
            Assert.AreEqual(draftRules, draft!.draftRulesJson);
            Assert.AreEqual("Assets/Other/Editor", draft.draftGeneratedCodeFolder);
            Assert.IsTrue(draft.MatchesBaseline(
                savedRules,
                "Assets/MenuRedirects/Editor"));
            Assert.IsFalse(draft.MatchesBaseline(
                savedRules,
                "Assets/Different/Editor"));
            Assert.IsFalse(draft.MatchesBaseline(
                MenuRedirectRuleFile.ToJson(new List<MenuRedirectRule>()),
                "Assets/MenuRedirects/Editor"));
        }

        [Test]
        public void Draft_RejectsMissingOrMismatchedFormat()
        {
            Assert.IsFalse(MenuRedirectDraft.TryParse(
                "{}",
                out _,
                out string? formatError));
            Assert.IsNotNull(formatError);

            Assert.IsFalse(MenuRedirectDraft.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Draft\",\"version\":99}",
                out _,
                out string? versionError));
            Assert.IsNotNull(versionError);

            Assert.IsFalse(MenuRedirectDraft.TryParse(
                "{\"format\":\"MUD.MenuRedirect.Draft\",\"version\":1," +
                "\"savedRulesJson\":\"x\"}",
                out _,
                out string? dataError));
            Assert.IsNotNull(dataError);
        }

        [Test]
        public void BuiltInProtection_KeepsToolsRootRemovable()
        {
            Assert.IsTrue(MenuRedirectGuard.IsBuiltInRootMenu("File"));
            Assert.IsTrue(MenuRedirectGuard.IsBuiltInRootMenu("Window"));
            Assert.IsFalse(MenuRedirectGuard.IsBuiltInRootMenu("Tools"));
            Assert.IsFalse(MenuRedirectGuard.IsBuiltInRootMenu("Tools/Custom"));
        }
    }

    public sealed class MenuRedirectAssemblyOwnershipTests
    {
        private const string ProjectRoot = "C:/Project";
        private const string UnityEditorRoot = "C:/Unity/2022.3/Editor";
        private const string UnityContentsRoot = "C:/Unity/2022.3";

        [Test]
        public void UnityEditorInstallAssembly_IsUnityOwned()
        {
            Assert.IsTrue(IsUnityOwned(
                "UnityEditor",
                UnityEditorRoot + "/Data/Managed/UnityEngine/UnityEditor.dll",
                string.Empty,
                string.Empty));
        }

        [Test]
        public void UnityOfficialPackage_WithNonUnityAssemblyName_IsUnityOwned()
        {
            Assert.IsTrue(IsUnityOwned(
                "Cinemachine",
                ProjectRoot + "/Library/ScriptAssemblies/Cinemachine.dll",
                "Packages/com.unity.cinemachine/Runtime/Cinemachine.asmdef",
                "com.unity.cinemachine"));
        }

        [Test]
        public void ThirdPartyPackage_WithUnityAssemblyName_IsNotUnityOwned()
        {
            Assert.IsFalse(IsUnityOwned(
                "Unity.Fake",
                ProjectRoot + "/Library/ScriptAssemblies/Unity.Fake.dll",
                "Packages/com.vendor.fake/Runtime/Unity.Fake.asmdef",
                "com.vendor.fake"));
        }

        [Test]
        public void ProjectAssembly_WithUnityName_IsNotUnityOwned()
        {
            Assert.IsFalse(IsUnityOwned(
                "UnityEngine.Fake",
                ProjectRoot + "/Library/ScriptAssemblies/UnityEngine.Fake.dll",
                "Assets/Plugins/Fake/UnityEngine.Fake.asmdef",
                string.Empty));
        }

        [Test]
        public void ProjectAssemblyWithoutAsmdef_IsNotUnityOwned()
        {
            Assert.IsFalse(IsUnityOwned(
                "Assembly-CSharp-Editor",
                ProjectRoot + "/Library/ScriptAssemblies/Assembly-CSharp-Editor.dll",
                string.Empty,
                string.Empty));
        }

        [Test]
        public void UnityOfficialPackageCacheAssembly_IsUnityOwned()
        {
            Assert.IsTrue(IsUnityOwned(
                "Cinemachine",
                ProjectRoot +
                "/Library/PackageCache/com.unity.cinemachine@2.9.7/Runtime/Cinemachine.dll",
                string.Empty,
                string.Empty));
        }

        [Test]
        public void ThirdPartyPackageCacheAssembly_IsNotUnityOwned()
        {
            Assert.IsFalse(IsUnityOwned(
                "Unity.Fake",
                ProjectRoot +
                "/Library/PackageCache/com.vendor.fake@1.0.0/Runtime/Unity.Fake.dll",
                string.Empty,
                string.Empty));
        }

        [Test]
        public void AsmrefPath_UsesContainingPackageOwnership()
        {
            Assert.IsTrue(IsUnityOwned(
                "Cinemachine",
                string.Empty,
                "Packages/com.unity.cinemachine/Runtime/Cinemachine.asmref",
                string.Empty));

            Assert.IsFalse(IsUnityOwned(
                "Unity.Fake",
                string.Empty,
                "Packages/com.vendor.fake/Runtime/Unity.Fake.asmref",
                string.Empty));
        }

        [Test]
        public void UnknownAssembly_FallsBackToNameHeuristic()
        {
            Assert.IsTrue(IsUnityOwned("Unity.Custom", string.Empty, string.Empty, string.Empty));
            Assert.IsFalse(IsUnityOwned("Custom.Tool", string.Empty, string.Empty, string.Empty));
        }

        [Test]
        public void LoadedAssemblies_ResolveActualOwnership()
        {
            Assert.IsTrue(MenuRedirectGuard.IsUnityAssembly(
                typeof(UnityEditor.Editor).Assembly,
                "UnityEditor"));

            Assert.IsFalse(MenuRedirectGuard.IsUnityAssembly(
                typeof(MenuRedirectGuard).Assembly,
                "MUD.Modules.MenuRedirect.Editor"));
        }

        [Test]
        public void LoadedOfficialPackageAssembly_IsUnityOwned()
        {
            Assembly? assembly = FindLoadedAssembly("UnityEngine.UI");
            if (assembly == null)
                Assert.Ignore("UnityEngine.UI is not loaded in this project.");

            Assert.IsTrue(MenuRedirectGuard.IsUnityAssembly(assembly!, "UnityEngine.UI"));
        }

        [Test]
        public void ProjectMenuItems_AreNotUnityOwned()
        {
            MenuRegistry registry = MenuRegistry.Build();

            Assert.IsNull(registry.FindUnityOwner("MUD Tests/Menu Redirect/Multiple First"));
        }

        private static Assembly? FindLoadedAssembly(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic &&
                    string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal))
                {
                    return assembly;
                }
            }

            return null;
        }

        private static bool IsUnityOwned(
            string assemblyName,
            string assemblyLocation,
            string assemblyDefinitionPath,
            string packageName)
        {
            return MenuRedirectGuard.IsUnityOwnedAssembly(
                assemblyName,
                assemblyLocation,
                assemblyDefinitionPath,
                packageName,
                ProjectRoot,
                UnityEditorRoot,
                UnityContentsRoot);
        }
    }

    public sealed class MenuRedirectValidationTests
    {
        [MenuItem("MUD Tests/Menu Redirect/Validation Source")]
        private static void ValidationSource()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Validation Other Source")]
        private static void ValidationOtherSource()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Validation Existing Target")]
        private static void ValidationExistingTarget()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Validation Branch/Child")]
        private static void ValidationBranchChild()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Picker Container/First")]
        private static void PickerContainerFirst()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Picker Container/Second")]
        private static void PickerContainerSecond()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Registered Container")]
        private static void RegisteredContainer()
        {
        }

        [MenuItem("MUD Tests/Menu Redirect/Registered Container/Child")]
        private static void RegisteredContainerChild()
        {
        }

        [SetUp]
        public void SetUp()
        {
            MenuRedirectEngine.InvalidateMenuPathCache();
        }

        [TearDown]
        public void TearDown()
        {
            MenuRedirectEngine.InvalidateMenuPathCache();
        }

        [Test]
        public void HideOnlyMissingSource_IsWarningNotError()
        {
            MenuRedirectValidationResult result = Validate(
                Rule("MUD Tests/Menu Redirect/Not Registered", string.Empty));

            Assert.IsFalse(result.HasErrors);
            Assert.AreEqual(MenuRedirectIssueLevel.Warning, result.Rules[0].Level);
        }

        [Test]
        public void RedirectMissingSource_IsError()
        {
            MenuRedirectValidationResult result = Validate(
                Rule(
                    "MUD Tests/Menu Redirect/Not Registered",
                    "MUD Tests/Menu Redirect/New Target"));

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(MenuRedirectIssueLevel.Error, result.Rules[0].Level);
        }

        [Test]
        public void RedirectToExistingMenu_IsError()
        {
            MenuRedirectValidationResult result = Validate(
                Rule(
                    "MUD Tests/Menu Redirect/Validation Source",
                    "MUD Tests/Menu Redirect/Validation Existing Target"));

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual("validation.target.exists", result.FirstError?.Key);
        }

        [Test]
        public void RedirectToBuiltInMenu_IsError()
        {
            MenuRedirectValidationResult result = Validate(
                Rule("MUD Tests/Menu Redirect/Validation Source", "File"));

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(MenuRedirectIssueLevel.Error, result.Rules[0].Level);
        }

        [Test]
        public void HideOnlyNestedBuiltInContainer_IsError()
        {
            MenuRedirectValidationResult result = Validate(
                Rule("Window/General", string.Empty));

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(MenuRedirectIssueLevel.Error, result.Rules[0].Level);
        }

        [Test]
        public void RedirectNestedContainer_IsValidBranch()
        {
            MenuRedirectValidationResult result = Validate(
                Rule(
                    "MUD Tests/Menu Redirect/Validation Branch",
                    "MUD Tests/Menu Redirect/Nested Branch Target"));

            Assert.IsFalse(result.HasErrors);
        }

        [Test]
        public void DuplicateSource_IsError()
        {
            MenuRedirectValidationResult result = Validate(
                Rule("MUD Tests/Menu Redirect/Validation Source", string.Empty),
                Rule("MUD Tests/Menu Redirect/Validation Source", string.Empty));

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(MenuRedirectIssueLevel.Error, result.Rules[0].Level);
            Assert.AreEqual(MenuRedirectIssueLevel.Error, result.Rules[1].Level);
        }

        [Test]
        public void OverlappingSources_AreErrors()
        {
            MenuRedirectValidationResult result = Validate(
                Rule("MUD Tests/Menu Redirect/Validation Branch", string.Empty),
                Rule("MUD Tests/Menu Redirect/Validation Branch/Child", string.Empty));

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(MenuRedirectIssueLevel.Error, result.Rules[0].Level);
            Assert.AreEqual(MenuRedirectIssueLevel.Error, result.Rules[1].Level);
        }

        [Test]
        public void InvalidPath_IsError()
        {
            MenuRedirectValidationResult result = Validate(
                Rule("MUD Tests//Invalid", string.Empty));

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(MenuRedirectIssueLevel.Error, result.Rules[0].Level);
        }

        [Test]
        public void HideOnlyMissingSource_DoesNotGetMissingExecutableError()
        {
            MenuRedirectValidationResult result = Validate(
                Rule("MUD Tests/Menu Redirect/Not Registered", string.Empty));

            Assert.IsFalse(result.HasErrors);
            Assert.AreEqual(1, result.Rules[0].Issues.Count);
        }

        private static MenuRedirectRule Rule(string from, string to)
        {
            return new MenuRedirectRule
            {
                fromPath = from,
                toPath = to,
                enabled = true,
            };
        }

        private static MenuRedirectValidationResult Validate(params MenuRedirectRule[] rules)
        {
            return MenuRedirectValidator.Validate(
                new List<MenuRedirectRule>(rules),
                MenuRedirectSettings.DefaultGeneratedCodeFolder);
        }
    }

    public sealed class MenuRedirectApplyTransactionTests
    {
        private const string TestRoot = "Assets/MenuRedirects/__MenuRedirectTests";
        private const string TestFolder = TestRoot + "/Editor";
        private const string TestGeneratedPath = TestFolder + "/" + MenuRedirectSettings.GeneratedCodeFileName;
        private const string NonGeneratedPath = TestRoot + "/Old/" +
                                                MenuRedirectSettings.GeneratedCodeFileName;
        private const string ConflictingGeneratedPath = TestRoot + "/Stray/" +
                                                        MenuRedirectSettings.GeneratedCodeFileName;

        [SetUp]
        public void SetUp()
        {
            DeleteTestRoot();
            MenuRedirectEngine.InvalidateMenuPathCache();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTestRoot();
            MenuRedirectEngine.InvalidateMenuPathCache();
        }

        [Test]
        public void PrepareApply_WritesGeneratedCode_AndRollbackRestoresState()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.generatedCodeFolder = TestFolder;
            settings.lastGeneratedCodePath = MenuRedirectSettings.DefaultGeneratedCodePath;
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Validation Source",
                toPath = "MUD Tests/Menu Redirect/Transaction Target",
                priority = 12,
                enabled = true,
            });

            MenuRedirectEngine.GeneratedCodeTransaction? transaction =
                MenuRedirectEngine.PrepareApply(settings);

            Assert.IsNotNull(transaction);
            Assert.IsTrue(transaction!.WroteFile);
            Assert.IsTrue(File.Exists(ToFullPath(TestGeneratedPath)));
            Assert.AreEqual(TestGeneratedPath, settings.lastGeneratedCodePath);

            transaction.Rollback();

            Assert.IsFalse(File.Exists(ToFullPath(TestGeneratedPath)));
            Assert.AreEqual(
                MenuRedirectSettings.DefaultGeneratedCodePath,
                settings.lastGeneratedCodePath);
        }

        [Test]
        public void PrepareApply_AbortsWhenOldWrapperIsNotGeneratedByTool()
        {
            string fullNonGeneratedPath = ToFullPath(NonGeneratedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullNonGeneratedPath)!);
            File.WriteAllText(fullNonGeneratedPath, "// user file\n");

            MenuRedirectSettings settings = CreateSettings();
            settings.generatedCodeFolder = TestFolder;
            settings.lastGeneratedCodePath = NonGeneratedPath;
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Validation Source",
                toPath = "MUD Tests/Menu Redirect/Transaction Target",
                enabled = true,
            });

            MenuRedirectEngine.GeneratedCodeTransaction? transaction =
                MenuRedirectEngine.PrepareApply(settings);

            Assert.IsNull(transaction);
            Assert.IsNotNull(MenuRedirectEngine.LastCodeGenError);
            Assert.IsFalse(File.Exists(ToFullPath(TestGeneratedPath)));
        }

        [Test]
        public void PrepareApply_AbortsWhenAnotherGeneratedWrapperRemains()
        {
            string fullConflictingPath = ToFullPath(ConflictingGeneratedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullConflictingPath)!);
            File.WriteAllText(
                fullConflictingPath,
                "// <auto-generated>由 MUD Menu Redirect 生成，请勿手动修改。</auto-generated>\n");

            MenuRedirectSettings settings = CreateSettings();
            settings.generatedCodeFolder = TestFolder;
            settings.lastGeneratedCodePath = MenuRedirectSettings.DefaultGeneratedCodePath;
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Validation Source",
                toPath = "MUD Tests/Menu Redirect/Transaction Target",
                enabled = true,
            });

            MenuRedirectEngine.GeneratedCodeTransaction? transaction =
                MenuRedirectEngine.PrepareApply(settings);

            Assert.IsNull(transaction);
            Assert.IsNotNull(MenuRedirectEngine.LastCodeGenError);
            Assert.IsFalse(File.Exists(ToFullPath(TestGeneratedPath)));
        }

        [Test]
        public void Commit_RechecksOldWrapperBeforeDeleting()
        {
            string fullOldPath = ToFullPath(NonGeneratedPath);
            string fullNewPath = ToFullPath(TestGeneratedPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOldPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(fullNewPath)!);

            const string header =
                "// <auto-generated>由 MUD Menu Redirect 生成，请勿手动修改。</auto-generated>";
            File.WriteAllText(fullOldPath, header + "\n");
            File.WriteAllText(fullNewPath, header + "\n");

            MenuRedirectSettings settings = CreateSettings();
            settings.lastGeneratedCodePath = TestGeneratedPath;

            // 直接构造事务，避免扫描到项目默认包装文件而提前中止。
            var transaction = new MenuRedirectEngine.GeneratedCodeTransaction(
                settings,
                NonGeneratedPath,
                NonGeneratedPath,
                fullNewPath,
                fullOldPath,
                true,
                true,
                false,
                true,
                true,
                null);

            // 模拟 Prepare 与 Commit 之间旧文件被用户替换。
            File.WriteAllText(fullOldPath, "// user file\n");

            Assert.IsFalse(transaction.Commit());
            Assert.IsTrue(File.Exists(fullOldPath));
            Assert.AreEqual("// user file\n", File.ReadAllText(fullOldPath));

            transaction.RollbackAfterCommitFailure();
            Assert.IsFalse(File.Exists(fullNewPath));
            Assert.AreEqual(NonGeneratedPath, settings.lastGeneratedCodePath);
        }

        [Test]
        public void Evaluate_BlocksTargetUnderSourceForLegacyConfig()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Validation Source",
                toPath = "MUD Tests/Menu Redirect/Validation Source/Child",
                enabled = true,
            });

            List<MenuRedirectEngine.RuleEvaluation> evaluations =
                MenuRedirectEngine.Evaluate(settings);

            Assert.AreEqual(1, evaluations.Count);
            Assert.IsTrue(evaluations[0].Blocked);
        }

        [Test]
        public void PrepareApply_RejectsFolderWithoutEditorSegment()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.generatedCodeFolder = "Assets/MenuRedirects/__MenuRedirectTests/NotEditor";
            settings.lastGeneratedCodePath = MenuRedirectSettings.DefaultGeneratedCodePath;

            MenuRedirectEngine.GeneratedCodeTransaction? transaction =
                MenuRedirectEngine.PrepareApply(settings);

            Assert.IsNull(transaction);
            Assert.IsNotNull(MenuRedirectEngine.LastCodeGenError);
            Assert.IsFalse(Directory.Exists(ToFullPath("Assets/MenuRedirects/__MenuRedirectTests/NotEditor")));
        }

        [Test]
        public void Evaluate_BlocksInvalidLegacyTargetPath()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Validation Source",
                toPath = "MUD Tests/Menu Redirect//Invalid Target",
                enabled = true,
            });

            List<MenuRedirectEngine.RuleEvaluation> evaluations =
                MenuRedirectEngine.Evaluate(settings);

            Assert.AreEqual(1, evaluations.Count);
            Assert.IsTrue(evaluations[0].Blocked);
        }

        [Test]
        public void Evaluate_NestedContainer_MovesDescendants()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Validation Branch",
                toPath = "MUD Tests/Menu Redirect/Nested Branch Target",
                enabled = true,
            });

            List<MenuRedirectEngine.RuleEvaluation> evaluations =
                MenuRedirectEngine.Evaluate(settings);

            Assert.AreEqual(1, evaluations.Count);
            Assert.IsFalse(evaluations[0].Blocked);
            Assert.AreEqual(1, evaluations[0].Moved.Count);
            Assert.AreEqual(
                "MUD Tests/Menu Redirect/Validation Branch/Child",
                evaluations[0].Moved[0].child);
            Assert.AreEqual(
                "MUD Tests/Menu Redirect/Nested Branch Target/Child",
                evaluations[0].Moved[0].target);
        }

        [Test]
        public void GeneratedCodeReady_RequiresMatchingFile()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.generatedCodeFolder = TestFolder;
            settings.lastGeneratedCodePath = TestGeneratedPath;
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Validation Source",
                toPath = "MUD Tests/Menu Redirect/Transaction Target",
                enabled = true,
            });

            Assert.IsFalse(MenuRedirectEngine.IsGeneratedCodeReady(settings, out _));

            Directory.CreateDirectory(Path.GetDirectoryName(ToFullPath(TestGeneratedPath))!);
            File.WriteAllText(
                ToFullPath(TestGeneratedPath),
                MenuRedirectEngine.BuildGeneratedContent(settings));

            Assert.IsTrue(MenuRedirectEngine.IsGeneratedCodeReady(settings, out _));

            File.AppendAllText(ToFullPath(TestGeneratedPath), "// stale\n");
            Assert.IsFalse(MenuRedirectEngine.IsGeneratedCodeReady(settings, out _));
        }

        [Test]
        public void GeneratedCode_WritesShortcutHintOnlyForHotkeySources()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Multiple First _g",
                toPath = "MUD Tests/Menu Redirect/Shortcut Target",
                enabled = true,
            });
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Branch/Child",
                toPath = "MUD Tests/Menu Redirect/Child Shortcut Target",
                enabled = true,
            });

            string content = MenuRedirectEngine.BuildGeneratedContent(settings);

            StringAssert.Contains("原菜单快捷键", content);
            StringAssert.Contains("_g", content);
            Assert.AreEqual(
                content.IndexOf("原菜单快捷键", StringComparison.Ordinal),
                content.LastIndexOf("原菜单快捷键", StringComparison.Ordinal),
                "只有带快捷键的源应写快捷键注释");
            Assert.AreEqual(1, MenuRedirectEngine.LastShortcutCount);
            CollectionAssert.AreEqual(
                new[] { "_g" },
                new List<string>(MenuRedirectEngine.LastShortcutTokens));
        }

        [Test]
        public void GeneratedCode_HasNoShortcutHint_WhenSourceHasNone()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.rules.Add(new MenuRedirectRule
            {
                fromPath = "MUD Tests/Menu Redirect/Branch/Child",
                toPath = "MUD Tests/Menu Redirect/Plain Target",
                enabled = true,
            });

            string content = MenuRedirectEngine.BuildGeneratedContent(settings);

            StringAssert.DoesNotContain("原菜单快捷键", content);
            Assert.AreEqual(0, MenuRedirectEngine.LastShortcutCount);
            Assert.IsEmpty(MenuRedirectEngine.LastShortcutTokens);
        }

        private static MenuRedirectSettings CreateSettings()
        {
            // 测试实例不能经由 ScriptableSingleton.instance 创建，也不调用 Save；
            // 这里只验证文件事务，因此用未初始化对象承载字段。
            MenuRedirectSettings settings =
                (MenuRedirectSettings)FormatterServices.GetUninitializedObject(
                    typeof(MenuRedirectSettings));
            settings.rules = new List<MenuRedirectRule>();
            settings.generatedCodeFolder = TestFolder;
            settings.lastGeneratedCodePath = MenuRedirectSettings.DefaultGeneratedCodePath;
            return settings;
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 string.Empty;
            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void DeleteTestRoot()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.DeleteAsset(TestRoot);

            if (Directory.Exists(ToFullPath(TestRoot)))
                Directory.Delete(ToFullPath(TestRoot), true);
        }
    }

    public sealed class MenuRedirectFeedbackTests
    {
        [Test]
        public void TryBuildFeedback_DeduplicatesLines_AndUsesHighestSeverity()
        {
            bool hasFeedback = MenuRedirectSettingsProvider.TryBuildFeedback(
                "保存失败\n同一行",
                MessageType.Error,
                "同一行",
                "警告一\n同一行",
                out string text,
                out MessageType type);

            Assert.IsTrue(hasFeedback);
            Assert.AreEqual(MessageType.Error, type);
            StringAssert.Contains("保存失败", text);
            StringAssert.Contains("警告一", text);
            Assert.AreEqual(1, CountOccurrences(text, "同一行"));
        }

        [Test]
        public void TryBuildFeedback_WarningWinsOverInfo()
        {
            bool hasFeedback = MenuRedirectSettingsProvider.TryBuildFeedback(
                "已还原",
                MessageType.Info,
                "目录无效",
                null,
                out string text,
                out MessageType type);

            Assert.IsTrue(hasFeedback);
            Assert.AreEqual(MessageType.Warning, type);
            StringAssert.Contains("已还原", text);
            StringAssert.Contains("目录无效", text);
        }

        [Test]
        public void TryBuildFeedback_ReturnsFalse_WhenAllMessagesEmpty()
        {
            bool hasFeedback = MenuRedirectSettingsProvider.TryBuildFeedback(
                null,
                MessageType.Info,
                "  ",
                string.Empty,
                out string text,
                out _);

            Assert.IsFalse(hasFeedback);
            Assert.IsTrue(string.IsNullOrEmpty(text));
        }

        [Test]
        public void HotkeyFeedback_CombinesPrimaryAndHint_InBothLanguages()
        {
            var languages = new[]
            {
                MenuRedirectLanguage.Chinese,
                MenuRedirectLanguage.English,
            };

            foreach (MenuRedirectLanguage language in languages)
            {
                string hint = MenuRedirectLocalization.Format(
                    language,
                    "apply.hotkey.hint",
                    2,
                    "_g, #%F12");
                Assert.AreNotEqual("apply.hotkey.hint", hint, language.ToString());
                StringAssert.Contains("_g", hint, language.ToString());

                string primary = MenuRedirectLocalization.Format(language, "apply.saved.done");
                string combined = MenuRedirectLocalization.Format(
                    language,
                    "apply.feedback.hotkey",
                    MenuRedirectLocalization.Text("apply.saved.done"),
                    MenuRedirectLocalization.Text("apply.hotkey.hint", 2, "_g, #%F12"));

                Assert.AreNotEqual("apply.feedback.hotkey", combined, language.ToString());
                StringAssert.Contains(primary, combined, language.ToString());
                StringAssert.Contains("_g", combined, language.ToString());
            }
        }

        [Test]
        public void CapFeedback_LimitsLines_AndReportsRemaining()
        {
            string text = "1\n2\n3\n4\n5\n6\n7\n8";

            string capped = MenuRedirectSettingsProvider.CapFeedback(
                text,
                6,
                600,
                count => "还有 " + count + " 条",
                out bool truncated);

            Assert.IsTrue(truncated);
            StringAssert.Contains("还有 2 条", capped);
            Assert.AreEqual(7, capped.Split('\n').Length);
        }

        [Test]
        public void CapFeedback_LimitsCharacters()
        {
            var lines = new string[20];
            for (int i = 0; i < lines.Length; i++)
                lines[i] = new string('x', 40);

            string capped = MenuRedirectSettingsProvider.CapFeedback(
                string.Join("\n", lines),
                20,
                100,
                count => "还有 " + count + " 条",
                out bool truncated);

            Assert.IsTrue(truncated);
            StringAssert.Contains("还有 18 条", capped);
            Assert.AreEqual(3, capped.Split('\n').Length);
        }

        [Test]
        public void CapFeedback_ReturnsOriginalWhenWithinLimits()
        {
            string text = "第一行\n第二行";

            string capped = MenuRedirectSettingsProvider.CapFeedback(
                text,
                6,
                600,
                _ => "更多",
                out bool truncated);

            Assert.IsFalse(truncated);
            Assert.AreEqual(text, capped);
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }

    public sealed class MenuRedirectCleanupTests
    {
        private const string TestRoot = "Assets/MenuRedirects/__MenuRedirectCleanupTests";
        private const string CurrentFolder = TestRoot + "/Current/Editor";
        private const string OldFolder = TestRoot + "/Old/Editor";
        private static readonly string CurrentPath =
            CurrentFolder + "/" + MenuRedirectSettings.GeneratedCodeFileName;
        private static readonly string OldPath =
            OldFolder + "/" + MenuRedirectSettings.GeneratedCodeFileName;
        private static readonly string UserPath = CurrentFolder + "/UserFile.cs";

        [SetUp]
        public void SetUp()
        {
            DeleteTestRoot();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTestRoot();
        }

        [Test]
        public void CollectCandidatePaths_IncludesCurrentAndPrevious()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.generatedCodeFolder = CurrentFolder;
            settings.lastGeneratedCodePath = OldPath;

            List<string> paths = MenuRedirectCleanup.CollectCandidatePaths(settings);

            CollectionAssert.AreEquivalent(new[] { CurrentPath, OldPath }, paths);
        }

        [Test]
        public void CollectCandidatePaths_DeduplicatesSamePath()
        {
            MenuRedirectSettings settings = CreateSettings();
            settings.generatedCodeFolder = CurrentFolder;
            settings.lastGeneratedCodePath = CurrentPath;

            List<string> paths = MenuRedirectCleanup.CollectCandidatePaths(settings);

            Assert.AreEqual(1, paths.Count);
            Assert.AreEqual(CurrentPath, paths[0]);
        }

        [Test]
        public void TryDeleteOwnedGeneratedFile_DeletesGeneratedWrapper()
        {
            MenuRedirectSettings settings = CreateSettings();
            WriteFile(CurrentPath, MenuRedirectEngine.BuildGeneratedContent(settings));

            Assert.IsTrue(MenuRedirectCleanup.IsOwnedGeneratedFile(CurrentPath));
            Assert.IsTrue(MenuRedirectCleanup.TryDeleteOwnedGeneratedFile(
                CurrentPath,
                out string error));

            Assert.AreEqual(string.Empty, error);
            Assert.IsFalse(File.Exists(ToFullPath(CurrentPath)));
        }

        [Test]
        public void TryDeleteOwnedGeneratedFile_RefusesUserFileWithGeneratedName()
        {
            WriteFile(CurrentPath, "// user file\n");

            Assert.IsFalse(MenuRedirectCleanup.IsOwnedGeneratedFile(CurrentPath));
            Assert.IsFalse(MenuRedirectCleanup.TryDeleteOwnedGeneratedFile(
                CurrentPath,
                out string error));

            Assert.IsNotEmpty(error);
            Assert.IsTrue(File.Exists(ToFullPath(CurrentPath)));
            Assert.AreEqual("// user file\n", File.ReadAllText(ToFullPath(CurrentPath)));
        }

        [Test]
        public void TryDeleteOwnedGeneratedFile_RefusesUnexpectedFileName()
        {
            WriteFile(UserPath, MenuRedirectEngine.BuildGeneratedContent(CreateSettings()));

            Assert.IsFalse(MenuRedirectCleanup.IsOwnedGeneratedFile(UserPath));
            Assert.IsFalse(MenuRedirectCleanup.TryDeleteOwnedGeneratedFile(
                UserPath,
                out string error));

            Assert.IsNotEmpty(error);
            Assert.IsTrue(File.Exists(ToFullPath(UserPath)));
        }

        [Test]
        public void CleanupLocalizationKeys_ExistInBothLanguages()
        {
            string[] keys =
            {
                "cleanup.nothing.title",
                "cleanup.nothing.body",
                "cleanup.kept.body",
                "cleanup.confirm.title",
                "cleanup.confirm.body",
                "cleanup.confirm.configKept",
                "cleanup.confirm.noConfig",
                "cleanup.confirm.ok",
                "cleanup.confirm.cancel",
                "cleanup.result.title",
                "cleanup.result.body",
                "cleanup.result.failures",
                "cleanup.result.ok",
                "cleanup.error.notAsset",
                "cleanup.error.unexpectedFileName",
                "cleanup.error.notGenerated",
                "cleanup.error.deleteFailed",
            };

            var languages = new[]
            {
                MenuRedirectLanguage.Chinese,
                MenuRedirectLanguage.English,
            };

            foreach (MenuRedirectLanguage language in languages)
            {
                foreach (string key in keys)
                {
                    string text = MenuRedirectLocalization.Format(language, key, "x", "y");
                    Assert.AreNotEqual(key, text, language + ": " + key);
                    Assert.IsNotEmpty(text, language + ": " + key);
                }
            }
        }

        private static MenuRedirectSettings CreateSettings()
        {
            MenuRedirectSettings settings =
                (MenuRedirectSettings)FormatterServices.GetUninitializedObject(
                    typeof(MenuRedirectSettings));
            settings.rules = new List<MenuRedirectRule>();
            settings.generatedCodeFolder = CurrentFolder;
            settings.lastGeneratedCodePath = OldPath;
            return settings;
        }

        private static void WriteFile(string assetPath, string content)
        {
            string fullPath = ToFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 string.Empty;
            return Path.Combine(
                projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void DeleteTestRoot()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.DeleteAsset(TestRoot);

            if (Directory.Exists(ToFullPath(TestRoot)))
                Directory.Delete(ToFullPath(TestRoot), true);
        }
    }

    public sealed class MenuRedirectSettingsProviderCacheTests
    {
        private static MenuRedirectSettingsProvider CreateProvider()
        {
            MethodInfo factory = typeof(MenuRedirectSettingsProvider).GetMethod(
                "CreateSettingsProvider",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(factory);
            return (MenuRedirectSettingsProvider)factory!.Invoke(null, null)!;
        }

        [Test]
        public void GetContent_ReusesContentPerLabelAndTooltip()
        {
            MenuRedirectSettingsProvider provider = CreateProvider();

            MethodInfo byLabel = typeof(MenuRedirectSettingsProvider).GetMethod(
                "GetContent",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null)!;
            MethodInfo byPair = typeof(MenuRedirectSettingsProvider).GetMethod(
                "GetContent",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string) },
                null)!;

            var first = (GUIContent)byLabel.Invoke(provider, new object[] { "label" })!;
            var second = (GUIContent)byLabel.Invoke(provider, new object[] { "label" })!;
            Assert.AreSame(first, second);

            var withTooltip = (GUIContent)byPair.Invoke(
                provider,
                new object[] { "label", "tip" })!;
            var withTooltipAgain = (GUIContent)byPair.Invoke(
                provider,
                new object[] { "label", "tip" })!;
            Assert.AreNotSame(first, withTooltip);
            Assert.AreSame(withTooltip, withTooltipAgain);
            Assert.AreEqual(string.Empty, first.tooltip);
            Assert.AreEqual("tip", withTooltip.tooltip);
        }

        [Test]
        public void GetF_CachesFormattedTextPerKeyAndValue()
        {
            MenuRedirectSettingsProvider provider = CreateProvider();

            MethodInfo getF = typeof(MenuRedirectSettingsProvider).GetMethod(
                "GetF",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(int) },
                null)!;

            var first = (string)getF.Invoke(provider, new object[] { "ui.toolbar.enabled", 3 })!;
            var second = (string)getF.Invoke(provider, new object[] { "ui.toolbar.enabled", 3 })!;
            var other = (string)getF.Invoke(provider, new object[] { "ui.toolbar.enabled", 4 })!;

            Assert.AreSame(first, second);
            Assert.AreNotSame(first, other);
            Assert.AreNotEqual(first, other);
        }

        [Test]
        public void FilterRules_MatchesSourceAndTargetCaseInsensitively()
        {
            var rules = new List<MenuRedirectRule>
            {
                new MenuRedirectRule { fromPath = "Tools/Alpha", toPath = "Tools/Beta" },
                new MenuRedirectRule { fromPath = "Window/Gamma", toPath = string.Empty },
                new MenuRedirectRule { fromPath = "Tools/Delta", toPath = "Other/Epsilon" }
            };
            var visible = new List<MenuRedirectRule>();

            MenuRedirectSettingsProvider.FilterRules(
                rules,
                null,
                "  bEtA ",
                false,
                visible);
            CollectionAssert.AreEqual(new[] { rules[0] }, visible);

            MenuRedirectSettingsProvider.FilterRules(
                rules,
                null,
                "gamma",
                false,
                visible);
            CollectionAssert.AreEqual(new[] { rules[1] }, visible);

            MenuRedirectSettingsProvider.FilterRules(
                rules,
                null,
                "tools",
                false,
                visible);
            CollectionAssert.AreEqual(new[] { rules[0], rules[2] }, visible);
        }

        [Test]
        public void FilterRules_OnlyIssuesKeepsWarningsAndErrors()
        {
            var rules = new List<MenuRedirectRule>
            {
                new MenuRedirectRule { fromPath = "Tools/Ok" },
                new MenuRedirectRule { fromPath = "Tools/Warning" },
                new MenuRedirectRule { fromPath = "Tools/Error" }
            };
            var validations = new List<MenuRedirectRuleValidation>
            {
                new MenuRedirectRuleValidation(),
                new MenuRedirectRuleValidation(),
                new MenuRedirectRuleValidation()
            };
            validations[1].Add(
                MenuRedirectIssueLevel.Warning,
                MenuRedirectLocalization.Text("ui.list.hint.reorder"));
            validations[2].Add(
                MenuRedirectIssueLevel.Error,
                MenuRedirectLocalization.Text("ui.list.hint.reorder"));

            var visible = new List<MenuRedirectRule>();

            MenuRedirectSettingsProvider.FilterRules(
                rules,
                validations,
                string.Empty,
                true,
                visible);
            CollectionAssert.AreEqual(new[] { rules[1], rules[2] }, visible);

            MenuRedirectSettingsProvider.FilterRules(
                rules,
                validations,
                "error",
                true,
                visible);
            CollectionAssert.AreEqual(new[] { rules[2] }, visible);
        }

        [Test]
        public void FilterRules_NullRulesAreIgnored()
        {
            var rules = new List<MenuRedirectRule>
            {
                new MenuRedirectRule { fromPath = "Tools/Alpha" },
                null!
            };
            var visible = new List<MenuRedirectRule>();

            MenuRedirectSettingsProvider.FilterRules(
                rules,
                null,
                string.Empty,
                false,
                visible);

            CollectionAssert.AreEqual(new[] { rules[0] }, visible);
        }
    }

    public sealed class MenuRedirectSplitViewTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void DefaultLeftWidth_UsesProportionWithinBounds()
        {
            Assert.AreEqual(
                320f,
                MenuRedirectSettingsProvider.GetDefaultSplitLeftWidth(1000f),
                Tolerance);
            Assert.AreEqual(
                300f,
                MenuRedirectSettingsProvider.GetDefaultSplitLeftWidth(600f),
                Tolerance);
            Assert.AreEqual(
                340f,
                MenuRedirectSettingsProvider.GetDefaultSplitLeftWidth(2000f),
                Tolerance);
        }

        [Test]
        public void MaxLeftWidth_LeavesRoomForRightPaneAndSplitter()
        {
            const float availableWidth = 1000f;
            float maxLeft = MenuRedirectSettingsProvider.GetMaxSplitLeftWidth(availableWidth);

            Assert.AreEqual(754f, maxLeft, Tolerance);
            Assert.AreEqual(
                MenuRedirectSettingsProvider.MinRightPaneWidth,
                availableWidth - maxLeft - MenuRedirectSettingsProvider.SplitterWidth,
                Tolerance);
        }

        [Test]
        public void ClampLeftWidth_KeepsNormalRequest()
        {
            Assert.AreEqual(
                320f,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(320f, 1000f, 220f, 754f),
                Tolerance);
        }

        [Test]
        public void ClampLeftWidth_RaisesRequestBelowMinimum()
        {
            Assert.AreEqual(
                MenuRedirectSettingsProvider.MinLeftPaneWidth,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(100f, 1000f, 220f, 754f),
                Tolerance);
        }

        [Test]
        public void ClampLeftWidth_LowersRequestAboveMaximum()
        {
            Assert.AreEqual(
                754f,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(900f, 1000f, 220f, 754f),
                Tolerance);
        }

        [Test]
        public void ClampLeftWidth_NarrowWindowNeverProducesNegativeWidth()
        {
            float maxLeft = MenuRedirectSettingsProvider.GetMaxSplitLeftWidth(300f);

            Assert.AreEqual(MenuRedirectSettingsProvider.MinLeftPaneWidth, maxLeft, Tolerance);
            Assert.AreEqual(
                MenuRedirectSettingsProvider.MinLeftPaneWidth,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(500f, 300f, 220f, maxLeft),
                Tolerance);
            Assert.AreEqual(
                MenuRedirectSettingsProvider.MinLeftPaneWidth,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(500f, 0f, 220f,
                    MenuRedirectSettingsProvider.GetMaxSplitLeftWidth(0f)),
                Tolerance);
        }

        [Test]
        public void ClampLeftWidth_HandlesMaximumBelowMinimum()
        {
            Assert.AreEqual(
                100f,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(500f, 400f, 220f, 100f),
                Tolerance);
        }

        [Test]
        public void DefaultLeftWidth_FitsWithinClampedRange()
        {
            const float availableWidth = 900f;
            float defaultLeft =
                MenuRedirectSettingsProvider.GetDefaultSplitLeftWidth(availableWidth);
            float maxLeft =
                MenuRedirectSettingsProvider.GetMaxSplitLeftWidth(availableWidth);

            Assert.AreEqual(
                defaultLeft,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(
                    defaultLeft,
                    availableWidth,
                    MenuRedirectSettingsProvider.MinLeftPaneWidth,
                    maxLeft),
                Tolerance);
            Assert.LessOrEqual(defaultLeft, maxLeft);
        }

        [Test]
        public void AvailableWidth_IgnoresTransientLayoutMeasurement()
        {
            Assert.AreEqual(
                1000f,
                MenuRedirectSettingsProvider.ResolveAvailableSplitWidth(1f, 1000f),
                Tolerance);
            Assert.AreEqual(
                800f,
                MenuRedirectSettingsProvider.ResolveAvailableSplitWidth(800f, 1000f),
                Tolerance);
            Assert.AreEqual(
                0f,
                MenuRedirectSettingsProvider.ResolveAvailableSplitWidth(1f, 0f),
                Tolerance);
        }

        [Test]
        public void AvailableWidth_InvalidMeasurementDoesNotLockDragAtMinimum()
        {
            const float viewWidth = 1000f;
            float availableWidth =
                MenuRedirectSettingsProvider.ResolveAvailableSplitWidth(1f, viewWidth);
            float maxLeft = MenuRedirectSettingsProvider.GetMaxSplitLeftWidth(availableWidth);

            Assert.AreEqual(
                320f,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(
                    320f,
                    availableWidth,
                    MenuRedirectSettingsProvider.MinLeftPaneWidth,
                    maxLeft),
                Tolerance);
            Assert.AreEqual(
                500f,
                MenuRedirectSettingsProvider.ClampSplitLeftWidth(
                    500f,
                    availableWidth,
                    MenuRedirectSettingsProvider.MinLeftPaneWidth,
                    maxLeft),
                Tolerance);
        }
    }
}
