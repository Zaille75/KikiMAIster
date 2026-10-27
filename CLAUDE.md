# KikiMAIster

All-in-one co-op stats mod for Slay the Spire 2. Spec-driven: `docs/specs/` is the source of truth;
update the spec before changing behavior. Plan: `docs/ROADMAP.md`.
**Start here in a new conversation: `docs/HANDOFF.md`** (current state, workflow, install command, open topics).

- Zaille75 does not code: Claude writes all code. Zaille75 validates specs and tests in-game on his Windows PC
  (he connects to this Linux box over SSH). Game files are never on this machine except the DLLs in `lib/`.
- Target the **beta** branch first (`lib/beta/`), default branch second (`lib/default/`).
- C# net9.0 + Harmony only, no BaseLib. Only `Patches/` may touch game internals; `Core/` stays pure C# and unit-tested.
- The mod must never crash the game or change gameplay (`affects_gameplay: false`).
- `lib/` (game DLLs, decompiled code) is copyrighted: keep it out of git.

## Toolchain (Linux box)
.NET 9 SDK + .NET 8 runtime installed in `~/.dotnet` (no system install, libicu missing). Prefix commands with:
`export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_ROOT=~/.dotnet PATH=$PATH:~/.dotnet:~/.dotnet/tools`
Decompiler: `ilspycmd` (global tool).
