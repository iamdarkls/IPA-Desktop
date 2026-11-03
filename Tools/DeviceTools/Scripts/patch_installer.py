"""Reviewed read-only placeholder browse option for pinned ideviceinstaller 1.2.0."""
from pathlib import Path
import hashlib
import io
import sys
import tarfile

root=Path(sys.argv[1]);file=root/'src/ideviceinstaller.c';source=file.read_text()
if hashlib.sha256(source.encode()).hexdigest() != '856796be11f39cbd433906c0c5aff825d349d40bf63541ed4181b09b4fe99760': raise RuntimeError('Unreviewed ideviceinstaller source')
def replace(before,after):
    global source
    if source.count(before)!=1: raise RuntimeError('Source changed: '+before)
    source=source.replace(before,after,1)
replace('int opt_list_user = 0;', 'int opt_list_user = 0;\nint opt_show_placeholders = 0;')
replace('\tLIST_ALL,','\tLIST_ALL,\n\tLIST_PLACEHOLDERS,')
replace('\t\t{ "all", no_argument, NULL, LIST_ALL },','\t\t{ "all", no_argument, NULL, LIST_ALL },\n\t\t{ "placeholders", no_argument, NULL, LIST_PLACEHOLDERS },')
replace('\t\tcase LIST_USER:', '\t\tcase LIST_PLACEHOLDERS:\n\t\t\topt_show_placeholders = 1;\n\t\t\tbreak;\n\t\tcase LIST_USER:')
replace('\t\tplist_t apps = NULL;','\t\tplist_t apps = NULL;\n\t\tif (opt_show_placeholders) plist_dict_set_item(client_opts, "ShowPlaceholders", plist_new_bool(1));')
replace('\t"        --all           List all types of apps\\n"\n\t"        --xml           Print output as XML Property List\\n"', '\t"        --all           List all types of apps\\n"\n\t"        --placeholders  Include placeholder/offloaded app records\\n"\n\t"        --xml           Print output as XML Property List\\n"')
file.write_text(source,newline='\n')
# The source archive contains the actual patched code used for compilation.
if len(sys.argv)>2:
    archive=Path(sys.argv[2]);temporary=archive.with_suffix('.patched.tar')
    with tarfile.open(archive) as old,tarfile.open(temporary,'w') as new:
        for member in old.getmembers():
            if member.name=='ideviceinstaller/src/ideviceinstaller.c':
                content=source.encode();member.size=len(content);new.addfile(member,io.BytesIO(content))
            else: new.addfile(member,old.extractfile(member) if member.isfile() else None)
    temporary.replace(archive)
print('PASS: ideviceinstaller adds explicit ShowPlaceholders without modifying installation or pairing')
