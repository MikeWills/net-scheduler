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

`.github/workflows/deploy.yml` deploys to the server automatically whenever a **GitHub Release is published**. The runner joins the Tailscale network (the server has no public SSH access) as an ephemeral node, then over SSH: backs up the SQLite DB, stops the service, `rsync`s the publish output to `/opt/ncsscheduler/`, restores ownership to `www-data`, restarts the service, and polls `http://localhost:5106/` for a response before declaring success.

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

**2. Deploy user + SSH key on the server**
```bash
sudo useradd -m -s /bin/bash deploy
sudo usermod -aG www-data deploy
sudo chmod -R g+w /opt/ncsscheduler

# Restrict sudo to only the commands the workflow needs
sudo tee /etc/sudoers.d/ncsscheduler-deploy > /dev/null <<'EOF'
Defaults:deploy !requiretty
deploy ALL=(root) NOPASSWD: /usr/bin/systemctl stop ncsscheduler, /usr/bin/systemctl start ncsscheduler, /usr/bin/chown -R www-data\:www-data /opt/ncsscheduler, /usr/bin/cp /opt/ncsscheduler/ncsscheduler.db *, /usr/bin/journalctl -u ncsscheduler *
EOF
sudo chmod 0440 /etc/sudoers.d/ncsscheduler-deploy
sudo visudo -c
```

> `/etc/sudoers.d/` files **must** be mode `0440` — `tee` creates them with your default umask instead, so sudo silently ignores the file (and every sudo call falls back to demanding a password) until you `chmod` it. `visudo -c` will tell you if a file has the wrong permissions. The `!requiretty` line is defensive in case your server otherwise sets `Defaults requiretty` globally, which also breaks NOPASSWD sudo over a non-interactive SSH session.

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
| `APP_PORT` | `5106` | Local port the health check polls after restart |

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
    ProxyPass / http://localhost:5106/
    ProxyPassReverse / http://localhost:5106/

    SSLEngine on
    # ... Let's Encrypt cert paths
</VirtualHost>
```

Enable required modules if not already active:

```bash
sudo a2enmod proxy proxy_http
sudo systemctl reload apache2
```
