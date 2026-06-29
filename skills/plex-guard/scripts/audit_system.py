#!/usr/bin/env python3
import argparse
import configparser
import datetime as dt
import glob
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path


HOST_REQUIREMENTS = [
    "Plex remote access must remain available on port 32400.",
    "PIA VPN must run on system start.",
    "qBittorrent WebUI must stay available on the local network.",
    "SSH must stay available.",
    "Plex media organizer and Crypto Keeper automations must keep running.",
]

SYSTEM_COMMAND_DIRS = [
    "/usr/local/sbin",
    "/usr/local/bin",
    "/usr/sbin",
    "/usr/bin",
    "/sbin",
    "/bin",
]


def run(cmd, timeout=20):
    try:
        proc = subprocess.run(
            cmd,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=timeout,
            check=False,
        )
        return {
            "cmd": cmd,
            "returncode": proc.returncode,
            "stdout": proc.stdout.strip(),
            "stderr": proc.stderr.strip(),
            "timeout": False,
        }
    except subprocess.TimeoutExpired as exc:
        return {
            "cmd": cmd,
            "returncode": 124,
            "stdout": (exc.stdout or "").strip() if isinstance(exc.stdout, str) else "",
            "stderr": f"Timed out after {timeout}s",
            "timeout": True,
        }
    except FileNotFoundError:
        return {
            "cmd": cmd,
            "returncode": 127,
            "stdout": "",
            "stderr": "command not found",
            "timeout": False,
        }


def command(name):
    found = shutil.which(name)
    if found:
        return found
    for directory in SYSTEM_COMMAND_DIRS:
        candidate = Path(directory) / name
        if os.access(candidate, os.X_OK):
            return str(candidate)
    return None


def now_iso():
    return dt.datetime.now(dt.timezone.utc).astimezone().isoformat(timespec="seconds")


def check(category, name, status, summary, severity="info", evidence=None, recommendation=""):
    return {
        "category": category,
        "name": name,
        "status": status,
        "severity": severity,
        "summary": summary,
        "recommendation": recommendation,
        "evidence": evidence or {},
    }


def read_text(path, limit=20000):
    try:
        text = Path(path).read_text(encoding="utf-8", errors="replace")
    except Exception as exc:
        return "", str(exc)
    if len(text) > limit:
        return text[:limit] + "\n...[truncated]...", ""
    return text, ""


def parse_ini(path):
    parser = configparser.RawConfigParser()
    parser.optionxform = str
    try:
        parser.read(path, encoding="utf-8")
    except Exception:
        return {}
    data = {}
    for section in parser.sections():
        for key, value in parser.items(section):
            data[f"{section}.{key}"] = value
    return data


def systemctl_check(unit):
    active = run(["systemctl", "is-active", unit], timeout=8)
    enabled = run(["systemctl", "is-enabled", unit], timeout=8)
    return {
        "active": active["stdout"] or active["stderr"],
        "active_code": active["returncode"],
        "enabled": enabled["stdout"] or enabled["stderr"],
        "enabled_code": enabled["returncode"],
    }


def listening_ports():
    result = run(["ss", "-tlnp"], timeout=10)
    text = "\n".join(part for part in [result.get("stdout", ""), result.get("stderr", "")] if part)
    return result, text


def has_port(ss_text, port):
    return re.search(rf"[:.]?{re.escape(str(port))}\s", ss_text) is not None


