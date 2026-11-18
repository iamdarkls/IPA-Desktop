"""Exercise the real macOS update helper with an installed release and a candidate ZIP."""

import argparse
import json
import os
from pathlib import Path
import plistlib
import shutil
import signal
import subprocess
import sys
import tempfile
import time

from verify import extract_package, run_gui_smoke, verify_gui, verify_macos_bundle, verify_native_tools


def binaries(bundle):
    return bundle / 'Contents' / 'MacOS'


def version(bundle):
    with (bundle / 'Contents' / 'Info.plist').open('rb') as info:
        return plistlib.load(info)['CFBundleShortVersionString']


def running_pid(bundle):
    executable = str(binaries(bundle) / 'IPA-Desktop')
    listing = subprocess.check_output(['/bin/ps', '-axo', 'pid=,command='], text=True, timeout=5)
    for line in listing.splitlines():
        pid, command = line.split(maxsplit=1)
        if command == executable or command.startswith(executable + ' '):
            return int(pid)
    return None


def wait_for_launch(bundle):
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        if pid := running_pid(bundle):
            # Give a process rejected by the single-instance mutex time to exit.
            time.sleep(1)
            if running_pid(bundle) == pid:
                return pid
        time.sleep(0.2)
    raise RuntimeError(f'LaunchServices did not start {bundle}.')


def stop(bundle):
    if pid := running_pid(bundle):
        os.kill(pid, signal.SIGTERM)
        deadline = time.monotonic() + 10
        while running_pid(bundle) == pid:
            if time.monotonic() > deadline:
                raise RuntimeError(f'Could not stop {bundle}.')
            time.sleep(0.2)


def replace_bundle(helper_bundle, candidate, installed, workspace, old_pid, fail_swap=False):
    helper = workspace / 'Apply.sh'
    shutil.copy2(binaries(helper_bundle) / 'Scripts' / 'Update-App.sh', helper)
    env = os.environ.copy()
    if fail_swap:
        # Fail exactly the move of the staged bundle, after the previous bundle has been backed up.
        shim = workspace / 'mv'
        shim.write_text("""#!/bin/sh
case "$1" in *.app.update-*) exit 1;; esac
exec /bin/mv "$@"
""")
        shim.chmod(0o755)
        env['PATH'] = str(workspace) + os.pathsep + env['PATH']

    with subprocess.Popen(['/bin/sh', helper, candidate, installed, str(old_pid)], env=env) as update:
        try:
            time.sleep(1.2)
            if update.poll() is not None:
                raise RuntimeError('Updater exited before the running app closed.')
            stop(installed)
            status = update.wait(timeout=80)
        finally:
            if update.poll() is None:
                update.kill()
    if status != (1 if fail_swap else 0):
        raise RuntimeError(f'Unexpected updater exit status: {status}.')
    restarted_pid = wait_for_launch(installed)
    if restarted_pid == old_pid:
        raise RuntimeError('The previous app process was not replaced.')
    verify_macos_bundle(installed)
    return restarted_pid


def read_saved_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def verify_update(previous, candidate):
    with tempfile.TemporaryDirectory(prefix='ipa-mac-update-') as folder:
        workspace = Path(folder).resolve()
        installed = extract_package(previous, workspace / 'Installed Apps', macos=True)
        staged = extract_package(candidate, workspace / 'Stage', macos=True)
        before, after = version(installed), version(staged)
        if before == after:
            raise RuntimeError('Update test requires two different app versions.')

        verify_gui(binaries(installed), workspace)
        data = workspace / 'data'
        persisted = {name: read_saved_json(data / name) for name in ('state.json', 'AppSets.json')}

        try:
            subprocess.run(['/usr/bin/open', '-n', installed], check=True, timeout=15)
            previous_pid = wait_for_launch(installed)
            restored_pid = replace_bundle(staged, staged, installed, workspace, previous_pid, fail_swap=True)
            if version(installed) != before or list(installed.parent.glob('*.app.update-*')):
                raise RuntimeError('Failed replacement did not restore the installed bundle cleanly.')
            replace_bundle(installed, staged, installed, workspace, restored_pid)
            backups = list(installed.parent.glob('IPA-Desktop.app.backup-*'))
            if version(installed) != after or len(backups) != 1 or version(backups[0]) != before:
                raise RuntimeError('Update did not install the candidate and retain the previous bundle.')
            stop(installed)

            verify_native_tools(binaries(installed) / 'native')
            run_gui_smoke(binaries(installed), data)
            for name, expected in persisted.items():
                if read_saved_json(data / name) != expected:
                    raise RuntimeError(f'User data changed across the update: {name}.')
        finally:
            stop(installed)
        print(f'PASS: macOS {before} → {after}, real bundle restart, icon, settings and replacement rollback')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('previous', type=Path)
    parser.add_argument('candidate', type=Path)
    args = parser.parse_args()
    if sys.platform != 'darwin':
        parser.error('This test uses macOS LaunchServices.')
    verify_update(args.previous.resolve(), args.candidate.resolve())
