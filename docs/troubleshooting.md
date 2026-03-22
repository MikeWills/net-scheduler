# Troubleshooting

## Service Won't Start

**Check the logs first:**

```bash
sudo journalctl -u ncsscheduler -n 100 --no-pager
```

### "Access to the path ... is denied" (database file)

The service runs as `www-data`. The database file and its parent directory must be owned and writable by that user:

```bash
sudo chown www-data:www-data /opt/ncsscheduler/ncsscheduler.db
sudo chmod 660 /opt/ncsscheduler/ncsscheduler.db
# Also ensure the directory is writable (SQLite needs to create a -wal and -shm file)
sudo chown www-data:www-data /opt/ncsscheduler
sudo chmod 750 /opt/ncsscheduler
```

### "No such file or directory" for the database

The path in `ConnectionStrings__DefaultConnection` doesn't exist or is wrong. Check the environment variable in `/etc/systemd/system/ncsscheduler.service` and confirm the path is absolute.

### Startup crash with no useful message

Likely a bad environment variable value — typically caused by incorrect systemd quoting. Values containing spaces (like `Data Source=...`) must wrap the **entire** `KEY=VALUE` pair in outer quotes:

```ini
# Correct
Environment="ConnectionStrings__DefaultConnection=Data Source=/opt/ncsscheduler/ncsscheduler.db"

# Wrong — passes literal quote chars to the app
Environment=ConnectionStrings__DefaultConnection="Data Source=/opt/ncsscheduler/ncsscheduler.db"
```

After editing the unit file, always reload:

```bash
sudo systemctl daemon-reload
sudo systemctl restart ncsscheduler
```

---

## Email Not Sending

### Nothing arrives, no error shown

1. Check `Email__SmtpHost`, `Email__SmtpPort`, `Email__Username`, and `Email__Password` are all set.
2. Confirm `Email__UseSsl` matches what your SMTP provider expects (Mailgun on port 587 uses STARTTLS, not SSL — set `UseSsl: true` which enables STARTTLS via MailKit).
3. Verify the sending domain is verified with your SMTP provider.

### "Authentication failed" in logs

The SMTP credentials are wrong. For Mailgun, the username is typically the full `postmaster@yourdomain.com` address, not just the local part.

### Emails go to spam

Set `Email__FromAddress` to an address on a domain with valid SPF/DKIM records. Using a Mailgun sandbox domain for production will cause spam filtering.

### Test email delivery in development

Set the SMTP credentials in user secrets and run the app locally. Trigger a password reset or invitation to send a real email.

---

## iCal Feed Returns 404

The feed URL contains a token that is generated on first use:

- **Net Controller feed** — token is created the first time the user visits their dashboard. If they've never logged in, there's no token yet.
- **Band Coordinator feed** — token is generated when the coordinator first visits the Assignments page.

If the user reports a 404, ask them to log in and visit their dashboard (NCS) or the Assignments page (BC) to trigger token generation.

The feed URLs are displayed on those pages — direct the user there to copy the current link.

---

## Sessions Not Appearing

### Public schedule is empty

The background session generator may not have run yet. It fires immediately on startup and every 24 hours after. If the app was just deployed:

1. Check the logs for `[SessionGenerator]` entries — they confirm the generator ran and how many sessions were created.
2. If no sessions exist for a net, confirm the net is **Active** and has at least one `NetScheduleRule` configured (Admin → Nets → Edit).

### A specific date is missing

Session generation looks ahead 9 weeks from today. Dates further out won't have sessions yet — this is expected.

If a date within 9 weeks is missing, check whether the net has a **season window** set that excludes that date (e.g., a net configured to run only May–September).

---

## Wrong Assignment Showing

### Standing assignment not applying

`StandingAssignment.DayOfWeek` is stored in **Eastern local time**, but sessions are stored in UTC. A net that runs Monday at 01:00 UTC is Sunday Eastern — the standing assignment must be set to Sunday, not Monday.

The `ScheduleService.ToEasternDate()` helper handles this conversion internally. If assignments seem off by one day seasonally, this is the cause — verify the standing assignment's day of week matches the Eastern local day the net actually airs.

---

## Apache Proxy Issues

### Site returns 502 Bad Gateway

The app isn't running or isn't listening on the expected port. Check:

```bash
sudo systemctl status ncsscheduler
sudo ss -tlnp | grep 5106
```

### Redirects loop or produce wrong URLs

Ensure `ProxyPreserveHost On` is set in the Apache virtual host and `App__BaseUrl` is set to the correct public URL in the systemd environment. Without `BaseUrl`, the app derives URLs from the incoming request; with a reverse proxy this can produce `http://` links over an `https://` connection.

---

## Login / Account Issues

### Admin account doesn't exist after first deploy

The seeder creates the admin user from `SeedAdmin:Email` and `SeedAdmin:Password` in `appsettings.Development.json` (development only) or from `appsettings.json`. In production, seed credentials are not typically set — create the admin account manually using the ASP.NET Core Identity tooling, or temporarily set `SeedAdmin` in the environment and restart.

### "Your account is locked out"

Identity lockout is enabled by default after repeated failed logins. To unlock a user, use **Admin → Manage Users** (SuperAdmin only). Alternatively reset via EF:

```bash
# Connect to the SQLite DB and clear lockout
sqlite3 /opt/ncsscheduler/ncsscheduler.db \
  "UPDATE AspNetUsers SET LockoutEnd = NULL, AccessFailedCount = 0 WHERE Email = 'user@example.com';"
sudo systemctl restart ncsscheduler
```
