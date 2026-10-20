"""Prepare a candidate only. Publication persists metadata after Windows tests."""
from pathlib import Path
import json, os, re, urllib.request, urllib.error, urllib.parse

PROJECTS=('libplist','libimobiledevice-glue','libusbmuxd','libtatsu','libimobiledevice','ideviceinstaller')
REPO='iamdarkls/IPA-Desktop'

def version(tag):
    match=re.fullmatch(r'v?(\d+)\.(\d+)\.(\d+)',tag or '')
    if not match: raise ValueError(f'Not a stable numeric version: {tag!r}')
    return tuple(map(int,match.groups()))

def api(path):
    request=urllib.request.Request('https://api.github.com/'+path,headers={
        'Accept':'application/vnd.github+json','User-Agent':'IPA-Desktop-device-updates',
        'Authorization':'Bearer '+os.environ['GH_TOKEN']})
    with urllib.request.urlopen(request,timeout=30) as response: return json.load(response)

def resolve_tag(project,tag,fetch=api):
    obj=fetch(f'repos/libimobiledevice/{project}/git/ref/tags/{urllib.parse.quote(tag,safe="")}')['object']
    for _ in range(8):
        if obj['type']=='commit' and re.fullmatch(r'[0-9a-f]{40}',obj['sha']): return obj['sha']
        if obj['type']!='tag': raise ValueError('Tag does not point to a commit')
        obj=fetch(f'repos/libimobiledevice/{project}/git/tags/{obj["sha"]}')['object']
    raise ValueError('Too many annotated tag indirections')

def discover(pins,stable,fetch=api):
    new_pins=dict(pins); new_stable={n:dict(v) for n,v in stable.items()}; changes=[]
    if set(pins)!=set(PROJECTS) or set(stable)!=set(PROJECTS): raise ValueError('Incomplete source configuration')
    for project in PROJECTS:
        release=fetch(f'repos/libimobiledevice/{project}/releases/latest')
        if release.get('draft') or release.get('prerelease'): continue
        tag=release['tag_name']
        if version(tag)<=version(stable[project]['tag']): continue
        sha=resolve_tag(project,tag,fetch)
        comparison=fetch(f'repos/libimobiledevice/{project}/compare/{pins[project]}...{sha}')
        if comparison['status'] not in ('ahead','identical'):
            raise ValueError(f'{project}: stable {tag} does not contain our pinned revision; manual review needed')
        new_pins[project]=sha;new_stable[project]={'tag':tag,'sha':sha}
        changes.append((project,stable[project]['tag'],tag,sha))
    return new_pins,new_stable,changes

def published(tag,fetch=api):
    try:
        release=fetch(f'repos/{REPO}/releases/tags/{tag}')
        if release.get('draft') or release.get('prerelease'):return False
        current=tag.removeprefix('device-tools-v')
        expected={f'IPA-DeviceTools-{current}-Windows-x64.zip',f'IPA-DeviceTools-{current}-Sources.zip'}
        healthy={a['name'] for a in release.get('assets',[]) if a.get('size',0)>0 and re.fullmatch(r'sha256:[0-9a-fA-F]{64}',a.get('digest') or '')}
        return expected.issubset(healthy)
    except urllib.error.HTTPError as error:
        if error.code!=404: raise
        return False

