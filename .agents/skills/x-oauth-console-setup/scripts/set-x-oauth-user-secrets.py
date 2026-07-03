#!/usr/bin/env python3
"""Prompt for X OAuth 2.0 credentials and store them in ASP.NET Core user secrets."""

from __future__ import annotations

import argparse
import getpass
import json
import os
import stat
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


CLIENT_ID_KEY = "Authentication:X:ClientId"
CLIENT_SECRET_KEY = "Authentication:X:ClientSecret"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Prompt for X OAuth 2.0 credentials and import them into .NET user secrets."
    )
    parser.add_argument(
        "--project",
        default="api/Intervals.Api",
        help="API project directory or .csproj path. Default: api/Intervals.Api",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Resolve the target user-secrets file without prompting or writing.",
    )
    return parser.parse_args()


def fail(message: str) -> None:
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(1)


def resolve_project(project_arg: str) -> Path:
    project = Path(project_arg).expanduser().resolve()
    if project.is_dir():
        projects = sorted(project.glob("*.csproj"))
        if not projects:
            fail(f"no .csproj file found in {project}")
        if len(projects) > 1:
            names = ", ".join(str(path) for path in projects)
            fail(f"multiple .csproj files found in {project}: {names}")
        return projects[0]
    if project.is_file() and project.suffix == ".csproj":
        return project
    fail(f"project path does not exist or is not a .csproj: {project}")


def user_secrets_id(project_file: Path) -> str:
    try:
        tree = ET.parse(project_file)
    except ET.ParseError as exc:
        fail(f"could not parse project file {project_file}: {exc}")

    root = tree.getroot()
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] == "UserSecretsId" and element.text and element.text.strip():
            return element.text.strip()
    fail(f"project file has no UserSecretsId: {project_file}")


def user_secrets_path(secrets_id: str) -> Path:
    if os.name == "nt":
        appdata = os.environ.get("APPDATA")
        if not appdata:
            fail("APPDATA is not set; cannot locate Windows user-secrets directory")
        return Path(appdata) / "Microsoft" / "UserSecrets" / secrets_id / "secrets.json"
    return Path.home() / ".microsoft" / "usersecrets" / secrets_id / "secrets.json"


def load_existing_secrets(path: Path) -> dict[str, object]:
    if not path.exists():
        return {}
    try:
        with path.open("r", encoding="utf-8-sig") as handle:
            payload = json.load(handle)
    except json.JSONDecodeError as exc:
        fail(f"existing user-secrets file is not valid JSON: {path}: {exc}")
    if not isinstance(payload, dict):
        fail(f"existing user-secrets file root is not an object: {path}")
    return payload


def remove_flat_key(payload: dict[str, object], key: str) -> None:
    payload.pop(key, None)


def set_nested(payload: dict[str, object], key: str, value: str) -> None:
    parts = key.split(":")
    current = payload
    for part in parts[:-1]:
        existing = current.get(part)
        if not isinstance(existing, dict):
            existing = {}
            current[part] = existing
        current = existing
    current[parts[-1]] = value


def write_json_private(path: Path, payload: dict[str, object]) -> None:
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    try:
        os.chmod(path.parent, stat.S_IRWXU)
    except PermissionError:
        pass

    fd, tmp_name = tempfile.mkstemp(prefix=".secrets.", suffix=".json", dir=path.parent)
    tmp_path = Path(tmp_name)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=2, sort_keys=True)
            handle.write("\n")
        os.chmod(tmp_path, stat.S_IRUSR | stat.S_IWUSR)
        os.replace(tmp_path, path)
    finally:
        if tmp_path.exists():
            tmp_path.unlink()


def prompt_secret(label: str) -> str:
    value = getpass.getpass(f"{label}: ").strip()
    if not value:
        fail(f"{label} cannot be empty")
    return value


def main() -> int:
    args = parse_args()
    project_file = resolve_project(args.project)
    secrets_id = user_secrets_id(project_file)
    secrets_file = user_secrets_path(secrets_id)

    if args.dry_run:
        print("Resolved X OAuth user-secrets target.")
        print(f"Target project: {project_file}")
        print(f"Target user-secrets file: {secrets_file}")
        print("No prompts were shown and no secrets were written because --dry-run was used.")
        return 0

    print("Paste X OAuth 2.0 credentials from the app's Keys and Tokens page.")
    print("Input is hidden and values will not be echoed.")
    client_id = prompt_secret("X OAuth 2.0 Client ID")
    client_secret = prompt_secret("X OAuth 2.0 Client Secret")

    payload = load_existing_secrets(secrets_file)
    remove_flat_key(payload, CLIENT_ID_KEY)
    remove_flat_key(payload, CLIENT_SECRET_KEY)
    set_nested(payload, CLIENT_ID_KEY, client_id)
    set_nested(payload, CLIENT_SECRET_KEY, client_secret)
    write_json_private(secrets_file, payload)

    print("Imported X OAuth credentials into .NET user secrets.")
    print(f"Target project: {project_file}")
    print(f"Target user-secrets file: {secrets_file}")
    print("Updated keys:")
    print(f"- {CLIENT_ID_KEY}")
    print(f"- {CLIENT_SECRET_KEY}")
    print("Restart Aspire or the API before testing X login.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
