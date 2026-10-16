#!/usr/bin/env python3
"""Build a self-contained Windows release without accessing the target PC."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile


def digest(path):
    checksum = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(block)
    return checksum.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    destination = args.output.resolve()
    destination.mkdir(parents=True, exist_ok=False)
    package = destination / "GS66Control"
    for project, output in [("Desktop", package), ("Helper", package / "helper")]:
        subprocess.run([
            args.dotnet, "publish", str(root / f"src/Gs66Control.{project}/Gs66Control.{project}.csproj"),
            "-c", "Release", "-r", "win-x64", "--self-contained", "true",
            "-p:DebugType=None", "-p:DebugSymbols=false", "-o", str(output),
        ], cwd=root, check=True)
    for name in ["verify-release.ps1", "install-release.ps1"]:
        shutil.copyfile(root / "scripts" / name, package / name)
    shutil.copyfile(root / "docs" / "desktop.md", package / "使用说明.md")
    shutil.copyfile(root / "docs" / "personal-tuning.md", package / "personal-tuning.md")
    for name in ["Gs66Control.Desktop.exe", "helper/Gs66Control.Helper.exe"]:
        if not (package / name).is_file():
            raise RuntimeError(f"Missing published executable: {name}")
    manifest = {path.relative_to(package).as_posix(): digest(path)
                for path in sorted(package.rglob("*")) if path.is_file()}
    (package / "release-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    release = destination / "GS66Control-win-x64.zip"
    with zipfile.ZipFile(release, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(package.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(destination))

    source_paths = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=root
    ).decode().split("\0")
    source_paths = sorted(name for name in source_paths if name and
                          (name.startswith(("src/", "tests/", "scripts/", "docs/", "assets/", ".github/")) or
                           name in {"README.md", "AGENTS.md", ".gitignore", "Directory.Build.props"}) and
                          (root / name).is_file())
    source_manifest = {name: digest(root / name) for name in source_paths}
    source_zip = destination / "GS66Control-source.zip"
    with zipfile.ZipFile(source_zip, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in source_paths:
            archive.write(root / name, name)
        archive.writestr("source-manifest.json", json.dumps(source_manifest, indent=2))
    report = {
        "runtime": "win-x64", "selfContained": True,
        "windowsRuntimeValidated": False, "newHardwareControlsValidated": False,
        "artifacts": {path.name: digest(path) for path in [release, source_zip]},
        "publishedFileCount": len(manifest), "sourceFileCount": len(source_manifest),
    }
    (destination / "package-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
