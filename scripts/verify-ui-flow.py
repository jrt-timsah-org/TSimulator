#!/usr/bin/env python3
"""Verify artifacts produced by the native builder/key-binding/recording UI smoke flow."""
import json, math, pathlib, sys
root = pathlib.Path(sys.argv[1])
scenario = json.loads((root / 'scenario.json').read_text())
robot = scenario['RobotOverrides']['0']
assert robot['MagazineCapacity'] == 60
assert math.isclose(robot['ShotInterval'], .08, abs_tol=1e-6)
assert math.isclose(robot['Width'], .55, abs_tol=1e-6)
assert math.isclose(robot['MaxSpeed'], 3.5, abs_tol=1e-6)
assert robot['Assembly']['Modules']['Shooter'] == 'shooter-rapid'
presets = list((root / 'robots').glob('*.json'))
assert len(presets) == 1, 'The flow should save exactly one local preset'
preset = json.loads(presets[0].read_text())
assert preset['Robot'] == robot
assert json.loads((root / 'settings.json').read_text())['Keys']['Fire'] == 'B'
recordings = list((root / 'replays').glob('*.jsonl'))
assert len(recordings) == 1
lines = [json.loads(line) for line in recordings[0].read_text().splitlines()]
assert lines[0]['Format'] == 2
assert lines[0]['Scenario']['RobotOverrides']['0']['MagazineCapacity'] == 60
assert any(c['Fire'] for frame in lines[1:-1] for c in frame['Commands'])
assert lines[-1]['Ticks'] > 0 and 'StateHash' in lines[-1]
if len(sys.argv) > 2:
    numeric = json.loads((pathlib.Path(sys.argv[2]) / 'scenario.json').read_text())
    modified = numeric['RobotOverrides']['0']
    assert modified['MagazineCapacity'] == 65
    assert math.isclose(modified['ShotInterval'], .08, abs_tol=1e-6)
    assert modified['Assembly'] is None, 'Numeric editing clears the recipe while preserving resolved stats'
    assert numeric['Robot']['MagazineCapacity'] == 30, 'Other robots retain the common spec'
print('UI FLOW OK: assemble, save preset, apply per robot, record firing, save scenario, rebind and save keys')
