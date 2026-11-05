"""Check GitHub release layout expected by QuadPane updater."""
import re
import urllib.request


def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "QuadPane-updater"})).read()


with urllib.request.urlopen(urllib.request.Request(
        "https://github.com/lampt24/quadpane/releases/latest", headers={"User-Agent": "QuadPane-updater"})) as latest:
    tag = latest.geturl().rsplit("/", 1)[-1]
assert re.fullmatch(r"v\d+\.\d+\.\d+", tag), tag
base = f"https://github.com/lampt24/quadpane/releases/download/{tag}/"
sums = get(base + "SHA256SUMS.txt").decode()
for rid in ("win-x64", "win-x86", "win-arm64"):
    name = f"QuadPane-{tag}-{rid}.zip"
    assert re.search(rf"^[0-9a-f]{{64}}  {re.escape(name)}$", sums, re.MULTILINE), name
installer = f"QuadPane-{tag}-win-x64-setup.exe"
assert re.search(rf"^[0-9a-f]{{64}}  {re.escape(installer)}$", sums, re.MULTILINE), installer
print(f"Release {tag}: đủ 3 gói ZIP, bộ cài và SHA256SUMS.txt")
