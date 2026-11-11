"""Run: python3 QuadPane/tools/check-tab-overflow.py."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = ET.parse(Path(__file__).resolve().parents[1] / 'PaneView.axaml').getroot()
ns = {'a': 'https://github.com/avaloniaui'}
name = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
controls = {node.get(name): node for node in root.iter() if node.get(name)}
scroller = controls['TabScroller']
assert scroller.get('HorizontalScrollBarVisibility') == 'Hidden'
assert scroller.get('VerticalScrollBarVisibility') == 'Disabled'
assert controls['TabStrip'] in list(scroller)
assert controls['TabAdd'] not in list(scroller.iter()), 'Add tab must remain reachable during overflow'
parent = next(node for node in root.iter() if scroller in list(node))
assert parent.get('ColumnDefinitions') == 'Auto,*,Auto,Auto'
for key in ('TabScrollLeft', 'TabScrollRight', 'TabAdd'):
    assert controls[key] in list(parent)
    assert controls[key].get('AutomationProperties.Name')
    assert controls[key].get('Click')
print('Tab overflow layout checks passed')
