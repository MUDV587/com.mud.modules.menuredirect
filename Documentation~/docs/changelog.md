# Changelog

The canonical changelog is [CHANGELOG.md](https://github.com/MUDV587/com.mud.modules.menuredirect/blob/main/CHANGELOG.md).
GitHub Releases contain the same release notes with downloadable tags.

## Unreleased

- Settings page: the two columns now share a single 1px black divider line inside one
  container; the empty gap between the rules list and rule details is gone. Splitter
  dragging keeps the black line and the horizontal-resize cursor as the only feedback
  (the blue hover/drag highlight was removed), and the right column's content sits
  closer to the divider line.

## 0.2.1 — 2026-11-06

- Changed the Package Manager display name to `MUD.Modules.MenuRedirect`.

## 0.2.0 — 2026-11-06

- Renamed the package to `com.mud.modules.menuredirect`, the editor assembly to
  `MUD.Modules.MenuRedirect.Editor`, and the menu entry to **Tools > MUD > Menu Redirect**.
- Added rule search/filter, unsaved-draft recovery, rule import/export, and the parent-path picker.
- Added shared validation, transactional generated-code updates, and domain-reload-safe hiding.
- Improved settings-page performance and splitter dragging; validation and draft writes are deferred
  while dragging.
- Added 84 EditMode regression tests.

See [GitHub Releases](https://github.com/MUDV587/com.mud.modules.menuredirect/releases) for all versions.
