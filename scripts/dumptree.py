"""Query helper for NIVR scene dumps (C:\\VRMod\\logs\\dumps\\*.txt).

  python dumptree.py <dump> tree [maxdepth] [path]   object names only, down to maxdepth below path
  python dumptree.py <dump> sub <path> [maxdepth]    full subtree (objects + components) below path
  python dumptree.py <dump> stats [depth]            renderer/UI counts per subtree at the given depth
  python dumptree.py <dump> grep <regex>             matching component lines with their object path

<path> is slash separated object names starting at a scene root, e.g. "Canvases/HUD_Camera".
"""
import re
import sys

OBJ = re.compile(r'^(\s*)([+-]) (.*?) \((layer=.*)\)$')


def parse(path):
    """-> list of (depth, active, name, info, [component lines], fullpath)"""
    nodes, stack, in_scene = [], [], False
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.rstrip('\n')
            if line.startswith('## SCENE'):
                in_scene, stack = True, []
                nodes.append((-1, True, line, '', [], line))
                continue
            if not in_scene:
                continue
            m = OBJ.match(line)
            if m:
                depth = len(m.group(1)) // 2
                stack = stack[:depth] + [m.group(3)]
                nodes.append((depth, m.group(2) == '+', m.group(3), m.group(4), [], '/'.join(stack)))
            elif line.strip().startswith('* ') and nodes:
                nodes[-1][4].append(line.strip()[2:])
    return nodes


def under(nodes, path):
    if not path:
        return nodes, 0
    out, base = [], None
    for n in nodes:
        if base is None:
            if n[5] == path:
                base = n[0]
                out.append(n)
        elif n[0] > base:
            out.append(n)
        else:
            break
    return out, (base or 0)


def short(c, width=200):
    return c if len(c) <= width else c[:width] + '...'


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    dump, cmd = sys.argv[1], sys.argv[2]
    nodes = parse(dump)
    if cmd == 'tree':
        maxdepth = int(sys.argv[3]) if len(sys.argv) > 3 else 2
        sel, base = under(nodes, sys.argv[4] if len(sys.argv) > 4 else '')
        for d, act, name, info, comps, full in sel:
            if d == -1:
                print(name)
            elif d - base <= maxdepth:
                types = ','.join(c.split(' ')[0].split('.')[-1] for c in comps)
                print('  ' * (d - base) + ('+ ' if act else '- ') + name + (f'  [{types}]' if types else ''))
    elif cmd == 'sub':
        maxdepth = int(sys.argv[4]) if len(sys.argv) > 4 else 99
        sel, base = under(nodes, sys.argv[3])
        for d, act, name, info, comps, full in sel:
            if d - base > maxdepth:
                continue
            ind = '  ' * (d - base)
            print(f"{ind}{'+' if act else '-'} {name} ({short(info, 140)})")
            for c in comps:
                print(f'{ind}    * {short(c, 420)}')
    elif cmd == 'stats':
        depth = int(sys.argv[3]) if len(sys.argv) > 3 else 1
        keys = ['MeshRenderer', 'SkinnedMeshRenderer', 'SpriteRenderer', 'UI.Image', 'RawImage', 'VideoPlayer', 'Canvas ', 'ParticleSystem', 'Light ']
        rows, cur = [], None
        for d, act, name, info, comps, full in nodes:
            if d == -1:
                continue
            if d <= depth:
                cur = [full, act, {k: 0 for k in keys}, 0]
                rows.append(cur)
            cur[3] += 1
            for c in comps:
                for k in keys:
                    if ('UnityEngine.' + k) in c + ' ' or ('.' + k) in c + ' ':
                        cur[2][k] += 1
                        break
        for full, act, counts, n in rows:
            nz = ' '.join(f'{k.strip()}={v}' for k, v in counts.items() if v)
            print(f"{'+' if act else '-'} {full}  objs={n}  {nz}")
    elif cmd == 'grep':
        rx = re.compile(sys.argv[3])
        for d, act, name, info, comps, full in nodes:
            for c in comps:
                if rx.search(c):
                    print(f"{'+' if act else '-'} {full}\n      * {short(c, 420)}")
    else:
        print(__doc__)


if __name__ == '__main__':
    main()
