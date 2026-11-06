# Installation

## Requirements

- Unity **2022.3 LTS** or newer.
- The package is **Editor-only**. It does not add runtime code to player builds.
- Git must be installed if you install through a Git URL.

## Install from Git URL

1. Open **Window > Package Manager**.
2. Click **+** in the top-left corner.
3. Choose **Add package from git URL...**.
4. Enter the package URL:

```text
https://github.com/MUDV587/com.mud.modules.menuredirect.git#v0.2.1
```

5. Click **Add** and wait for Unity to compile.

The package appears as **MUD Menu Redirect** in the Package Manager.

## Install through manifest.json

Open `Packages/manifest.json` and add:

```json
"com.mud.modules.menuredirect": "https://github.com/MUDV587/com.mud.modules.menuredirect.git#v0.2.1"
```

Save the file and return to Unity. Package Manager resolves and imports the package automatically.

## Version pinning

For team projects, pin a tag or commit:

```text
https://github.com/MUDV587/com.mud.modules.menuredirect.git#v0.2.1
```

Omitting the fragment follows the default branch and can change without warning.

## Updating

Change the fragment to the new tag, or update the manifest entry, then let Unity recompile.
After upgrading, open **Project Settings > MUD > Menu Redirect** and click **Apply** if the release
notes ask for a regeneration.

## Migrating from an older local package

Early development used the package name `com.mud.modules.menuhub`. That package name is not published.
If you used it locally:

1. Remove the old package folder from `Packages/`.
2. Remove its entry from `Packages/packages-lock.json` if one remains.
3. Install `com.mud.modules.menuredirect`.
4. Open the settings page and click **Apply**.

The configuration path (`ProjectSettings/MenuRedirect.asset`), generated-code path, and C# namespace
are unchanged, so existing rules and generated wrappers remain compatible. The menu entry is now
**Tools > MUD > Menu Redirect**.

## Before uninstalling

Run **Tools > MUD > Menu Redirect > Clean Up Generated Files** before removing the package.
Unity does not provide a reliable post-uninstall callback, so cleanup must happen while the package is
still installed. See [Cleanup & Uninstall](cleanup.md).

## Verify the installation

1. Open **Tools > MUD > Menu Redirect**. The Project Settings page should open.
2. Open **Window > Package Manager** and confirm **MUD Menu Redirect** is installed.
3. Run the EditMode tests if you want a deeper check:
   **Window > General > Test Runner > EditMode > Run All**.
