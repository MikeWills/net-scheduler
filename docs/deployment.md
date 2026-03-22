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
