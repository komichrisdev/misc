---
name: plex-guard
description: Audit system security for the Plex host without changing service behavior. Use for daily Plex Guard checks, AV scan review, hardening recommendations, and Discord security reports while preserving Plex remote access, PIA, qBittorrent WebUI, SSH, Plex media organizer, and Crypto Keeper.
---

# Plex Guard

Plex Guard is a report-only security automation for this host. It audits and recommends; it does not remediate unless the user explicitly asks in a separate task.

## Model

Daily automation should use `gpt-5.4-mini`. This task needs concise security triage and report writing, not a frontier model.

## Non-Breaking Rules

Do not disable, restart, reconfigure, firewall-block, quarantine, delete, or move anything during a guard pass.

Preserve these working requirements:

- Plex must remain remotely accessible, including port `32400`.
- PIA VPN must continue to start at boot.
- qBittorrent WebUI must remain available on the local network.
- SSH must remain available for this machine.
- Existing automations must continue: Plex media organizer and Crypto Keeper.

If a hardening recommendation could affect one of those requirements, label it as a staged recommendation that needs explicit approval and rollback planning.

## Daily Workflow

1. Start the bundled ClamAV scan script from cron.
2. Let the scan run for the configured timeout window.
3. Run the report script from a later cron entry so it reads the completed scan result instead of polling.
4. Review antivirus status and scan results.
5. Summarize findings as:
   - `attention required`: needs user follow-up.
   - `warning`: useful hardening or limited visibility.
   - `ok`: no action needed.
6. Write a short daily report. Include a clean report even when no attention is required.
7. Send the report to Discord.

Manual run:

```bash
/home/komichris/.codex/skills/plex-guard/scripts/start_clamav_scan.sh
/home/komichris/.codex/skills/plex-guard/scripts/run_daily.sh
```

## What To Check

- ClamAV installed, definitions fresh, and scan results.
- Rootkit tooling availability and any scan warnings.
- SSH posture: key-only preference, root login posture, X11 forwarding, listener presence.
- Plex listener and service presence.
- PIA service presence and startup configuration.
- qBittorrent WebUI listener, authentication config, VPN interface binding, and port-forward helper.
- Firewall/fail2ban/AppArmor/unattended-upgrades visibility.
- Daily cron and systemd timer entries for Plex media organizer and Crypto Keeper.

Root-only checks may return permission errors. Report those as visibility gaps rather than trying to escalate.
