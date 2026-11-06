# Import & Export

Rules can be exported to a JSON file and imported into another project. The file contains rules only;
it does not contain generated-code folders, language preferences, or other local settings.

## Export

Click **Export...** in the toolbar and choose a file. The current in-memory rules are written. Export
does not save the configuration or generate code.

## Import

Click **Import...** and choose a JSON file. The tool validates the whole file before changing memory.

If validation succeeds, you are asked to confirm replacing the current rules. Confirm to replace the
list in memory; cancel to leave it unchanged.

Import does **not** save or generate code. Click **Apply** afterward.

## File format

```json
{
  "format": "MUD.MenuRedirect.Rules",
  "version": 1,
  "count": 2,
  "rules": [
    {
      "fromPath": "NuGet/Manage NuGet Packages",
      "toPath": "Tools/My Tools/Manage NuGet Packages",
      "priority": 0,
      "enabled": true
    },
    {
      "fromPath": "Window/Unity CLI Loop",
      "toPath": "",
      "priority": 0,
      "enabled": false
    }
  ]
}
```

### Fields

| Field | Required | Meaning |
|---|---|---|
| `format` | Yes | Must be `MUD.MenuRedirect.Rules`. |
| `version` | Yes | Must be `1` for the current format. |
| `count` | Yes | Must match the number of entries in `rules`. |
| `rules` | Yes | Array of rule objects. |
| `fromPath` | Yes | Source menu path. |
| `toPath` | Yes | Target path, or an empty string for hide-only. |
| `priority` | No | `MenuItem` priority; defaults to `0`. |
| `enabled` | No | Defaults to `true`. |

## Validation rules

Import is all-or-nothing. The file is rejected if:

- the JSON is empty or malformed;
- the format or version is unsupported;
- `count` does not match the number of rules;
- a rule has an invalid path;
- a source path is duplicated;
- a rule fails the normal rule validation.

A failed import leaves the current rules untouched. A successfully imported list still needs
**Apply** before it is saved or used to generate code.

## Portability

The file is intentionally portable across projects, but paths must exist in the target project.
Menus from a package that is not installed in the target project will fail validation.

The file uses UTF-8 JSON and does not include absolute paths.