def clamav_scan_result_check(scan_result, fallback_paths, scan_timeout, result_path):
    def int_field(name, default=-1):
        value = scan_result.get(name, default)
        if value is None:
            return default
        return int(value)

    infected = int_field("infected_files")
    scanned = int_field("scanned_files")
    total_errors = int_field("total_errors")
    returncode = int_field("returncode")
    timeout = bool(scan_result.get("timeout", False))
    paths = scan_result.get("paths") or fallback_paths
    missing_paths = scan_result.get("missing_paths") or []
    duration = scan_result.get("duration_seconds")
    duration_text = f" Duration: {duration}s." if duration is not None else ""

    if timeout:
        status = "warning"
        severity = "medium"
        summary = f"ClamAV scan timed out after {scan_timeout}s.{duration_text}"
        recommendation = "Increase PLEX_GUARD_SCAN_TIMEOUT_SECONDS or narrow scan paths if this repeats."
    elif returncode == 1 or infected > 0:
        status = "attention"
        severity = "high"
        summary = f"ClamAV found {infected} infected file(s).{duration_text}"
        recommendation = "Review the scan output and decide quarantine/removal manually."
    elif returncode == 0:
        status = "ok"
        severity = "info"
        summary = f"ClamAV scan completed cleanly. Scanned files: {scanned if scanned >= 0 else 'unknown'}.{duration_text}"
        recommendation = ""
    else:
        status = "warning"
        severity = "medium"
        error_text = f" Total errors: {total_errors}." if total_errors >= 0 else ""
        summary = f"ClamAV scan returned code {returncode}.{duration_text}{error_text}"
        recommendation = "Review scan output and ClamAV configuration."

    return check(
        "antivirus",
        "Daily ClamAV scan",
        status,
        summary,
        severity,
        evidence={
            "result_json": str(result_path),
            "paths": paths,
            "missing_paths": missing_paths,
            "returncode": returncode,
            "timeout": timeout,
            "started_at": scan_result.get("started_at"),
            "finished_at": scan_result.get("finished_at"),
            "output_file": scan_result.get("output_file"),
            "total_errors": total_errors,
            "output_tail": scan_result.get("output_tail", ""),
        },
        recommendation=recommendation,
    )


