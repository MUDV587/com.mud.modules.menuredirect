# Settings Page

Open the page with **Edit > Project Settings > MUD > Menu Redirect** or **Tools > MUD > Menu Redirect**.

## Layout

The page uses two resizable columns:

- **Left column**: rule list, search, filter, Add, Import, Export, Apply, and Revert.
- **Right column**: details for the selected rule, including paths, priority, validation, and status.
- **Bottom area**: apply feedback and generated-code status.

Drag the divider line between the columns to resize them. The two columns are separated by a
single shared 1px black line; the 6px-wide drag handle is invisible, and dragging feedback is
the line itself plus the horizontal-resize cursor (no highlight is drawn). The right column's
content sits close to the divider line. The left column has a minimum width of 220 px
and the right column a minimum width of 240 px. The splitter width is not persisted; it resets after
leaving the page or reloading the domain.

## Toolbar

The toolbar shows text counts for:

- enabled rules;
- rules that pass validation;
- rules with warnings;
- rules with errors.

The counts are cached until the validation result changes, so they do not rescan every rule on every
GUI event.

## Search and filter

The search box performs a case-insensitive substring match against source and target paths.

The **Only Issues** filter keeps rules with warnings or errors. When the filter has no matches, the
list shows an empty state with a **Clear Filter** button.

While a filter is active, drag-reordering is disabled. Adding, importing, or reverting automatically
clears the filter.

## Rule actions

- **Add**: create a new disabled rule.
- **Import...**: replace the current in-memory rules from a JSON file after confirmation.
- **Export...**: write the current in-memory rules to a JSON file.
- **Apply**: save the configuration and generate wrapper code.
- **Revert**: discard unsaved in-memory changes and restore the last applied state.

Import, export, and picker selections only change memory. They do not write the configuration or
generate code until you click **Apply**.

## Generated code folder

The **Generated Code** row controls where `MenuRedirects.g.cs` is written. The folder must be under
`Assets` and must contain a directory named `Editor`. The file name itself is fixed.

Use **Select...** to choose a folder, **Open Folder** to reveal it, or **Reset** to return to the
default `Assets/MenuRedirects/Editor`.

Changing the folder marks the page as unsaved. Apply migrates the generated file to the new location.

## Language

The language selector supports **Follow System**, **中文**, and **English**. The preference is stored
per project in `EditorPrefs`, not in `MenuRedirect.asset`. Changing the language does not mark the
page as dirty, save the configuration, or recompile generated code.

## Unsaved state

The page shows whether there are unsaved changes and whether a recompile is pending. If Unity crashes
or reloads the domain before Apply, the draft can be restored from `Library/MenuRedirectDraft.json`.
See [Draft Recovery](drafts.md).

## Feedback area

Apply results, generated-code status, and hide messages appear below the two columns in a HelpBox-style
area. The area grows and shrinks without pushing the whole page down.

If hiding is intentionally deferred because the generated code is stale or missing, the feedback area
explains why and asks you to finish the compile or apply again.
