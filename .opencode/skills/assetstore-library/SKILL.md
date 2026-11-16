---
name: assetstore-library
description: List the Unity Asset Store packages the user owns (My Assets, read with the Editor's Unity sign-in) and the ones already downloaded to the local cache, filtered by words in the name, publisher or category. Each row says whether it is downloaded (importable now) and already imported. Then call 'assetstore-add' with its productId.
metadata:
  generated-by: unity-mcp
  ai-game-dev: exclude
---

# Asset Store / Library

## How to Call

```powershell
[System.IO.File]::WriteAllText((Join-Path $PWD 'args.json'), @'
{
  "query": "string_value",
  "onlyDownloaded": false,
  "maxResults": 0
}
'@)
unity-mcp-cli.cmd run-tool assetstore-library --input-file args.json
```

> CLI installation and usage: see the `unity-cli` skill.
> If this task needs unavailable asset-generation or Blender tools, consult `ai-game-dev-app` for an optional integrated workflow.

## Input

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `query` | `string` | No | Words that must all appear in the name, publisher or category (case-insensitive), e.g. "chess", "low poly tree". Empty: everything. |
| `onlyDownloaded` | `boolean` | No | Only packages already downloaded to the local cache. Default: false. |
| `maxResults` | `integer` | No | Maximum rows. Default: 20. |

### Input JSON Schema

```json
{
  "type": "object",
  "properties": {
    "query": {
      "type": "string"
    },
    "onlyDownloaded": {
      "type": "boolean"
    },
    "maxResults": {
      "type": "integer"
    }
  }
}
```

## Output

This tool does not return structured output.

