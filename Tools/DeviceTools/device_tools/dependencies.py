"""Indexed, testable dependency closure. No native tools run at import time."""
from collections import deque
from pathlib import Path
import hashlib


def file_index(folder):
    result = {}
    for path in Path(folder).iterdir():
        if path.is_file():
            key = path.name.casefold()
            if key in result:
                raise RuntimeError(f'Ambiguous Windows filename: {path.name}')
            result[key] = path
    return result


class DependencyCollector:
    def __init__(self, prefix_bin, mingw_bin, system_bin, inspect):
        self.prefix = file_index(prefix_bin)
        self.mingw = file_index(mingw_bin)
        self.system = file_index(system_bin)
        self.inspect = inspect

    def collect(self, roots):
        pending = deque(map(Path, roots))
        scheduled = {file.name.casefold() for file in pending}
        seen = set()
        output = []
        while pending:
            file = pending.popleft()
            key = file.name.casefold()
            if key in seen:
                continue
            if not file.is_file():
                raise RuntimeError(f'Missing binary: {file}')
            if key.startswith('msys-'):
                raise RuntimeError(f'MSYS runtime cannot be shipped as a native Windows dependency: {file.name}')
            seen.add(key)
            output.append(file)
            for dll in self.inspect(file):
                name = dll.casefold()
                if name in seen:
                    continue
                found = self.prefix.get(name) or self.mingw.get(name)
                if found is not None:
                    if name not in scheduled:
                        scheduled.add(name)
                        pending.append(found)
                elif name.startswith(('api-ms-win-', 'ext-ms-win-')) or name in self.system:
                    continue
                else:
                    raise RuntimeError(f'Unresolved dependency {dll} of {file.name}')
        return output


def sha256_file(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()
