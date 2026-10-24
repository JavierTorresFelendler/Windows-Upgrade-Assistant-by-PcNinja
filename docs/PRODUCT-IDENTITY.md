# Product identity and portable download contract

- Display/GUI title: Windows Upgrade Assistant By PcNinja, without a release suffix.
- Product version, not filename, drives update comparison. Full version/source
  commit remain in properties, startup logs and CLI version/status.
- Internal build output: Windows-Upgrade-Assistant-By-PcNinja.exe.
- New publication policy, owner approved for the Beta updater trial: versioned
  assets such as Windows-Upgrade-Assistant-By-PcNinja-V1.1-Beta1.exe,
  V1.1-Beta2.exe, V1.1.exe (product 1.1.0), or V1.1.1.exe.
- Previously published RC8/older asset names and bytes remain unchanged. Do not
  rename or overwrite historical GitHub assets.
- Each release pins its own asset/size/SHA-256. Do not use a mutable latest EXE URL;
  resolve the verified manifest's exact versioned asset instead.
- New versions stay beside the running EXE. No Program Files relocation, original
  replacement/rename, or automatic numbered collision copies.
- Stable/RC builds discover stable only; Beta builds discover numbered Beta only.
  Beta publication never changes latest stable.

See [application updates](APPLICATION-UPDATES.md) for consent, notices, trust,
CLI and live acceptance. GitHub source ZIP/tar files are not executable assets.
