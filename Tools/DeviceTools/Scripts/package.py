"""MSYS2 entry point; native process execution stays at the boundary."""
from pathlib import Path
import os
import re
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from device_tools.packaging import PackageBuilder


def inspect_dependencies(file):
    data = subprocess.check_output(['objdump', '-p', str(file)], text=True)
    return re.findall(r'DLL Name:\s*(\S+)', data)


def main():
    root = Path(__file__).resolve().parents[3]
    mingw = Path(subprocess.check_output(['cygpath', '-w', '/mingw64'], text=True).strip()) / 'bin'
    system = Path(os.environ['SYSTEMROOT']) / 'System32'
    count = PackageBuilder(root, mingw, system, inspect_dependencies).create()
    print(f'Packaged {count} Windows binaries and license notices')


if __name__ == '__main__':
    main()
