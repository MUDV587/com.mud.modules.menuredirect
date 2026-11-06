# 导入与导出

规则可以导出为 JSON 文件，再导入到另一个项目。文件只包含规则，不包含生成目录、语言偏好等本机设置。

## 导出

点击工具栏 **导出…** 并选择文件。工具把当前内存中的规则写入文件。
导出不会保存配置，也不会生成代码。

## 导入

点击 **导入…** 并选择 JSON 文件。工具会先完整验证文件，再修改内存。

验证通过后会询问是否整体替换当前规则。确认后替换内存列表；取消则保持原样。

导入**不会**保存配置或生成代码；之后仍需点击 **应用**。

## 文件格式

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

### 字段

| 字段 | 必填 | 含义 |
|---|---|---|
| `format` | 是 | 必须是 `MUD.MenuRedirect.Rules`。 |
| `version` | 是 | 当前格式必须为 `1`。 |
| `count` | 是 | 必须与 `rules` 条数一致。 |
| `rules` | 是 | 规则数组。 |
| `fromPath` | 是 | 原菜单路径。 |
| `toPath` | 是 | 新路径；留空字符串表示仅隐藏。 |
| `priority` | 否 | `MenuItem` 优先级，默认 `0`。 |
| `enabled` | 否 | 默认 `true`。 |

## 校验规则

导入是全有或全无。出现以下情况会拒绝整份文件：

- JSON 为空或格式错误；
- `format` 或 `version` 不支持；
- `count` 与规则条数不一致；
- 规则路径非法；
- 原路径重复；
- 规则未通过常规验证。

导入失败不会修改当前规则。成功导入的列表仍需 **应用** 才会保存或生成代码。

## 可移植性

文件可以在项目间复用，但路径必须在目标项目中存在。
目标项目未安装相关包时，菜单不存在，验证会失败。

文件使用 UTF-8 JSON，不包含绝对路径。
