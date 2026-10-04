#!/usr/bin/env python3
"""Refresh the static UI glyph atlas input; module/preset labels are added at runtime."""
from pathlib import Path
root = Path(__file__).resolve().parent.parent
characters = {chr(i) for i in range(32, 127)}
for source in (root / 'src' / 'TSimulator.Desktop').glob('*.cs'):
    characters.update(c for c in source.read_text(encoding='utf-8') if ord(c) > 127 and c.isprintable())
(root / 'assets' / 'fonts' / 'ui-glyphs.txt').write_text(''.join(sorted(characters)) + '\n', encoding='utf-8')
print(f'Updated {len(characters)} UI glyphs')
