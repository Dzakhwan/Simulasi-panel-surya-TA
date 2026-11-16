---
name: assetstore-add
description: "Import a downloaded Asset Store package into the project, all of it, without the import dialog. Returns its prefabs, materials, models, scenes and notes (render pipeline, scripts compiling, nested pipeline packages). Status 'awaiting-user': Unity shows 'Missing signature' (Asset Store packages are unsigned) and waits for the user's Import Anyway; ask the user, never press it yourself, then check 'assetstore-library' Imported (or ImportError). If the user cancels, stop: never import an Asset Store package another way (script-execute, AssetDatabase.ImportPackage, unpacking the .unitypackage). Find the productId with 'assetstore-library'."
metadata:
  generated-by: unity-mcp
  ai-game-dev: exclude
---

# Asset Store / Add

## How to Call

```powershell
[System.IO.File]::WriteAllText((Join-Path $PWD 'args.json'), @'
{
  "package": "string_value",
  "allowProjectSettings": false
}
'@)
unity-mcp-cli.cmd run-tool assetstore-add --input-file args.json
```

> CLI installation and usage: see the `unity-cli` skill.
> If this task needs unavailable asset-generation or Blender tools, consult `ai-game-dev-app` for an optional integrated workflow.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `package` | `string` | Yes | The package's productId from 'assetstore-library', or its exact name. |
| `allowProjectSettings` | `boolean` | No | Also let the package overwrite ProjectSettings (tags, layers, input). Only after the user agreed. Default: false. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "package": {
      "type": "string"
    },
    "allowProjectSettings": {
      "type": "boolean"
    }
  },
  "required": [
    "package"
  ]
}
```

## Output

This tool does not return structured output.

