# Rules & Paths

A rule maps a source menu path to an optional target path.

## Fields

| Field | Meaning |
|---|---|
| Enabled | Disabled rules are kept in the list but are not validated or applied. |
| Source Path | The existing menu path to hide or move. |
| Target Path | The new menu path. Leave empty to hide only. |
| Priority | The `MenuItem` priority passed to generated wrappers. Lower values appear earlier among siblings. |

Priority does not change the order of the rules in the list; that order is edited by dragging.

## Four common rule shapes

### Leaf redirect

```text
NuGet/Manage NuGet Packages  →  Tools/My Tools/Manage NuGet Packages
```

A leaf is a path that is registered as a menu item. The original is hidden and a wrapper is generated
at the target path.

### Hide a leaf

```text
NuGet/Manage NuGet Packages  →  (empty)
```

The menu item is removed. No wrapper is generated.

### Move a branch

```text
NuGet  →  Tools/My Tools
```

A branch is a top-level path without `/`, or a container that has registered children but is not
itself registered as a menu item. The tool scans all executable children and creates wrappers under
the new parent, preserving their relative structure.

Protected children are skipped and reported in validation and generated-code comments.

### Hide a branch

```text
NuGet  →  (empty)
```

The parent is removed, which removes its whole subtree in one operation. If the branch contains a
Unity built-in child, the rule is rejected as an error instead of removing built-in menus.

## Branch detection

- A path without `/` is treated as a branch container.
- A path with `/` is treated as a branch if it has registered children and is not itself registered.
- A path that is itself registered with `[MenuItem]` is treated as a leaf, even if it also has children.

This prevents a "menu item plus submenu" from being mistaken for a pure container.

## Protected menus

Unity built-in top-level menus (File, Edit, Assets, GameObject, Window, Help, and similar) and entries
declared by Unity-owned assemblies are protected. They cannot be hidden or moved.

A nested container is protected when it contains a Unity-owned child. Branch moves skip protected
children and keep their contents.

The `Tools` root is **not** protected: third-party and custom tools can be moved or hidden.

Unity ownership is determined from the assembly location, `PackageInfo`, and asmdef/asmref paths.
An assembly whose name starts with `Unity` is not automatically considered Unity-owned if it belongs
to the project or a third-party package.

## Dynamic and context menus

- Dynamically registered menus can be hidden, but not redirected. Redirecting requires a static
  `[MenuItem]` method so the tool can generate a wrapper.
- Context menus (`CONTEXT/...`) declared by Unity are protected. Third-party context menus can be
  selected and redirected; execution uses the current selection to construct `MenuCommand`.

## Shortcuts

The generated wrapper does not copy the original shortcut. Unity's `MenuItem` shortcut is a
compile-time attribute, and a wrapper cannot reproduce it automatically.

When the generated code changes, the tool writes a comment before affected wrapper methods and lists
the affected shortcuts in the Apply feedback. Add the shortcut manually to the new menu if needed.

## Validation

A rule must pass validation before Apply is enabled. Typical errors:

- the target path already exists;
- the source path has no executable static `[MenuItem]`;
- rules overlap or produce duplicate targets;
- the path format is invalid;
- the branch has no executable children;
- the source or target is protected.

Hiding a dynamically registered menu is a warning, not an error.

See [Validation & Safety](validation-safety.md) for the full behavior.
