# Apply, Generated Code & Domain Reload

Menu Redirect uses a two-part mechanism: generated wrapper code and hidden original menus. The order
matters.

## Apply sequence

When you click **Apply**, the tool:

1. validates the rules;
2. writes the new generated wrapper file;
3. saves `ProjectSettings/MenuRedirect.asset`;
4. deletes the old generated file if the folder changed;
5. either hides the original menus immediately (if the wrapper code did not change) or defers hiding
   until the compile and domain reload finish.

If saving the configuration fails, the newly written wrapper file is rolled back. If deleting the old
file fails, the new file is removed and the folder path is pointed back to the old file.

## Generated code

Default location:

```text
Assets/MenuRedirects/Editor/MenuRedirects.g.cs
```

The folder can be changed in the settings page, but:

- the file name is always `MenuRedirects.g.cs`;
- the folder must be under `Assets`;
- the folder path must contain a directory named `Editor`.

The first line contains this tool's generated header. Only files with that header are overwritten,
migrated, or deleted.

Each enabled rule produces a `[MenuItem(targetPath)]` wrapper. The wrapper calls
`MenuRedirectInvoker.Execute(sourcePath)`, which reflects the original `[MenuItem]` method. The tool
also generates validate wrappers when the original method has a validate function.

`EditorApplication.ExecuteMenuItem` is intentionally not used: after the original menu is removed, it
returns `false`.

## Why hiding waits for a domain reload

If the wrapper code changed, the new menu entries do not exist until Unity compiles the file and
reloads the domain. Hiding the originals before that would leave neither the old menu nor the new one.

The bootstrap waits for the reload, checks that the generated file exists and matches the current
rules, then replays hiding. If the compile failed or the file is missing, hiding is deferred.

## Domain reload replay

After every domain reload, `MenuRedirectBootstrap` checks the configuration and generated code. If
the generated wrapper is ready, it removes the original menus again.

This is necessary because Unity rebuilds its menu registry on every reload. The removal is not
persistent by itself.

## Stale or missing generated code

Hiding is skipped when:

- the generated file is missing;
- the generated header is missing;
- the generated content does not match the current rules;
- the project has a compile error that prevents the wrapper from loading.

Fix the compile error and click **Apply** again. The feedback area explains what is missing.

## Multiple generated files

If the project contains more than one generated `MenuRedirects.g.cs` with this tool's header, Apply
aborts. Multiple files would produce duplicate menu classes. Remove the extra file, then apply again.

## Manual edits

Do not edit `MenuRedirects.g.cs` manually. The next Apply overwrites it. Change the rules in the
settings page instead.

## Shortcut comments

When a wrapper replaces a menu with a shortcut, the generated file adds a comment before the affected
method. The Apply feedback also lists the shortcut count and keys. Shortcuts must be added manually
to the new menu.

## Reverting

**Revert** discards unsaved in-memory edits. It does not change the saved configuration, generate code,
or restore menus that were already hidden in the current session. A domain reload is needed to
restore menus removed during the session.
