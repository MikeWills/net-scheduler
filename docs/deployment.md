# Deployment

## Overview

The app is deployed to Ubuntu Linux, running as a systemd service behind an Apache reverse proxy with SSL via Let's Encrypt.

| Item | Detail |
|---|---|
| Server path | `/opt/ncsscheduler/` |
| Service user | `www-data` |
| Service name | `ncsscheduler.service` |
| Reverse proxy | Apache with `ProxyPreserveHost On` |
| SSL | Let's Encrypt |

---

## Build and Publish

```bash
dotnet publish NcsScheduler/NcsScheduler.csproj -c Release -o publish
```

Then copy the contents of the `publish/` folder to the server (e.g. via WinSCP or `scp`). Alternatively, publish directly from Visual Studio using the included FolderProfile.

> `appsettings.json` and `appsettings.Development.json` are excluded from publish output (`CopyToPublishDirectory=Never`) — the server maintains its own copies of these files.

---

## Automated Deploy (GitHub Actions)

`.github/workflows/deploy.yml` deploys to the server automatically whenever a **GitHub Release is published**. The runner joins the Tailscale network (the server has no public SSH access) as an ephemeral node, then over SSH: backs up the SQLite DB (via the root-owned `/usr/local/sbin/ncsscheduler-backup-db` helper), stops the service, `rsync`s the publish output to `/opt/ncsscheduler/` **as the plain `deploy` user with no sudo** — `deploy` owns that tree, and its setgid directories hand new files the `www-data` group the service reads with — restarts the service, and polls `http://localhost:5000/` (the production port) for a response before declaring success.

The deploy account holds **no wildcard sudo rules**. It gets exactly four root commands, each pinned to its full argument list: stop the service, start the service, run the argument-free backup helper, and tail 50 lines of the service journal. See step 2 below for why that matters.

### One-time setup

**1. Tailscale OAuth client** (lets the runner join your tailnet as a temporary node)
- Tailscale admin console → **Settings → OAuth clients** → Generate client
- Scope: `Devices Core` write access
- Tag it `tag:ci`
- In your tailnet's ACL policy file, make sure the tag is defined and allowed to reach the server, e.g.:
  ```jsonc
  "tagOwners": {
    "tag:ci": ["autogroup:admin"],
  },
  ```
  If your ACLs restrict traffic between devices (not "accept all"), add a `grants`/`acls` rule permitting `tag:ci` → the server (port 22).

**2. Deploy user, deploy-tree ownership, and SSH key on the server**

The `deploy` account **owns the deploy tree** and rsyncs into it directly as itself — no sudo involved in the file sync at all. Only three things still need root, and each is granted as an exact, argument-pinned command.

```bash
sudo useradd -m -s /bin/bash deploy

# Let deploy own /opt/ncsscheduler, with www-data as the group so the service
# can still read everything. The setgid bit on directories makes every file
# rsync creates inherit the www-data group automatically.
sudo chown -R deploy:www-data /opt/ncsscheduler
sudo find /opt/ncsscheduler -type d -exec chmod g+s {} +
sudo chmod -R g+rX /opt/ncsscheduler

# The database itself stays owned by the service account -- the app writes it,
# the deploy never touches it (rsync excludes *.db / -shm / -wal / .bak-*).
sudo chown www-data:www-data /opt/ncsscheduler/ncsscheduler.db*
```

> This ownership change is what makes `sudo rsync` unnecessary in the first place. Do it **before** installing the new sudoers file, or the next deploy will have neither the old sudo grant nor the filesystem permissions it replaces.

**Backup helper.** Install `deploy/ncsscheduler-backup-db` from this repo as a root-owned, argument-free script. It does the `cp`/snapshot and old-backup pruning internally, so no `cp` with a wildcard destination has to be granted:

```bash
sudo install -o root -g root -m 0755 \
    deploy/ncsscheduler-backup-db /usr/local/sbin/ncsscheduler-backup-db
sudo apt install -y sqlite3   # optional; enables a consistent live-DB snapshot
sudo -u deploy sudo /usr/local/sbin/ncsscheduler-backup-db   # smoke-test it
```

**Sudoers.** Write the file to a temp path, validate it, *then* install it — a broken `/etc/sudoers.d/` file can lock you out of sudo entirely:

```bash
sudo tee /etc/sudoers.d/.ncsscheduler-deploy.new > /dev/null <<'EOF'
Defaults:deploy !requiretty
deploy ALL=(root) NOPASSWD: /usr/bin/systemctl stop ncsscheduler, /usr/bin/systemctl start ncsscheduler, /usr/local/sbin/ncsscheduler-backup-db "", /usr/bin/journalctl -u ncsscheduler -n 50 --no-pager
EOF

sudo visudo -c -f /etc/sudoers.d/.ncsscheduler-deploy.new   # must print "parsed OK"
sudo install -o root -g root -m 0440 \
    /etc/sudoers.d/.ncsscheduler-deploy.new /etc/sudoers.d/ncsscheduler-deploy
sudo rm /etc/sudoers.d/.ncsscheduler-deploy.new
sudo visudo -c

# Confirm: no `*` should appear anywhere in the output for this app.
sudo -l -U deploy
```