def clamav_checks(scan_paths, scan_timeout, scan_result_json=None):
    checks = []
    clamscan = command("clamscan")
    freshclam = command("freshclam")

    if not clamscan:
        checks.append(check(
            "antivirus",
            "ClamAV installed",
            "attention",
            "ClamAV clamscan is not installed or not in PATH.",
            "high",
            recommendation="Install ClamAV before relying on daily AV scans.",
        ))
        return checks

    version = run([clamscan, "--version"], timeout=10)
    checks.append(check(
        "antivirus",
        "ClamAV installed",
        "ok" if version["returncode"] == 0 else "warning",
        version["stdout"] or version["stderr"] or "ClamAV is present.",
        "info",
        evidence={"returncode": version["returncode"]},
    ))

    db_candidates = [
        Path("/var/lib/clamav/daily.cvd"),
        Path("/var/lib/clamav/daily.cld"),
        Path("/var/lib/clamav/main.cvd"),
        Path("/var/lib/clamav/main.cld"),
    ]
    mtimes = [p.stat().st_mtime for p in db_candidates if p.exists()]
    if mtimes:
        newest = dt.datetime.fromtimestamp(max(mtimes), dt.timezone.utc).astimezone()
        age_hours = (dt.datetime.now(dt.timezone.utc).timestamp() - max(mtimes)) / 3600
        status = "ok" if age_hours <= 48 else "warning"
        checks.append(check(
            "antivirus",
            "ClamAV definition age",
            status,
            f"Newest local ClamAV database timestamp: {newest.isoformat(timespec='seconds')} ({age_hours:.1f}h old).",
            "medium" if status == "warning" else "info",
            recommendation="Run or fix freshclam if definitions are older than 48 hours." if status == "warning" else "",
        ))
    elif freshclam:
        fresh_version = run([freshclam, "--version"], timeout=10)
        checks.append(check(
            "antivirus",
            "ClamAV definitions",
            "warning",
            fresh_version["stdout"] or "Could not find local database files, but freshclam exists.",
            "medium",
            recommendation="Check freshclam service/timer and database path.",
        ))
    else:
        checks.append(check(
            "antivirus",
            "ClamAV definitions",
            "attention",
            "Could not find ClamAV database files or freshclam.",
            "high",
            recommendation="Install clamav-freshclam and refresh definitions.",
        ))

    existing_paths = [p for p in scan_paths if Path(p).exists()]
    missing_paths = [p for p in scan_paths if not Path(p).exists()]
    if not existing_paths:
        checks.append(check(
            "antivirus",
            "Daily scan",
            "attention",
            "No configured scan paths exist.",
            "high",
            evidence={"configured_paths": scan_paths, "missing_paths": missing_paths},
        ))
        return checks

    if scan_result_json:
        result_path = Path(scan_result_json)
        if not result_path.exists():
            checks.append(check(
                "antivirus",
                "Daily ClamAV scan",
                "warning",
                f"No ClamAV scan result is available yet at {result_path}.",
                "medium",
                evidence={"result_json": str(result_path), "paths": existing_paths, "missing_paths": missing_paths},
                recommendation="Confirm the Plex Guard ClamAV start timer ran and that the report timer is delayed beyond the scan timeout.",
            ))
            return checks
        try:
            scan_result = json.loads(result_path.read_text(encoding="utf-8"))
        except Exception as exc:
            checks.append(check(
                "antivirus",
                "Daily ClamAV scan",
                "warning",
                f"Could not read ClamAV scan result: {exc}",
                "medium",
                evidence={"result_json": str(result_path)},
                recommendation="Inspect the scan result JSON and ClamAV log.",
            ))
            return checks
        checks.append(clamav_scan_result_check(scan_result, existing_paths, scan_timeout, result_path))
        return checks

    scan_cmd = [
        clamscan,
        "--infected",
        "--recursive=yes",
        "--suppress-ok-results",
        "--cross-fs=no",
        "--max-filesize=100M",
        "--max-scansize=400M",
        *existing_paths,
    ]
    if command("ionice"):
        scan_cmd = ["ionice", "-c3", *scan_cmd]
    if command("nice"):
        scan_cmd = ["nice", "-n", "15", *scan_cmd]
    scan = run(scan_cmd, timeout=scan_timeout)
    scan_text = "\n".join(part for part in [scan["stdout"], scan["stderr"]] if part)
    infected = parse_int_from_scan(scan_text, "Infected files")
    scanned = parse_int_from_scan(scan_text, "Scanned files")
    if scan["timeout"]:
        status = "warning"
        severity = "medium"
        summary = f"ClamAV scan timed out after {scan_timeout}s."
        recommendation = "Increase PLEX_GUARD_SCAN_TIMEOUT_SECONDS or narrow scan paths if this repeats."
    elif scan["returncode"] == 1 or infected > 0:
        status = "attention"
        severity = "high"
        summary = f"ClamAV found {infected} infected file(s)."
        recommendation = "Review the scan output and decide quarantine/removal manually."
    elif scan["returncode"] == 0:
        status = "ok"
        severity = "info"
        summary = f"ClamAV scan completed cleanly. Scanned files: {scanned if scanned >= 0 else 'unknown'}."
        recommendation = ""
    else:
        status = "warning"
        severity = "medium"
        summary = f"ClamAV scan returned code {scan['returncode']}."
        recommendation = "Review scan stderr and ClamAV configuration."
    checks.append(check(
        "antivirus",
        "Daily ClamAV scan",
        status,
        summary,
        severity,
        evidence={
            "paths": existing_paths,
            "missing_paths": missing_paths,
            "returncode": scan["returncode"],
            "timeout": scan["timeout"],
            "output_tail": tail(scan_text, 4000),
        },
        recommendation=recommendation,
    ))
    return checks


def parse_int_from_scan(text, label):
    match = re.search(rf"{re.escape(label)}:\s*([0-9]+)", text)
    if not match:
        return -1
    return int(match.group(1))


def tail(text, limit):
    if len(text) <= limit:
        return text
    return text[-limit:]


def file_age_hours(path):
    mtime = path.stat().st_mtime
    return (dt.datetime.now(dt.timezone.utc).timestamp() - mtime) / 3600


