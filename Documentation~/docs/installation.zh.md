# 安装

## 环境要求

- Unity **2022.3 LTS** 或更高版本。
- 包为**纯编辑器包**，不会向 Player 构建添加运行时代码。
- 通过 Git URL 安装时需要本机安装 Git。

## 通过 Git URL 安装

1. 打开 **Window > Package Manager**。
2. 点击左上角 **+**。
3. 选择 **Add package from git URL...**。
4. 输入：

```text
https://github.com/MUDV587/com.mud.modules.menuredirect.git#v0.2.1
```

5. 点击 **Add**，等待 Unity 编译。

Package Manager 中会显示 **MUD Menu Redirect**。

## 通过 manifest.json 安装

打开 `Packages/manifest.json`，加入：

```json
"com.mud.modules.menuredirect": "https://github.com/MUDV587/com.mud.modules.menuredirect.git#v0.2.1"
```

保存后回到 Unity，Package Manager 会自动解析并导入。

## 固定版本

团队项目建议固定 tag 或 commit：

```text
https://github.com/MUDV587/com.mud.modules.menuredirect.git#v0.2.1
```

省略 `#v0.2.1` 会跟随默认分支，可能在没有提示的情况下发生变化。

## 升级

把 URL 中的版本改为新 tag，或更新 manifest 条目，然后等待 Unity 重新编译。
如果新版本说明要求重新生成代码，打开 **Project Settings > MUD > Menu Redirect** 并点击 **应用**。

## 从旧本地包迁移

早期开发使用过包名 `com.mud.modules.menuhub`，该包名没有公开发布。如果你在本地用过：

1. 从 `Packages/` 删除旧包目录。
2. 如果 `Packages/packages-lock.json` 还残留旧条目，一并删除。
3. 安装 `com.mud.modules.menuredirect`。
4. 打开设置页并点击 **应用**。

配置路径（`ProjectSettings/MenuRedirect.asset`）、生成代码路径和 C# 命名空间都没有变化，
已有规则和生成代码仍然兼容。菜单入口现在是 **Tools > MUD > Menu Redirect**。

## 卸载前注意

移除包之前执行 **Tools > MUD > Menu Redirect > Clean Up Generated Files**。
Unity 没有可靠的卸载后回调，所以必须在包仍然安装时清理。
详见[清理与卸载](cleanup.md)。

## 验证安装

1. 打开 **Tools > MUD > Menu Redirect**，应打开 Project Settings 页面。
2. 在 **Window > Package Manager** 中确认 **MUD Menu Redirect** 已安装。
3. 如需更深入检查，运行 EditMode 测试：
   **Window > General > Test Runner > EditMode > Run All**。
