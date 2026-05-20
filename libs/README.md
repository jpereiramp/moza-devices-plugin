# Local dependency cache

This directory is intentionally kept almost empty in git.

The plugin builds against assemblies that must be supplied from local installs:

- `libs/SimHub/SimHub.Plugins.dll`
- `libs/SimHub/GameReaderCommon.dll`
- `libs/SimHub/SimHub.Logging.dll`
- `libs/SimHub/Newtonsoft.Json.dll`
- `libs/MOZA_SDK/MOZA_API_CSharp.dll`
- `libs/MOZA_SDK/MOZA_API_C.dll`
- `libs/MOZA_SDK/MOZA_SDK.dll`

Use `scripts/Prepare-Dependencies.ps1` or `make deps` to copy these files into
place. The actual DLLs are ignored by git because they are third-party binaries.