def rootkit_checks(timeout):
    checks = []
    chkrootkit = command("chkrootkit")
    if chkrootkit:
        daily_log = Path("/var/log/chkrootkit/log.today")
        daily_text = ""
        daily_error = ""
        if os.geteuid() != 0 and daily_log.exists():
            daily_text, daily_error = read_text(daily_log, limit=20000)
        if daily_text and not daily_error:
            age_hours = file_age_hours(daily_log)
            stale = age_hours > 36
            has_warnings = "WARNING" in daily_text
            status = "warning" if stale or has_warnings else "ok"
            checks.append(check(
                "rootkit",
                "chkrootkit",
                status,
                f"chkrootkit is installed at {chkrootkit}; latest root daily log is {age_hours:.1f}h old"
                + (" and contains warnings." if has_warnings else " and contains no warnings."),
                "medium" if status == "warning" else "info",
                evidence={
                    "scanner": chkrootkit,
                    "daily_log": str(daily_log),
                    "age_hours": round(age_hours, 1),
                    "output_tail": tail(daily_text, 4000),
                },
                recommendation="Review /var/log/chkrootkit/log.today as root before taking action." if status == "warning" else "",
            ))
        else:
            result = run([chkrootkit, "-q"], timeout=timeout)
            output = "\n".join(part for part in [result["stdout"], result["stderr"]] if part).strip()
            if "needs root privileges" in output.lower():
                status = "warning"
                summary = "chkrootkit is installed, but full scan output requires root and no readable daily log was available."
            else:
                status = "ok" if result["returncode"] == 0 and not output else "warning"
                summary = "chkrootkit returned no notable output." if status == "ok" else "chkrootkit produced output or nonzero status."
            checks.append(check(
                "rootkit",
                "chkrootkit",
                status,
                summary,
                "medium" if status == "warning" else "info",
                evidence={"scanner": chkrootkit, "returncode": result["returncode"], "timeout": result["timeout"], "output_tail": tail(output, 4000)},
                recommendation="Review chkrootkit output manually before taking action." if status == "warning" else "",
            ))
    else:
        checks.append(check("rootkit", "chkrootkit", "warning", "chkrootkit is not installed.", "low"))

    rkhunter = command("rkhunter")
    if rkhunter:
        result = run([rkhunter, "--check", "--sk", "--rwo", "--nocolors"], timeout=timeout)
        output = "\n".join(part for part in [result["stdout"], result["stderr"]] if part).strip()
        if result["timeout"]:
            status = "warning"
            summary = "rkhunter timed out."
        elif "you must be the root user" in output.lower():
            status = "warning"
            summary = "rkhunter requires root; warning-only output could not be captured by this user."
        elif output:
            status = "warning"
            summary = "rkhunter reported warnings."
        else:
            status = "ok"
            summary = "rkhunter produced no warning-only output."
        checks.append(check(
            "rootkit",
            "rkhunter",
            status,
            summary,
            "medium" if status == "warning" else "info",
            evidence={"returncode": result["returncode"], "timeout": result["timeout"], "output_tail": tail(output, 4000)},
            recommendation="Run rkhunter or review /var/log/rkhunter.log as root before taking action." if status == "warning" else "",
        ))
    else:
        checks.append(check("rootkit", "rkhunter", "warning", "rkhunter is not installed.", "low"))
    return checks


def service_checks(ss_text):
    checks = []
    units = {
        "Plex Media Server": "plexmediaserver.service",
        "PIA VPN daemon": "piavpn.service",
        "qBittorrent": "qbittorrent-nox@qbittorrent.service",
        "SSH": "ssh.service",
        "Crypto Keeper monitor": "crypto-keeper-monitor.timer",
        "Crypto Keeper report": "crypto-keeper-report.timer",
        "Crypto Keeper Discord": "crypto-keeper-discord.timer",
    }
    for name, unit in units.items():
        state = systemctl_check(unit)
        ok = state["active"] in {"active", "activating"} or (
            unit.endswith(".timer") and state["active"] == "active"
        )
        status = "ok" if ok else "attention"
        severity = "high" if name in {"Plex Media Server", "PIA VPN daemon", "qBittorrent", "SSH"} and not ok else "medium" if not ok else "info"
        summary = f"{unit}: active={state['active']}, enabled={state['enabled']}"
        recommendation = f"Investigate {unit}; do not disable required access while fixing." if not ok else ""
        if name == "qBittorrent" and not ok and (has_port(ss_text, 8080) or has_port(ss_text, 8090)):
            status = "warning"
            severity = "medium"
            summary = f"{unit}: active={state['active']}, enabled={state['enabled']}; WebUI listener is present."
            recommendation = "qBittorrent is reachable now; confirm its startup path before rebooting or disabling anything."
        checks.append(check(
            "services",
            name,
            status,
            summary,
            severity,
            evidence=state,
            recommendation=recommendation,
        ))

    checks.append(check(
        "services",
        "Plex listener",
        "ok" if has_port(ss_text, 32400) else "attention",
        "Port 32400 is listening." if has_port(ss_text, 32400) else "Plex port 32400 was not found listening.",
        "high" if not has_port(ss_text, 32400) else "info",
        recommendation="Restore Plex remote accessibility before applying other hardening." if not has_port(ss_text, 32400) else "",
    ))
    checks.append(check(
        "services",
        "SSH listener",
        "ok" if has_port(ss_text, 22) else "attention",
        "SSH port 22 is listening." if has_port(ss_text, 22) else "SSH port 22 was not found listening.",
        "high" if not has_port(ss_text, 22) else "info",
        recommendation="Preserve SSH access before changing firewall or sshd settings." if not has_port(ss_text, 22) else "",
    ))
    return checks


