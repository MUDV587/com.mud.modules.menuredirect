# Design & Internals

This page summarizes the architecture for contributors. The detailed internal design notes are in
[Documentation~/Design.md](https://github.com/MUDV587/com.mud.modules.menuredirect/blob/main/Documentation~/Design.md).

## Components

| Component | Responsibility |
|---|---|
| `MenuRedirectRule` | Serializable rule data: source, target, priority, enabled. |
| `MenuRedirectSettings` | Project-wide `ScriptableSingleton` stored at `ProjectSettings/MenuRedirect.asset`. |
| `MenuRedirectSettingsProvider` | Project Settings page, rule list, detail panel, import/export, apply/revert. |
| `MenuRedirectPickerWindow` | Searchable source and parent path picker. |
| `MenuRedirectValidator` | Shared validation used by the list, detail panel, and Apply. |
| `MenuRedirectEngine` | Generates wrappers, migrates files, hides menus, replays after reload. |
| `MenuRegistry` | Scans `[MenuItem]` declarations, records assemblies, methods, validate functions, priorities. |
| `MenuRedirectBridge` | Reflection bridge to Unity's internal menu API. |
| `MenuRedirectGuard` | Protected-menu and Unity ownership checks. |
| `MenuRedirectInvoker` | Reflection execution of the original `[MenuItem]` method. |
| `MenuRedirectCleanup` | Deletes managed generated files before uninstall. |
| `MenuRedirectLocalization` | Chinese/English UI strings and per-project language preference. |
| `MenuRedirectBootstrap` | Replays hiding after domain reloads. |

## Data flow

```text
Settings page
  → MenuRedirectValidator
  → MenuRedirectEngine.PrepareApply
      → generated MenuRedirects.g.cs
      → save ProjectSettings/MenuRedirect.asset
      → hide originals (or defer until reload)
  → MenuRedirectBootstrap after reload
      → verify generated code matches rules
      → replay hiding
```

## Why generated wrappers

Unity menu entries are registered at compile time. A runtime-only redirect cannot create a new
`[MenuItem]` path. The tool therefore generates a C# file with `[MenuItem(target)]` wrappers and
delegates execution to the original method through reflection.

The original menu is hidden with Unity's internal menu API. The wrapper is necessary because
`EditorApplication.ExecuteMenuItem` returns `false` once the original entry is removed.

## Protection model

Unity built-in top-level menus and entries declared by Unity-owned assemblies are protected. Unity
ownership is resolved from:

- the assembly location;
- `PackageInfo` for package assemblies;
- asmdef/asmref paths for project assemblies.

The `Tools` root is intentionally not protected, because it is commonly used by third-party tools.

## Transactional apply

Apply writes the new generated file, saves the configuration, then deletes the old file. On failure,
it rolls back the new file and keeps the old path. It refuses to overwrite non-generated files and
aborts if multiple managed generated files remain.

## Performance

The settings page is IMGUI and can be sensitive to per-event allocations. The current implementation:

- caches rule row display content, validation messages, button widths, and colored label styles;
- caches the resolved UI language and language dropdown options;
- caches toolbar counts until validation changes;
- caches generated-code paths and checks file timestamps at most once per second;
- debounces validation by 0.15 seconds and draft saves by 0.75 seconds;
- defers validation and draft writes while the splitter is dragged;
- uses a lightweight fast path for `MouseDrag` / `MouseUp` while still re-laying out the two columns
  on the following `Layout` / `Repaint`, preserving fully real-time resizing.

The unavoidable cost is that a fully live splitter re-lays out and repaints both columns on every
visible frame. Preview lines or a reduced refresh rate would avoid that cost but are intentionally
not used.

## Tests

The EditMode suite under `Tests/Editor` covers:

- rule validation and protected-menu detection;
- branch and leaf classification;
- generated-code transactions and rollback;
- rule-file import/export validation;
- Unity ownership detection using assembly locations, package info, and asmdef/asmref paths;
- localization key coverage.

Run **Window > General > Test Runner > EditMode > Run All**.
