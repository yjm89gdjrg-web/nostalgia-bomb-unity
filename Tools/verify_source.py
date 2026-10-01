#!/usr/bin/env python3
"""Dependency-free structural checks; NOT a C# compiler or Unity physics/test runner."""
from pathlib import Path
import json, math, re
from collections import deque

ROOT = Path(__file__).resolve().parents[1]

def check(condition, message):
    if not condition:
        raise AssertionError(message)
    print('PASS:', message)

for p in sorted(ROOT.rglob('*.json')) + sorted(ROOT.rglob('*.asmdef')):
    json.loads(p.read_text())
    print('PASS: JSON', p.relative_to(ROOT))

for p in sorted((ROOT / 'Assets').rglob('*.cs')):
    text = p.read_text()
    # Ignore strings and comments; validates delimiter balance, not types/API availability.
    clean = re.sub(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/', '', text, flags=re.S)
    stack = []
    pairs = {')': '(', ']': '[', '}': '{'}
    for char in clean:
        if char in '([{': stack.append(char)
        elif char in ')]}':
            check_ok = stack and stack.pop() == pairs[char]
            if not check_ok: raise AssertionError(f'{p}: unbalanced delimiters')
    check(not stack, f'C# delimiters {p.relative_to(ROOT)}')
    check('namespace NostalgiaBomb' in text, f'namespace {p.name}')

assets = ROOT / 'Assets'
guids = {}
for p in assets.rglob('*'):
    if p.suffix == '.meta': continue
    meta = Path(str(p) + '.meta')
    check(meta.exists(), f'meta present {p.relative_to(ROOT)}')
    guid = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(), re.M).group(1)
    check(guid not in guids, f'unique GUID {p.name}')
    guids[guid] = p
scene = (assets / 'Scenes/Prototype.unity').read_text()
script_guid = re.search(r'm_Script: .*guid: ([0-9a-f]{32})', scene).group(1)
check(guids[script_guid] == assets / 'Scripts/MatchGame.cs', 'saved scene points to MatchGame script')
scene_guid = re.search(r'^guid: (.+)$', (assets / 'Scenes/Prototype.unity.meta').read_text(), re.M).group(1)
check(scene_guid in (ROOT / 'ProjectSettings/EditorBuildSettings.asset').read_text(), 'saved scene enabled in build settings')
check('activeInputHandler: 0' in (ROOT / 'ProjectSettings/ProjectSettings.asset').read_text(), 'legacy input selected')
check('UnityEngine' not in (assets / 'Scripts/RoundRules.cs').read_text(), 'rules independent of Unity')
tests = (assets / 'Tests/EditMode/RoundRulesTests.cs').read_text()
check(tests.count('[Test]') == 14, '14 NUnit specifications present (NOT executed)')

# Independent top-down geometry sanity model; samples expanded footprints, not Unity physics.
arena = (assets / 'Scripts/Arena.cs').read_text()
pattern = r'Box\("[^"]+", new Vector3\(([^)]+)\), new Vector3\(([^)]+)\)'
boxes = []
for pos, size in re.findall(pattern, arena):
    x, y, z = [float(v.replace('f', '')) for v in pos.split(',')]
    sx, sy, sz = [float(v.replace('f', '')) for v in size.split(',')]
    if y <= 0: continue
    boxes.append((x-sx/2, x+sx/2, z-sz/2, z+sz/2))
check(len(boxes) == 9, 'nine solid wall/cover footprints extracted')
def free(p, radius):
    x, z = p
    for xmin, xmax, zmin, zmax in boxes:
        dx = max(xmin-x, 0, x-xmax)
        dz = max(zmin-z, 0, z-zmax)
        if dx*dx+dz*dz <= radius*radius: return False
    return True

def clear(a, b):
    steps = max(1, math.ceil(math.dist(a, b)/.08))
    return all(free((a[0]+(b[0]-a[0])*i/steps, a[1]+(b[1]-a[1])*i/steps), .45) for i in range(steps+1))

nodes = [(x,z) for z in range(-18,19,4) for x in range(-14,15,4) if free((x,z),.65)]
edges = [[j for j,b in enumerate(nodes) if i != j and math.dist(a,b)<=5.7 and clear(a,b)] for i,a in enumerate(nodes)]
seen = {0}; queue = deque([0])
while queue:
    for neighbor in edges[queue.popleft()]:
        if neighbor not in seen: seen.add(neighbor); queue.append(neighbor)
check(len(seen) == len(nodes), f'footprint waypoint model connected ({len(nodes)} nodes)')
for p in [(-3,-16),(0,-16),(3,-16),(-3,17),(0,17),(3,17),(-10,13),(10,13)]:
    check(any(clear(p,n) for n in nodes), f'spawn/site {p} can connect to waypoint graph')
check(not clear((-10,-10),(-10,10)), 'west dogleg blocks a naive direct route')
check(not clear((0,-10),(0,10)), 'central workshop blocks a naive direct route')
print('\nSTRUCTURAL CHECKS COMPLETE. Unity compilation, NUnit execution, physics, touch and iOS build NOT verified.')
