"""Check GitHub release layout expected by QuadPane updater."""
import json
import re
import urllib.request


def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "QuadPane-updater"})).read()


release = json.loads(get("https://api.github.com/repos/lampt24/quadpane/releases/latest"))
tag = release["tag_name"]
assert re.fullmatch(r"v\d+\.\d+\.\d+", tag), tag
assets = {asset["name"]: asset["browser_download_url"] for asset in release["assets"]}
sums = get(assets["SHA256SUMS.txt"]).decode()
for rid in ("win-x64", "win-x86", "win-arm64"):
    name = f"QuadPane-{tag}-{rid}.zip"
    assert name in assets, name
    assert re.search(rf"^[0-9a-f]{{64}}  {re.escape(name)}$", sums, re.MULTILINE), name
print(f"Release {tag}: đủ 3 gói và SHA256SUMS.txt")
