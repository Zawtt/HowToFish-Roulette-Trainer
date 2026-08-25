# Validation

The roulette runtime in this repository is the unchanged known-good v1 source.
Its preserved JSONL results are available under `validation/v1.0/`.

| Test | Spins | Successful |
|---|---:|---:|
| Black matrix | 50 | 50 |
| Red matrix | 50 | 50 |
| Green matrix | 30 | 30 |
| Free/original mode | 30 | 30 |
| Mode switching | 5 | 5 |
| Scene reload | 2 | 2 |
| Installed smoke test | 1 | 1 |
| Green visual sequence | 10 | 10 |

Universal installer validation was performed against:

- original build SHA-256 `871F76587F0A61338C2F3F8E68D3AA1E2EDC01AB6F907399EF2F01E8CD352BCA`;
- current Steam build SHA-256 `FA8C6F47874E69FE07B9C978F35CC05372DF2BDD3535DE5F5FAC355F999A5762`;
- a renamed executable and renamed Unity data directory;
- a deliberately incompatible assembly, which was rejected without modification or backup creation.

Both compatible builds passed install, mode change, restore-to-identical-hash, and reinstall.
