# Draft Recovery

Menu Redirect protects unsaved edits from crashes and domain reloads.

## Where the draft lives

```text
Library/MenuRedirectDraft.json
```

`Library/` is local to the machine and is not committed to version control.

## When it is written

After a rule or generated-code folder changes, the page marks itself dirty and schedules a save.
The save is debounced by **0.75 seconds**. If you leave the page, reload the domain, or quit the
editor, the draft is flushed synchronously.

While the splitter is being dragged, validation and draft writes are deferred until the mouse is
released. This avoids blocking the drag with validation work or synchronous file I/O.

## What it contains

The draft stores two snapshots:

- the **saved baseline**: the rule JSON and generated-code folder that were last applied;
- the **draft**: the current in-memory rule JSON and generated-code folder.

The baseline is used to decide whether the draft is still relevant.

## Restore behavior

When the page opens, the tool compares the draft baseline with the current configuration. If they
match, the draft is restored and the page shows a message that unsaved changes were recovered.

If the baseline does not match, the configuration was changed in another session (or applied/reverted
elsewhere). The old draft is considered stale and is not applied.

## Draft validation

Drafts are restored even if some paths are temporarily invalid. This is intentional: a crash should
not lose a half-typed path. Validation still runs after restore, so Apply remains disabled until the
errors are fixed.

## Clearing the draft

The draft is deleted after a successful **Apply** or **Revert**. It is not part of the package and
should not be committed.

## Troubleshooting

If an old draft keeps appearing:

1. Finish or revert the current edits.
2. Click **Apply** or **Revert** to clear the draft.
3. If the file is corrupt, close Unity and delete `Library/MenuRedirectDraft.json`.

Deleting the draft only loses unsaved edits; it does not affect `ProjectSettings/MenuRedirect.asset`
or generated code.
