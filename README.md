# RO — How to Fish Roulette Operator

<p align="center">
  <img src="src/HowToFish.RouletteTrainer.App/Assets/roulette-fish-pixel.png" width="160" alt="A pixel-art fish hugging a roulette wheel">
</p>
<p align="center">
  <strong>A tiny, playful single-player roulette cheat for How to Fish.</strong><br>
  Pick black, red, or green — RO steers the local roulette ball toward your color.
</p>

## About
RO is a personal reverse-engineering project exploring Unity assembly inspection, runtime hooks, physics control, and safe patch generation. It installs a small local module into a compatible game build, letting you choose the roulette's outcome before each spin.

Built for personal, offline experimentation — not to harm the game, its developers, or other players.

## Features
- Free, Black, Red, and Green modes
- Detects compatible builds by structure, not a fixed version hash — works with renamed executables too
- Patch generated locally per build, with SHA-256 backups and one-click restore
- Refuses to patch anything it can't safely verify

## Installation
1. Download the latest ZIP from [Releases](https://github.com/Zawtt/RO-HowToFish-Roulette-Operator/releases/latest) and extract it.
2. Close *How to Fish*, then run `How to Fish - Roulette Operator.exe`.
3. Point it at the game's executable if it isn't found automatically.
4. Confirm RO detects a compatible structure, then click **INSTALL / REPAIR**.
5. Open the game, pick a color, spin.

Use **FREE** to disable assistance. Use **RESTORE ORIGINAL** (game closed) to revert to the original assembly.

## Compatibility
RO locates `*_Data/Managed/Assembly-CSharp.dll` and checks the roulette types, methods, and fields it needs. Works across known and renamed builds while that structure holds.

No tool can guarantee support for future updates — if required members change, RO refuses to patch and leaves the game untouched.

## Building from source
Requires Windows x64, .NET 8 SDK, and your own local copy of the game (assemblies aren't redistributed here):

```powershell
.\scripts\setup-references.ps1 -GameExecutable "C:\Path\To\How to Fish.exe"
dotnet build .\HowToFish.RouletteTrainer.sln -c Release
```

## Safety
RO backs up the active assembly (SHA-256) before patching, and verifies the new patch before replacing anything. Always close the game first.

v1.0 is unsigned, so Windows may show an unknown-publisher warning. Only download from this repo and verify the ZIP against `SHA256SUMS.txt`.

Unofficial, fan-made project — not affiliated with or endorsed by *How to Fish*'s creators. Use responsibly and within the game's terms.

## License
Original code under [MIT](LICENSE). Third-party components keep their own licenses.
