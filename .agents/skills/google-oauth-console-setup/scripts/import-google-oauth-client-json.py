#!/usr/bin/env python3
"""Import a Google Web OAuth client JSON into ASP.NET Core user secrets."""

from __future__ import annotations

import argparse
import json
import os
import stat
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


CLIENT_ID_KEY = "Authentication:Google:ClientId"
CLIENT_SECRET_KEY = "Authentication:Google:ClientSecret"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Import a downloaded Google OAuth web client JSON into .NET user secrets."
    )
    parser.add_argument("client_json", help="Path to the downloaded Google client_secret JSON file.")
    parser.add_argument(
        "--project",
        default="api/Intervals.Api",
        help="API project directory or .csproj path. Default: api/Intervals.Api",
    )
    parser.add_argument(
        "--expected-redirect-uri",
        action="append",
        default=[],
        help="Redirect URI that must be present in the Google client JSON. Can be repeated.",
    )
    parser.add_argument(
        "--allow-secret-file-in-repo",
        action="store_true",
        help="Allow importing from a client_secret JSON file located inside the repository.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Validate the Google client JSON and target user-secrets file without writing.",
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


def find_repo_root(start: Path) -> Path | None:
    current = start.resolve()
    if current.is_file():
        current = current.parent
    for candidate in (current, *current.parents):
        if (candidate / ".git").exists():
            return candidate
    return None


def is_relative_to(path: Path, root: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


def load_google_web_client(client_json: Path) -> dict[str, object]:
    if not client_json.is_file():
        fail(f"client JSON file does not exist: {client_json}")

    try:
        with client_json.open("r", encoding="utf-8-sig") as handle:
            payload = json.load(handle)
    except json.JSONDecodeError as exc:
        fail(f"client JSON is not valid JSON: {exc}")

    if not isinstance(payload, dict):
        fail("client JSON root must be an object")
    if "web" not in payload:
        if "installed" in payload:
            fail("this is an installed/desktop OAuth client; create a Web application client instead")
        fail("client JSON does not contain a 'web' OAuth client")

    web = payload["web"]
    if not isinstance(web, dict):
        fail("client JSON 'web' value must be an object")
    return web


def require_string(web: dict[str, object], key: str) -> str:
    value = web.get(key)
    if not isinstance(value, str) or not value.strip():
        fail(f"client JSON is missing web.{key}")
    return value.strip()


def redirect_uris(web: dict[str, object]) -> list[str]:
    value = web.get("redirect_uris", [])
    if value is None:
        return []
    if not isinstance(value, list) or not all(isinstance(item, str) for item in value):
        fail("client JSON web.redirect_uris must be a string array")
    return value


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


def main() -> int:
    args = parse_args()
    client_json = Path(args.client_json).expanduser().resolve()
    project_file = resolve_project(args.project)
    repo_root = find_repo_root(Path.cwd())

    if (
        repo_root is not None
        and is_relative_to(client_json, repo_root)
        and not args.allow_secret_file_in_repo
    ):
        fail(
            "refusing to import a secret JSON file from inside the repository; "
            "move it outside the repo or pass --allow-secret-file-in-repo"
        )

    web = load_google_web_client(client_json)
    client_id = require_string(web, "client_id")
    client_secret = require_string(web, "client_secret")
    actual_redirect_uris = redirect_uris(web)

    for expected in args.expected_redirect_uri:
        if expected not in actual_redirect_uris:
            fail(
                f"expected redirect URI not found in Google client JSON: {expected}\n"
                f"configured redirect URIs: {', '.join(actual_redirect_uris) or '(none)'}"
            )

    secrets_id = user_secrets_id(project_file)
    secrets_file = user_secrets_path(secrets_id)

    if args.dry_run:
        print("Validated Google OAuth web client JSON.")
        print(f"Target project: {project_file}")
        print(f"Target user-secrets file: {secrets_file}")
        print("No secrets were written because --dry-run was used.")
        return 0

    payload = load_existing_secrets(secrets_file)
    remove_flat_key(payload, CLIENT_ID_KEY)
    remove_flat_key(payload, CLIENT_SECRET_KEY)
    set_nested(payload, CLIENT_ID_KEY, client_id)
    set_nested(payload, CLIENT_SECRET_KEY, client_secret)
    write_json_private(secrets_file, payload)

    print("Imported Google OAuth web client into .NET user secrets.")
    print(f"Target project: {project_file}")
    print(f"Target user-secrets file: {secrets_file}")
    print("Updated keys:")
    print(f"- {CLIENT_ID_KEY}")
    print(f"- {CLIENT_SECRET_KEY}")
    print("Restart Aspire or the API before testing Google login.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
