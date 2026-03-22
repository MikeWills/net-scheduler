# Upgrading an Existing Deployment

This covers updating a running production server to a new version of NCS Scheduler.

## Before You Start

- Check the git log / release notes for any **breaking changes or manual steps** called out for the specific version you're deploying.
- EF Core migrations run automatically on startup — you do not need to run `dotnet ef database update` manually unless troubleshooting.
- The SQLite database file is **not** replaced during an upgrade; only the application binaries change.

---

## Standard Upgrade Steps

### 1. Build the new release locally

```bash
dotnet publish NcsScheduler/NcsScheduler.csproj -c Release -o publish
```

`appsettings.json` and `appsettings.Development.json` are excluded from publish output and will **not** overwrite the server's config files.

### 2. Copy files to the server

Use WinSCP, `scp`, or `rsync` to copy the contents of the `publish/` folder to `/opt/ncsscheduler/` on the server:

```bash
rsync -av --delete publish/ user@yourserver:/opt/ncsscheduler/
```

> `--delete` removes old files no longer in the publish output. Do **not** delete the database file — it lives outside the publish folder if `ConnectionStrings__DefaultConnection` points to an absolute path (e.g. `/opt/ncsscheduler/ncsscheduler.db`). Confirm the DB path in your systemd unit before using `--delete`.

### 3. Restart the service

```bash
sudo systemctl restart ncsscheduler
sudo systemctl status ncsscheduler
```

On startup the app will:
1. Apply any pending EF migrations automatically
2. Re-seed roles and the admin user (safe to run repeatedly — skips existing records)
3. Start the session generator background service

### 4. Verify

- Open the site and confirm the public schedule loads
- Check the logs if the service doesn't come up cleanly:

```bash
sudo journalctl -u ncsscheduler -n 50 --no-pager
```

---

## Rollback

If the new version doesn't start or has a critical bug:

1. Copy the previous publish output back to `/opt/ncsscheduler/`
2. `sudo systemctl restart ncsscheduler`

> If the new version ran migrations before you rolled back, the database schema may be ahead of the old binary. In practice this only matters if the migration dropped or renamed columns. Additive migrations (new columns with defaults) are backwards-compatible.

---

## Config File Changes

If a new version adds new `appsettings.json` keys, you'll need to add them to the server's config or systemd environment manually. The commit message or release notes will call this out.

To check what's changed in `appsettings.json` between versions:

```bash
git diff v1.0..v1.1 -- NcsScheduler/appsettings.json
```
