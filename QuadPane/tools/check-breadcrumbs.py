"""Run: python3 QuadPane/tools/check-breadcrumbs.py (UI wiring regression check)."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
view = ET.parse(root / 'PaneView.axaml').getroot()
name = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
controls = {node.get(name): node for node in view.iter() if node.get(name)}
bar = controls['BreadcrumbBar']
assert controls['Breadcrumbs'] in list(bar.iter())
scroller = next(node for node in bar.iter() if node.tag.endswith('}ScrollViewer'))
assert scroller.get('HorizontalScrollBarVisibility') == 'Auto', 'Deep paths must remain reachable'
edit = next(node for node in bar.iter() if node.get('Click') == 'EditAddress_Click')
assert edit.get('AutomationProperties.Name'), 'Editing must have an accessible name'
assert edit not in list(scroller.iter()), 'Editing must remain reachable on long paths'
address = controls['AddressBox']
for event in ('KeyDown', 'GotFocus', 'LostFocus'):
    assert address.get(event), f'Address editing needs {event}'
code = (root / 'PaneView.axaml.cs').read_text()
assert 'AutomationProperties.SetName(button, target)' in code
assert 'if (Host.Navigate(target)) FocusView();' in code
assert 'folder = folder.Parent' in code, 'Use filesystem parents to preserve drive and UNC roots'
print('Breadcrumb UI wiring checks passed')