> **Why no wildcards.** The previous rule granted `/usr/bin/rsync *` and `/usr/bin/cp /opt/ncsscheduler/ncsscheduler.db *`. Both are root-equivalent, not narrow: `sudo rsync` with an unconstrained argument list can read or overwrite *any* file on the box (`/etc/shadow`, `/root/.ssh/authorized_keys`, `/etc/sudoers.d/` itself), and `--rsync-path=`/`-e` make it execute arbitrary commands as root. Whoever held the deploy SSH key owned the whole server, not just this app.
>
> **Sudo matches the entire command line**, so every entry above has to be the exact, full line the workflow runs — `journalctl -u ncsscheduler -n 50 --no-pager`, not `journalctl -u ncsscheduler`.
>
> **The `""` is load-bearing.** A command listed with no argument spec after it permits *any* arguments — `/usr/local/sbin/ncsscheduler-backup-db` alone would let the caller pass whatever it likes, not just the argument-free invocation the script assumes. The explicit `""` pins it to "no arguments".
>
> **Mode 0440 is mandatory.** `tee` creates files with your default umask instead, and sudo silently ignores a misconfigured file — every sudo call then falls back to demanding a password. `visudo -c` reports wrong permissions. The `!requiretty` line is defensive in case the server sets `Defaults requiretty` globally, which also breaks NOPASSWD sudo over non-interactive SSH.

**Ordering.** The sudoers file and `deploy.yml` must change together. A workflow that calls `ncsscheduler-backup-db` against the old sudoers file fails at the backup step; the old workflow's `sudo rsync` against the new sudoers file fails at the sync step. Apply the server-side changes above first, then merge and release.

On your workstation, generate a dedicated deploy keypair and install the public half:
```bash
ssh-keygen -t ed25519 -f deploy_key -C "github-actions-deploy" -N ""
ssh-copy-id -i deploy_key.pub deploy@<server-tailscale-hostname>
```

**3. GitHub repo secrets** (Settings → Secrets and variables → Actions)

| Secret | Value |
|---|---|
| `TS_OAUTH_CLIENT_ID` | from step 1 |
| `TS_OAUTH_SECRET` | from step 1 |
| `SSH_PRIVATE_KEY` | contents of `deploy_key` (private half) from step 2 |
| `DEPLOY_HOST` | server's Tailscale hostname, e.g. `ncs-server.tailnet.ts.net` |
| `DEPLOY_USER` | `deploy` |

The workflow also references a `production` GitHub Environment — create one (Settings → Environments) if you want required reviewers/approval before a deploy runs; otherwise remove the `environment: production` line from the workflow.

**Workflow constants** — non-sensitive, so they're hardcoded in the `env:` block at the top of `deploy.yml` rather than stored as secrets. Edit them there directly if your setup differs:

| Variable | Default | What it is |
|---|---|---|
| `DEPLOY_PATH` | `/opt/ncsscheduler` | Server directory the publish output is synced to |
| `SERVICE_NAME` | `ncsscheduler` | systemd service stopped/started/journaled during deploy |
| `APP_PORT` | `5000` | Local port the health check polls after restart — must match `ASPNETCORE_URLS` in the systemd unit, not the 5106 dev-time port from `launchSettings.json` |

### Triggering a deploy

Push/merge to `master` as usual (this only builds via `dotnet.yml`), then cut a [GitHub Release](https://github.com/MikeWills/net-scheduler/releases/new) from that commit to trigger the actual deploy.

---

## Environment Variables

Sensitive and environment-specific config is set via environment variables in the systemd unit file rather than in config files:

```bash
App__BaseUrl=https://ncs.example.com
Email__Username=your-smtp-username
Email__Password=your-smtp-password
ConnectionStrings__DefaultConnection=Data Source=/opt/ncsscheduler/ncsscheduler.db
ASPNETCORE_ENVIRONMENT=Production
```

ASP.NET Core maps `__` (double-underscore) to `:` in config keys.

> **systemd quoting:** Values containing spaces must have the entire `KEY=VALUE` pair wrapped in outer quotes:
> ```ini
> Environment="ConnectionStrings__DefaultConnection=Data Source=/opt/ncsscheduler/ncsscheduler.db"
> ```
> Using inner quotes (`Environment=KEY="value with spaces"`) passes the literal quote characters to the app and causes a startup crash.

---

## systemd Service

Example `/etc/systemd/system/ncsscheduler.service`:

```ini
[Unit]
Description=NCS Scheduler
After=network.target

[Service]
WorkingDirectory=/opt/ncsscheduler
ExecStart=/usr/bin/dotnet /opt/ncsscheduler/NcsScheduler.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
# Without this, Kestrel falls back to its own default (port 5000, but don't
# rely on that) rather than the port Apache/the deploy health check expect --
# set it explicitly so the real listening port is never in question.
Environment=ASPNETCORE_URLS=http://localhost:5000
Environment=App__BaseUrl=https://ncs.example.com
Environment="ConnectionStrings__DefaultConnection=Data Source=/opt/ncsscheduler/ncsscheduler.db"
Environment=Email__Username=your-smtp-username
Environment=Email__Password=your-smtp-password

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable ncsscheduler
sudo systemctl start ncsscheduler
sudo systemctl status ncsscheduler
```

---

## Apache Virtual Host

```apache
<VirtualHost *:443>
    ServerName ncs.example.com

    ProxyPreserveHost On
    ProxyPass / http://localhost:5000/
    ProxyPassReverse / http://localhost:5000/

    SSLEngine on
    # ... Let's Encrypt cert paths
</VirtualHost>
```

Enable required modules if not already active:

```bash
sudo a2enmod proxy proxy_http
sudo systemctl reload apache2
```
