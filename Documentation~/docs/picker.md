# Menu Picker

The picker is a searchable tree of registered menu paths. It is used for source paths and target
parent paths.

## Opening the picker

- **Source Path**: click **Select...** next to the source field.
- **Target Path parent**: click **Parent...** next to the target field.

Both modes use the same cached menu registry. The picker does not rescan every time it opens unless
you click refresh.

## Source mode

Source mode lists:

- every registered menu path;
- all intermediate containers, including containers that are not registered themselves;
- children of those containers.

Selecting a path only updates the rule in memory. Click **Apply** to save and generate code.

## Parent mode

Parent mode lists only:

- top-level paths;
- containers that have children.

For a branch rule, the selected parent becomes the new target path directly.

For a leaf rule, the tool keeps the last segment of the current target path. If the target path is
empty, it uses the last segment of the source path. This lets you move a leaf under a new parent
without retyping its name.

If the source path is empty, selecting a parent fills the target parent and asks you to complete the
new menu name.

## Status icons

| Icon | Meaning |
|---|---|
| Green check | Executable menu item. |
| Yellow `!` | Hide-only, or contains protected children. |
| Red `✕` | Unity-protected entry; cannot be selected. |
| Gray dash | Disabled or unavailable entry. |

Protected entries can still be hovered in the tree, but cannot be selected.

## Search

Search matches the full path, case-insensitively. Matching items are shown with their ancestors
expanded automatically. Clearing the search restores the normal tree state.

## Keyboard and mouse

- Click a row to expand or collapse it.
- Double-click a row to select it.
- `Up` / `Down` moves the keyboard focus.
- `Enter` selects the focused row.
- `Esc` closes the picker.
- The refresh button rebuilds the menu registry.

Clicking a row does not move the blue "current value" marker. The marker shows the path currently set
on the rule; hover is shown separately in light blue.

## Dynamic menus

Dynamically registered menus do not appear in the registry. Type their paths manually. They can be
hidden, but not redirected.