def qbittorrent_checks(ss_text):
    checks = []
    config_paths = [
        Path("/var/lib/qbittorrent/.config/qBittorrent/qBittorrent.conf"),
        Path("/home/komichris/.config/qBittorrent/qBittorrent.conf"),
    ]
    selected = next((p for p in config_paths if p.exists()), None)
    if not selected:
        return [check(
            "qbittorrent",
            "qBittorrent config",
            "warning",
            "Could not find qBittorrent config file.",
            "medium",
        )]
    data = parse_ini(selected)
    web_enabled = data.get("Preferences.WebUI\\Enabled", "").lower() == "true"
    web_address = data.get("Preferences.WebUI\\Address", "")
    web_port = data.get("Preferences.WebUI\\Port", "")
    https_enabled = data.get("Preferences.WebUI\\HTTPS\\Enabled", "").lower() == "true"
    password_set = bool(data.get("Preferences.WebUI\\Password_PBKDF2", ""))
    interface = data.get("BitTorrent.Session\\Interface") or data.get("BitTorrent.Session\\InterfaceName", "")
    bt_port = data.get("BitTorrent.Session\\Port") or data.get("Connection\\PortRangeMin", "")

    web_listening = any(has_port(ss_text, port) for port in [web_port, 8080, 8090] if str(port).isdigit())
    status = "ok" if web_enabled and web_listening and password_set else "attention"
    checks.append(check(
        "qbittorrent",
        "WebUI availability",
        status,
        f"WebUI enabled={web_enabled}, address={web_address or '(default)'}, configured_port={web_port or '(unknown)'}, listener_found={web_listening}, https={https_enabled}, password_set={password_set}.",
        "high" if status == "attention" else "info",
        evidence={"config": str(selected)},
        recommendation="Keep WebUI local-network accessible, but require auth and firewall/LAN scoping." if status == "attention" else "",
    ))

    vpn_bound = interface == "wgpia0"
    checks.append(check(
        "qbittorrent",
        "VPN binding",
        "ok" if vpn_bound else "attention",
        f"qBittorrent BitTorrent interface is {interface or '(unset)'}.",
        "high" if not vpn_bound else "info",
        recommendation="Bind qBittorrent traffic to wgpia0 without changing WebUI access." if not vpn_bound else "",
    ))

    if bt_port and has_port(ss_text, bt_port):
        checks.append(check("qbittorrent", "BitTorrent listener", "ok", f"BitTorrent port {bt_port} is listening.", "info"))
    else:
        checks.append(check(
            "qbittorrent",
            "BitTorrent listener",
            "warning",
            f"Configured BitTorrent port {bt_port or '(unknown)'} was not clearly found listening.",
            "medium",
            recommendation="Check PIA port forwarding and qBittorrent port sync before changing firewall rules.",
        ))
    return checks


def read_sshd_settings_fallback():
    settings = {}
    visited = set()

    def parse_file(path):
        path = Path(path)
        if path in visited:
            return
        visited.add(path)
        text, error = read_text(path)
        if error:
            return
        for raw_line in text.splitlines():
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split(None, 1)
            if len(parts) != 2:
                continue
            key = parts[0].lower()
            value = parts[1].strip().lower()
            if key == "include":
                for pattern in value.split():
                    if not pattern.startswith("/"):
                        pattern = str(Path("/etc/ssh") / pattern)
                    for included in sorted(glob.glob(pattern)):
                        parse_file(Path(included))
                continue
            settings.setdefault(key, value)

    parse_file(Path("/etc/ssh/sshd_config"))
    return settings


