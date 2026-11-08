"""Build and package the exact guarded Windows Store engine on a Windows runner."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import zipfile

tools = Path(__file__).resolve().parent
pins = json.loads((tools / "pin.json").read_text())
artifacts = Path("artifacts").resolve()
root = artifacts / "guarded-windows"
source = root / "source"
stage = root / "package"
source.mkdir(parents=True)
(stage / "LICENSES").mkdir(parents=True)

def run(*args, **kwargs):
    return subprocess.check_output(args, text=True, **kwargs).strip()

run("git", "init", cwd=source)
run("git", "-c", "core.autocrlf=false", "fetch", "--depth", "1", pins["repository"], pins["revision"], cwd=source)
run("git", "-c", "core.autocrlf=false", "checkout", "--detach", "FETCH_HEAD", cwd=source)
if run("git", "rev-parse", "HEAD", cwd=source) != pins["revision"]:
    raise RuntimeError("Unexpected Store engine revision")
run("python", str(tools / "patch.py"), str(source))
exe = stage / "ipatool.exe"
build_env = {**os.environ, "GOOS": "windows", "GOARCH": "amd64", "CGO_ENABLED": "0"}
run("go", "build", "-mod=readonly", "-trimpath", "-ldflags",
    f"-X github.com/majd/ipatool/v2/cmd.version={pins['version']}",
    "-o", str(exe), ".", cwd=source, env=build_env)
run("go", "generate", "./pkg/...", cwd=source, env=build_env)
print(run("go", "test", "-mod=readonly", "./cmd", "./pkg/appstore",
          "-run", "^TestDesktop", cwd=source, env=build_env))
profile = run(str(exe), "account-safety-info", cwd=stage, timeout=15)
if profile != "IPA-DESKTOP-AUTH-SAFETY/1":
    raise RuntimeError("Incorrect account safety profile")
for command, expected in [
    ([], ("auth", "download", "--format", "list-purchases")),
    (["download", "--help"], ("--external-version-id", "--purchase", "--app-id")),
    (["list-purchases", "--help"], ("--all", "--range")),
    (["get-version-metadata", "--help"], ("--external-version-id", "--app-id")),
]:
    help_output = run(str(exe), *command, cwd=stage, timeout=15)
    if not all(flag in help_output for flag in expected):
        raise RuntimeError(f"Incompatible engine command: {command}")
(stage / "LICENSES" / "ipatool-MIT.txt").write_bytes((source / "LICENSE").read_bytes())
(stage / "sources.json").write_text(json.dumps({
    **pins, "profile": profile, "sha256": hashlib.sha256(exe.read_bytes()).hexdigest(),
}, indent=2) + "\n", encoding="utf-8")
version = pins["version"]
with zipfile.ZipFile(artifacts / f"IPA-AuthTools-{version}-Windows-x64.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    for file in sorted(stage.rglob("*")):
        if file.is_file():
            archive.write(file, file.relative_to(stage).as_posix())
with zipfile.ZipFile(artifacts / f"IPA-AuthTools-{version}-Sources.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    for folder, prefix in [(source, "ipatool"), (tools, "Windows")]:
        for file in sorted(folder.rglob("*")):
            relative = file.relative_to(folder)
            if file.is_file() and not {".git", "__pycache__"}.intersection(relative.parts):
                archive.write(file, f"{prefix}/{relative.as_posix()}")
print("PASS: Windows Store engine starts and matches the IPA Desktop CLI contract")
