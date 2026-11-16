"""Run: python3 QuadPane/tools/check-filter-toggle.py."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
view = ET.parse(root / 'PaneView.axaml').getroot()
name = '{http://schemas.microsoft.com/winfx/2006/xaml}Name'
controls = {node.get(name): node for node in view.iter() if node.get(name)}
assert controls['FilterBox'].get('IsVisible') == 'False', 'Filter should not occupy space initially'
button = controls['FilterButton']
assert button.get('Click') == 'FilterButton_Click'
assert button.get('AutomationProperties.Name')
assert button.get('DockPanel.Dock') == 'Right', 'Search must remain reachable beside long paths'
code = (root / 'PaneView.axaml.cs').read_text()
focus = code.split('public void FocusFilter()', 1)[1].split('private void', 1)[0]
assert focus.index('FilterBox.IsVisible = true') < focus.index('FilterBox.Focus()')
close = code.split('private void HideFilter()', 1)[1].split('private void', 1)[0]
assert '_filterTimer.Stop()' in close and 'ApplyFilterText()' in close
assert 'FilterBox.Text = ""' in close and 'FilterBox.IsVisible = false' in close
print('Filter toggle checks passed')
