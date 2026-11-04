#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Apply DebugAutoplay.RepairLog lift patches to baked level JSONs.
Input: a file with lines "L<level>: bolt <id>: +<lift>; bolt <id>: +<lift>; ..."
Adds <lift> to bolts[id].headLift in Assets/_Game/Data/Levels/Level_XX.json (creates .bak once).
"""
import json, re, sys, io, os

def main(log_path):
    patches = {}  # level -> {bolt: lift}
    with io.open(log_path, encoding='utf-8') as f:
        for line in f:
            m = re.match(r'\s*L(\d+):\s*(.*)', line)
            if not m:
                continue
            lvl = int(m.group(1))
            for bm in re.finditer(r'bolt\s+(\d+):\s*\+([0-9.,]+)', m.group(2)):
                patches.setdefault(lvl, {})[int(bm.group(1))] = float(bm.group(2).replace(',', '.'))
    base = os.path.join(os.path.dirname(__file__), '..', 'Assets', '_Game', 'Data', 'Levels')
    total = 0
    for lvl, bolts in sorted(patches.items()):
        path = os.path.join(base, 'Level_%02d.json' % lvl)
        bak = path + '.bak'
        if not os.path.exists(bak):
            io.open(bak, 'w', encoding='utf-8').write(io.open(path, encoding='utf-8').read())
        d = json.load(io.open(path, encoding='utf-8-sig'))
        n = 0
        for bid, lift in bolts.items():
            d['bolts'][bid]['headLift'] = round(d['bolts'][bid]['headLift'] + lift, 3)
            n += 1
        io.open(path, 'w', encoding='utf-8').write(json.dumps(d, indent=4, ensure_ascii=False))
        print('L%d: patched %d bolts' % (lvl, n))
        total += n
    print('total patched bolts:', total)

if __name__ == '__main__':
    main(sys.argv[1])
