# Troubleshooting & FAQ

## The new menu does not appear

1. Check the Console for compile errors.
2. Wait for the compile and domain reload to finish.
3. Confirm the generated file exists at the configured path.
4. Open the settings page and click **Apply** again.
5. Confirm the rule is enabled and passes validation.

If the wrapper code changed, the original menu is hidden only after the reload. During the compile,
the original may still be visible.

## The original menu is still visible

- The generated code may be stale or missing. Apply again after the compile succeeds.
- The rule may be disabled or invalid.
- Another package may have re-registered the menu after the tool hid it.
- A domain reload may have occurred while the wrapper was unavailable; the bootstrap intentionally
  defers hiding in that case.

Check the feedback area for the generated-code status.

## Both the original and the new menu are missing

This should not happen during normal apply because hiding waits for the wrapper. If it does:

1. Check the Console for compile errors.
2. Confirm `MenuRedirects.g.cs` exists and has the generated header.
3. Fix the compile error.
4. Click **Apply** again.
5. If necessary, delete the generated file and let the next domain reload restore the original menus.

## Apply is disabled

The rule has a validation error. Open the detail panel and read the Validation section. Common causes
are an existing target path, an invalid source, overlapping rules, or a protected menu.

Warnings do not disable Apply.

## The menu picker is empty or missing an entry

- Dynamic menus are not in the registry. Type the path manually.
- Click the refresh button in the picker.
- The menu may be declared by a package that is not loaded yet; wait for compilation.
- Protected entries appear but cannot be selected.

## The shortcut is missing

Shortcuts are compile-time `[MenuItem]` attributes and are not copied to generated wrappers. Add the
shortcut manually to the new menu. The Apply feedback lists affected shortcuts.

## The target path already exists

Choose a different target, hide the existing target first, or remove the conflicting rule. The tool
does not overwrite existing menus.

## I changed the language but nothing else changed

That is expected. Language is stored in `EditorPrefs` per project. It does not mark the page dirty,
save the configuration, or recompile generated code.

## Can I redirect a dynamically registered menu?

No. Dynamic menus can be hidden, but a redirect needs a static `[MenuItem]` method so a wrapper can
be generated. Convert the source to a static attribute, or use hide-only.

## Can I commit the configuration?

Yes. Commit:

```text
ProjectSettings/MenuRedirect.asset
Assets/MenuRedirects/Editor/MenuRedirects.g.cs
```

Do not commit `Library/MenuRedirectDraft.json`.

## Can several team members use different menu layouts?

The configuration is project-wide. Different layouts require separate branches or projects. The
language preference is local per user.

## Does it work in builds?

No. It is an Editor-only package and only changes the Editor menu.

## Does it modify third-party packages?

No. It reads the menu registry and generates a separate file under `Assets`. It does not edit
third-party source, assets, asmdefs, or package manifests.

## How do I remove everything?

Run the cleanup command before uninstalling, then remove the package. See
[Cleanup & Uninstall](cleanup.md).

## Still stuck?

Open an issue with:

- Unity version;
- package version;
- a screenshot of the rule and validation panel;
- relevant Console errors;
- whether the generated file exists and matches the configured folder.