def ssh_checks():
    sshd = command("sshd") or "/usr/sbin/sshd"
    result = run([sshd, "-T"], timeout=10)
    text = result["stdout"].lower()
    evidence = {"returncode": result["returncode"], "stderr": result["stderr"]}
    checks = []
    if result["returncode"] != 0:
        settings = read_sshd_settings_fallback()
        evidence["fallback"] = "parsed readable sshd_config files"
        if not settings:
            checks.append(check("ssh", "sshd effective config", "warning", "Could not read sshd effective config.", "medium", evidence=evidence))
            return checks
    else:
        settings = {}
        for line in text.splitlines():
            parts = line.split(None, 1)
            if len(parts) == 2:
                settings[parts[0]] = parts[1]
    password_auth = settings.get("passwordauthentication", "unknown")
    root_login = settings.get("permitrootlogin", "unknown")
    x11 = settings.get("x11forwarding", "unknown")
    password_auth_accepted = os.environ.get("PLEX_GUARD_ACCEPT_PASSWORD_AUTH_RISK", "").lower() in {"1", "true", "yes", "on"}
    checks.append(check(
        "ssh",
        "Password authentication",
        "ok" if password_auth == "no" or password_auth_accepted else "warning",
        f"passwordauthentication={password_auth}" + ("; accepted risk" if password_auth_accepted and password_auth != "no" else ""),
        "info" if password_auth == "no" or password_auth_accepted else "medium",
        recommendation="Consider key-only SSH after confirming current access paths." if password_auth != "no" and not password_auth_accepted else "",
    ))
    checks.append(check(
        "ssh",
        "Root login",
        "ok" if root_login in {"no", "prohibit-password"} else "warning",
        f"permitrootlogin={root_login}",
        "medium" if root_login not in {"no", "prohibit-password"} else "info",
        recommendation="Avoid enabling root SSH login." if root_login not in {"no", "prohibit-password"} else "",
    ))
    checks.append(check(
        "ssh",
        "X11 forwarding",
        "ok" if x11 == "no" else "warning",
        f"x11forwarding={x11}",
        "low" if x11 != "no" else "info",
        recommendation="Disable X11 forwarding only if it is not needed for your SSH workflow." if x11 != "no" else "",
    ))
    return checks


def fail2ban_sshd_enabled():
    paths = [Path("/etc/fail2ban/jail.local")]
    jail_dir = Path("/etc/fail2ban/jail.d")
    if jail_dir.exists():
        paths.extend(sorted(jail_dir.glob("*.conf")))
        paths.extend(sorted(jail_dir.glob("*.local")))
    enabled = None
    in_sshd = False
    for path in paths:
        text, error = read_text(path)
        if error:
            continue
        for raw_line in text.splitlines():
            line = raw_line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("[") and line.endswith("]"):
                in_sshd = line.strip("[]").lower() == "sshd"
                continue
            if in_sshd and line.lower().startswith("enabled") and "=" in line:
                enabled = line.split("=", 1)[1].strip().lower() in {"true", "yes", "1", "on"}
    return enabled


