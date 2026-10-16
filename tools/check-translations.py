"""Lista las claves L.T/L.F del proyecto y comprueba cuáles faltan en Strings.cs.

Uso: python keys.py <carpeta del proyecto> [--missing]
"""
import glob
import os
import re
import sys

root = sys.argv[1]
call = re.compile(r'L\.[TF]\(\s*"((?:[^"\\]|\\.)*)"')
entry = re.compile(r'^\s*\["((?:[^"\\]|\\.)*)"\]\s*=', re.M)

keys = []
for f in sorted(glob.glob(os.path.join(root, '*.cs'))):
    if os.path.basename(f) == 'Strings.cs':
        continue
    for m in call.finditer(open(f, encoding='utf-8').read()):
        if m.group(1) not in keys:
            keys.append(m.group(1))

strings = os.path.join(root, 'Strings.cs')
have = set(entry.findall(open(strings, encoding='utf-8').read())) if os.path.exists(strings) else set()

if '--missing' in sys.argv:
    missing = [k for k in keys if k not in have]
    unused = sorted(have - set(keys))
    print(f'claves: {len(keys)}  traducidas: {len(have)}  faltan: {len(missing)}  sobran: {len(unused)}')
    for k in missing:
        print('FALTA ', k)
    for k in unused:
        print('SOBRA ', k)
else:
    print(len(keys))
    for k in keys:
        print(k)
