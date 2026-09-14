# ServerSync build provenance

`ServerSync.dll` is the Valheim 1.0.7 source build `valheim-1.0.7-r1`, based on https://github.com/blaxxun-boop/ServerSync commit `c57c2aa54e07cdcc7630d6068699ea781622323e`. Its license is retained in `ServerSync.LICENSE.txt`.

- Previous DLL SHA-256: `166956302a294e224474b26f4c7d58409084ad3f48bd0af1feb7551f229c8f60`.
- Current DLL SHA-256: `adb7b367c2711b72435e8219a89358926c2ea81e3b7cc817b2c12dd4768aee74`.
- Assembly identity remains `ServerSync, Version=1.0.0.0`; file version is `1.0.0.1`.
- Built from source using .NET SDK 10.0.200, Debug/net48, original Valheim 1.0.7 client DLLs (Steam build 25185596), and installed BepInEx/Harmony references.
- Recompilation replaces three obsolete `ldsfld ZRoutedRpc.Everybody` instructions with the new constant value. Three admin checks use public `ZNet.IsAdmin(hostName)` instead of the old `ListContainsId` reflection fallback. Its platform-ID matching delegates to the same game policy. Configuration protocol, keys, version checks, lock enforcement and socket buffering remain intact.

The preserved source, upstream files, input hashes and reproducible build script are at `C:\Users\blizz\.codex\references\valheim\integrations\serversync\versions\valheim-1.0.7-r1`. The `baseline` subdirectory there is the **old input DLL**, not the new build.

Reproduce on this workspace:

```powershell
& 'C:\Users\blizz\.codex\references\valheim\integrations\serversync\versions\valheim-1.0.7-r1\Build.ps1' -Target client -OutputDirectory "$PWD\obj\ServerSync107"
```

The mod merges this library into its final DLL. Do not install it separately. Final merged game references were checked against both original client/server DLL sets; native networking, connection handshakes and admin configuration synchronization still require in-game verification.
