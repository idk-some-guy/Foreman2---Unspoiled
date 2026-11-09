# PresetRig

Hand-run tool that runs Factorio headless to capture what the game reports. No test suite runs it. It never writes into a Factorio install or the real mods folder: each run gets its own folder under the system temp path (`rig-<guid>/{mods,write,config.ini}`) and deletes it afterwards unless `--keep` is given.

Sources:
- `docker` runs the `factoriotools/factorio:2.1.24` arm64 image (OrbStack; `/opt/homebrew/bin/docker`). The binary is `/opt/factorio/bin/arm64/factorio` inside the image.
- `steam` runs the local Steam install's own binary. Use it only when a Docker run is not possible.

Commands, from the repository root:

```
dotnet run --project tools/PresetRig -- probe --source docker [--binary <path>] --out tools/PresetRig/evidence/2.1.24.json [--log <file>] [--keep]
dotnet run --project tools/PresetRig -- export --source docker [--binary <path>] --set vanilla|space-age --mod <mod folder> [--out <file>] [--evidence <file>]
dotnet run --project tools/PresetRig -- pngs [--install <Factorio install>]
```

`probe` creates a Space Age save, runs `probe/foremanprobe_2.1.0` with `--instrument-mod` for 3100 ticks and writes the JSON it prints, plus the exit codes of both Factorio runs. `export` runs any exporter mod the same way for one tick, writes the raw output, and with `--evidence` merges what happened when the game loaded the mod into that file as `legacy_mod_load`. `export` exits with the game's exit code, or 1 when the export end marker is missing; with `--evidence` it exits 0 so a refused mod can be recorded. `--binary` overrides the Factorio binary path inside the image or install. The probe's last tick is set from the 3100-tick run length when the mod is copied into the run folder. `pngs` counts the PNG files under the install's data folders (read only).

Host paths in everything the rig writes are scrubbed: the home folder becomes `~` and the run folder `<run>`.