def host_hardening_checks(ss_text):
    checks = []
    for tool, package_name in [
        ("fail2ban-client", "fail2ban"),
        (command("aa-status") or "/usr/sbin/aa-status", "apparmor"),
        ("unattended-upgrade", "unattended-upgrades"),
    ]:
        checks.append(check(
            "hardening",
            f"{package_name} availability",
            "ok" if command(tool) else "warning",
            f"{tool} is {'available' if command(tool) else 'not available in PATH'}.",
            "info" if command(tool) else "medium",
        ))

    ufw = command("ufw") or "/usr/sbin/ufw"
    ufw_result = run([ufw, "status", "verbose"], timeout=10)
    if ufw_result["returncode"] == 0:
        checks.append(check("hardening", "UFW visibility", "ok", "UFW status is readable.", "info", evidence={"output": ufw_result["stdout"]}))
    else:
        ufw_state = systemctl_check("ufw.service")
        ufw_ok = ufw_state["active"] in {"active", "activating"} and ufw_state["enabled"] == "enabled"
        checks.append(check(
            "hardening",
            "UFW service",
            "ok" if ufw_ok else "warning",
            f"ufw.service: active={ufw_state['active']}, enabled={ufw_state['enabled']}; detailed rules require root.",
            "info" if ufw_ok else "medium",
            evidence={"ufw_status": {"returncode": ufw_result["returncode"], "stderr": ufw_result["stderr"]}, "service": ufw_state},
            recommendation="Review firewall state as root; preserve Plex remote, SSH, and qBittorrent local WebUI access." if not ufw_ok else "",
        ))

    fail2ban = command("fail2ban-client")
    if fail2ban:
        f2b_result = run([fail2ban, "status"], timeout=10)
        f2b_state = systemctl_check("fail2ban.service")
        sshd_enabled = fail2ban_sshd_enabled()
        f2b_ok = f2b_result["returncode"] == 0 or (
            f2b_state["active"] in {"active", "activating"} and sshd_enabled is True
        )
        checks.append(check(
            "hardening",
            "fail2ban status",
            "ok" if f2b_ok else "warning",
            "fail2ban status is readable." if f2b_result["returncode"] == 0 else f"fail2ban.service: active={f2b_state['active']}, sshd_config_enabled={sshd_enabled}; detailed jail status requires root.",
            "info" if f2b_ok else "medium",
            evidence={"returncode": f2b_result["returncode"], "stdout": f2b_result["stdout"], "stderr": f2b_result["stderr"], "service": f2b_state, "sshd_enabled_config": sshd_enabled},
            recommendation="Check fail2ban as root and ensure SSH is covered." if not f2b_ok else "",
        ))

    exposed = []
    for port in [8080, 8090, 32400, 22, 3389]:
        if has_port(ss_text, port):
            exposed.append(port)
    checks.append(check(
        "hardening",
        "Listening service summary",
        "warning" if 3389 in exposed else "ok",
        f"Observed listening ports of interest: {', '.join(map(str, exposed)) or 'none'}",
        "medium" if 3389 in exposed else "info",
        recommendation="Review RDP exposure on 3389 if it is not intentionally needed." if 3389 in exposed else "",
    ))
    return checks


def automation_checks():
    checks = []
    cron = run(["crontab", "-l"], timeout=10)
    cron_text = cron["stdout"]
    plex_cron = "plex-media-organizer-daily" in cron_text
    guard_scan_cron = "plex-guard-clamav-start" in cron_text
    guard_build_cron = "plex-guard-build-report" in cron_text
    guard_send_cron = "plex-guard-discord-send" in cron_text
    crypto_cron = "crypto-keeper-report-if-due" in cron_text
    checks.append(check(
        "automations",
        "Plex media organizer cron",
        "ok" if plex_cron else "attention",
        "Plex media organizer daily cron is present." if plex_cron else "Plex media organizer cron was not found.",
        "high" if not plex_cron else "info",
    ))
    checks.append(check(
        "automations",
        "Plex Guard cron",
        "ok" if guard_scan_cron and guard_build_cron and guard_send_cron else "warning",
        "Plex Guard split scan/build/send cron is present." if guard_scan_cron and guard_build_cron and guard_send_cron else "Plex Guard split scan/build/send cron was not fully found in current user's crontab.",
        "medium" if not (guard_scan_cron and guard_build_cron and guard_send_cron) else "info",
        evidence={"clamav_start": guard_scan_cron, "build_report": guard_build_cron, "discord_send": guard_send_cron},
        recommendation="Keep separate ClamAV start, report build, and Discord send timers so scans/checks finish overnight and reports send in the morning." if not (guard_scan_cron and guard_build_cron and guard_send_cron) else "",
    ))
    checks.append(check(
        "automations",
        "Crypto Keeper report cron",
        "ok" if crypto_cron else "warning",
        "Crypto Keeper report cron is present." if crypto_cron else "Crypto Keeper report cron was not found.",
        "medium" if not crypto_cron else "info",
    ))
    return checks


