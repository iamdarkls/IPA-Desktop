"""Describe the exact inputs compiled into a native bundle."""
import json


def write_manifest(config, destination, platform):
    pins = json.loads((config / 'pins.json').read_text())
    stable = json.loads((config / 'stable.json').read_text())
    components = {}
    for name, revision in pins.items():
        release = stable.get(name, {})
        version = release.get('tag', 'source')
        if release.get('sha') != revision:
            version += '+' + revision[:12]
        components[name] = {'version': version, 'revision': revision}
    value = {'schema': 1, 'version': (config / 'VERSION').read_text().strip(),
             'platform': platform, 'eventProtocol': 1, 'components': components}
    destination.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
