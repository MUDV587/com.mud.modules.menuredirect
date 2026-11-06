# Validation & Safety

The settings page, detail panel, and Apply button all use the same validation result. There is no
separate "Apply anyway" path.

## Error vs. warning

| Level | Effect |
|---|---|
| Error | Apply is disabled. The rule cannot be applied until the error is fixed. |
| Warning | Apply is allowed, but the rule is limited or some children are skipped. |
| Pass | The rule is valid. |

## Common errors

- **Target path already exists.** Redirecting would create a duplicate menu path.
- **Source path has no executable static `[MenuItem]`.** The source may be dynamic, missing, or only
  a container with no executable children.
- **Source and target overlap.** A rule cannot move a menu into itself or into a path removed by
  another rule.
- **Duplicate source paths.** Two rules cannot remove the same source.
- **Invalid path syntax.** Empty segments (`//`), leading/trailing separators, and similar malformed
  paths are rejected.
- **Protected source or target.** Unity built-in menus and Unity-owned entries cannot be removed or
  moved.
- **Branch has no executable children.** A branch move needs at least one executable child.

## Common warnings

- The source is a dynamically registered menu and can only be hidden, not redirected.
- A branch contains protected children; those children are skipped and remain available.
- The target parent is protected but the leaf itself can still be created.

## Protected containers

A nested container is protected when it contains a menu declared by a Unity-owned assembly. The tool
will not remove the container as a whole. A branch move skips the protected child and preserves its
contents.

## Legacy or bypassed configuration

Even if a configuration was written by an older version or edited outside the settings page, the tool
re-validates paths and targets before the domain-reload replay. It will not hide the original menu if
the replacement is not ready.

## Transactional file handling

Apply is designed to avoid partial states:

- the new wrapper is written first;
- the config is saved second;
- the old wrapper is deleted only after the save succeeds;
- a failed save rolls back the new file;
- a failed old-file delete removes the new file and keeps the old path;
- a non-generated file at the target is never overwritten;
- multiple residual generated files abort the operation.

## Generated header

Only files whose first line contains the tool's generated header are considered managed. A user file
named `MenuRedirects.g.cs` without the header is left alone.

## What is not validated

- **Shortcuts** are not copied. The tool warns and lists them, but cannot recreate Unity's
  compile-time shortcut behavior.
- **Dynamic menus** cannot be redirected. Convert them to static `[MenuItem]` methods first, or use
  hide-only.
- **Runtime behavior** is outside scope; this is an Editor-only tool.

## Recovery

If Apply fails, read the feedback area. The configuration and generated files are left in the last
consistent state. If you are unsure:

1. Open the settings page.
2. Check the generated-code status.
3. Fix compile errors in the Console.
4. Click **Apply** again.
5. If necessary, run the cleanup command and apply from scratch.