def summarize(checks):
    attention = [item for item in checks if item["status"] == "attention"]
    warnings = [item for item in checks if item["status"] == "warning"]
    overall = "attention" if attention else "warning" if warnings else "ok"
    return {
        "overall": overall,
        "attention_count": len(attention),
        "warning_count": len(warnings),
        "ok_count": len([item for item in checks if item["status"] == "ok"]),
    }


def markdown_report(audit):
    summary = audit["summary"]
    lines = [
        "# Plex Guard Daily Security Report",
        "",
        f"Generated: {audit['generated_at']}",
        f"Overall: **{summary['overall'].upper()}**",
        f"Attention: {summary['attention_count']} | Warnings: {summary['warning_count']} | OK: {summary['ok_count']}",
    ]
    if summary["overall"] != "ok":
        lines.extend(["", "## Non-Breaking Requirements", ""])
        lines.extend(f"- {item}" for item in audit["host_requirements"])
    grouped = {}
    for item in audit["checks"]:
        grouped.setdefault(item["category"], []).append(item)
    for category in sorted(grouped):
        visible_items = [item for item in grouped[category] if item["status"] != "ok"]
        if not visible_items:
            continue
        lines.extend(["", f"## {category.title()}"])
        for item in visible_items:
            marker = {"ok": "OK", "warning": "WARN", "attention": "ATTN"}.get(item["status"], item["status"].upper())
            lines.append(f"- **{marker} {item['name']}**: {item['summary']}")
            if item.get("recommendation"):
                lines.append(f"  Recommendation: {item['recommendation']}")
    attention = [item for item in audit["checks"] if item["status"] == "attention"]
    warnings = [item for item in audit["checks"] if item["status"] == "warning"]
    lines.extend(["", "## Copy/Paste Fix Request"])
    if not attention and not warnings:
        lines.extend([
            "```text",
            "Plex Guard found no issues requiring follow-up today.",
            "```",
        ])
    else:
        lines.extend([
            "```text",
            "Act on this Plex Guard report. Fix or propose fixes for the items below without breaking Plex remote access, PIA startup, qBittorrent local WebUI, SSH, Plex media organizer, or Crypto Keeper.",
            "",
            "Items to address:",
        ])
        for index, item in enumerate([*attention, *warnings][:12], start=1):
            lines.append(f"{index}. {item['name']} ({item['status']}): {item['recommendation'] or item['summary']}")
        lines.append("```")
    return "\n".join(lines).rstrip() + "\n"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-json", required=True)
    parser.add_argument("--output-md", required=True)
    parser.add_argument("--scan-path", action="append", default=[])
    parser.add_argument("--scan-timeout", type=int, default=1800)
    parser.add_argument("--scan-result-json")
    parser.add_argument("--rootkit-timeout", type=int, default=180)
    parser.add_argument("--skip-rootkit", action="store_true")
    args = parser.parse_args()

    scan_paths = args.scan_path or ["/home/komichris", "/srv/media", "/tmp"]
    ss_result, ss_text = listening_ports()
    checks = []
    checks.extend(clamav_checks(scan_paths, args.scan_timeout, args.scan_result_json))
    if not args.skip_rootkit:
        checks.extend(rootkit_checks(args.rootkit_timeout))
    checks.extend(service_checks(ss_text))
    checks.extend(qbittorrent_checks(ss_text))
    checks.extend(ssh_checks())
    checks.extend(host_hardening_checks(ss_text))
    checks.extend(automation_checks())

    audit = {
        "generated_at": now_iso(),
        "host_requirements": HOST_REQUIREMENTS,
        "scan_paths": scan_paths,
        "ss": {
            "returncode": ss_result["returncode"],
            "stderr": ss_result["stderr"],
        },
        "summary": summarize(checks),
        "checks": checks,
    }
    Path(args.output_json).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output_md).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output_json).write_text(json.dumps(audit, indent=2), encoding="utf-8")
    Path(args.output_md).write_text(markdown_report(audit), encoding="utf-8")
    print(json.dumps(audit["summary"], indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
