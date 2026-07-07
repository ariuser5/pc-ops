# Backup to Google Drive (encrypted)

## Script structure
- `scripts/core/Common.ps1`: logging and shared command/path helpers
- `scripts/core/Bitwarden-Session.ps1`: acquire password from Bitwarden and produce cleanup state for logout/lock/session restore
- `scripts/core/Archive-Local.ps1`: create encrypted local archive (no upload), receives password as a parameter
- `scripts/core/Upload-GDrive.ps1`: upload archive and delete local file unless `-KeepLocal`
- `scripts/Backup-SecuritySdCard.ps1`: predefined workflow orchestrator

## Prerequisites
- Bitwarden CLI (`bw`) in `PATH`
- 7-Zip (`7z.exe`) installed (in `PATH` or default install folder)
- `rclone` in `PATH` and already configured with a Google Drive remote
- Bitwarden item selector configured via env var:
	- `BACKUP_BW_ITEM_ID` (preferred), or
	- `BACKUP_BW_ITEM_NAME`

## What the script does
1. Validates tools and source path
2. Gets the password from Bitwarden (`bw get password ...`)
3. Builds an encrypted `.7z` archive with:
	 - format: `7z`
	 - encryption: `AES-256` (7z format with `-p`)
	 - header encryption: on (`-mhe=on`)
4. Uploads archive with `rclone copyto`
5. Deletes local archive unless `-KeepLocal` is set

## Default output location
By default archives are created under:

`%LOCALAPPDATA%\PCOps\Backups\<source-folder-name>`

The folder name is sanitized to be filesystem-safe. If the source is an edge case (for example a drive root), the script uses a safe fallback name.

## Core step scripts
- `Bitwarden-Session.ps1 -Action Acquire`: gets password from Bitwarden and returns cleanup state
- `Archive-Local.ps1`: creates encrypted archive from source using direct password parameter
- `Upload-GDrive.ps1`: uploads archive to Google Drive and optionally removes local archive
- `Bitwarden-Session.ps1 -Action Cleanup`: restores Bitwarden auth/session state

## Bitwarden login/logout behavior
- If the script had to log in to Bitwarden, cleanup logs out at the end.
- If Bitwarden was already logged in and usable, cleanup does not log out.
- If Bitwarden was logged in but locked, the script unlocks it, then cleanup re-locks.
- Password is passed directly in-memory to the archive step.

## Wrapper script (predefined destination)

`scripts/Backup-SecuritySdCard.ps1` is the predefined wrapper.

The predefined wrapper orchestrates the core scripts directly with:
- predefined destination: `gdrive:Backups/Security-SD-Card`
- Bitwarden item is selected from environment variables (`BACKUP_BW_ITEM_ID` or `BACKUP_BW_ITEM_NAME`)
- automatic exclude pattern for literal `[no-sync]` prefix: `[[]no-sync[]]*`
- when backing up a drive root (for example `S:\`), also excludes: `System Volume Information*`, `$RECYCLE.BIN*`
- archive filename prefix: `backup-<source-folder-name>`
- supports `-KeepLocal` to keep the local archive after upload
- supports optional `-ArchivePassword` (`SecureString`) to bypass Bitwarden completely

### Configure Bitwarden item selector

Set one of these in your shell before running the wrapper:

```powershell
$env:BACKUP_BW_ITEM_ID = "<your-item-id>"
# or
$env:BACKUP_BW_ITEM_NAME = "<your-item-name>"
```

## Verbose mode (redaction-safe)

Use `-Verbose` on the wrapper to print step-level diagnostics without logging passwords, session tokens, or secret values.

```powershell
pwsh -File .\automations\backup-to-gdrive\scripts\Backup-SecuritySdCard.ps1 \
	-SourcePath "C:\Users\you\Documents" \
	-Verbose
```

Replace the destination inside `scripts/Backup-SecuritySdCard.ps1`, then run:

```powershell
pwsh -File .\automations\backup-to-gdrive\scripts\Backup-SecuritySdCard.ps1 \
	-SourcePath "C:\Users\you\Documents"
```

You can also pass the source path positionally (first argument):

```powershell
pwsh -File .\automations\backup-to-gdrive\scripts\Backup-SecuritySdCard.ps1 "C:\Users\you\Documents"
```

Keep local archive after upload:

```powershell
pwsh -File .\automations\backup-to-gdrive\scripts\Backup-SecuritySdCard.ps1 \
	-SourcePath "C:\Users\you\Documents" \
	-KeepLocal
```

Use a manually provided password and skip Bitwarden:

```powershell
$pw = Read-Host "Archive password" -AsSecureString
pwsh -File .\automations\backup-to-gdrive\scripts\Backup-SecuritySdCard.ps1 \
	-SourcePath "C:\Users\you\Documents" \
	-ArchivePassword $pw
```

If you omit `-SourcePath`, the wrapper prompts for it interactively.

```powershell
pwsh -File .\automations\backup-to-gdrive\scripts\Backup-SecuritySdCard.ps1
```