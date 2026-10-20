"""Prepare metadata only; publication follows standalone Windows verification."""
from pathlib import Path
import json
import os
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from device_tools.updates import discover, published, version

def main():
    folder=Path(__file__).resolve().parents[1]
    pins=json.loads((folder/'pins.json').read_text());stable=json.loads((folder/'stable.json').read_text())
    current=(folder/'VERSION').read_text().strip();major,minor,patch=version(current)
    new_pins,new_stable,changes=discover(pins,stable)
    candidate=f'{major}.{minor}.{patch+1}' if changes else current
    exists=published('device-tools-v'+candidate)
    if changes and exists: raise ValueError('Candidate version already published; reconcile VERSION before retrying')
    build=bool(changes) or not exists
    # No files are written until all upstream checks complete successfully.
    if changes:
        (folder/'pins.json').write_text(json.dumps(new_pins,indent=2)+'\n')
        (folder/'stable.json').write_text(json.dumps(new_stable,indent=2)+'\n')
        (folder/'VERSION').write_text(candidate+'\n')
        notes=[f'## IPA Desktop Device Tools {candidate}','',
               'Автоматическое обновление стабильных исходников:','']
        notes += [f'- **{name}**: {old} → [{new}](https://github.com/libimobiledevice/{name}/releases/tag/{new}), ревизия `{sha[:12]}`.' for name,old,new,sha in changes]
        notes += ['', 'Windows x64-комплект публикуется после сборки, проверки DLL и совместимости команд. Предыдущие релизы сохраняются для отката. Лицензии и исходники приложены.','']
        (folder/'changelog.md').write_text('\n'.join(notes))
    output=os.environ.get('GITHUB_OUTPUT')
    if output:
        with open(output,'a') as file:file.write(f'build={str(build).lower()}\nupdate={str(bool(changes)).lower()}\n')
    print(f'Candidate {candidate}; changed projects: {len(changes)}; build: {build}')
    if not build: print('No newer stable release. Current published Windows package is retained.')

if __name__=='__main__':main()
