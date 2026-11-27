"""Build offline Windows ZIPs from the published editor and official Veyra packages."""

import argparse
import hashlib
import json
import shutil
import tempfile
from pathlib import Path
from urllib.request import urlopen
from zipfile import ZIP_DEFLATED, ZipFile


VERSION = "2.2.0"
PACKAGES = {
    "NVIDIA": (750110975, "fd1235483a6e36b2520966ec161104478cd93186e97af3b0d78d2dfa7b129932"),
    "AMD": (748639635, "b383cd4fbc94a6b84e32f36bc8c2758b4c03216c2bd0a2560e3112ba520d6905"),
}
DOWNLOAD_BASE = f"https://github.com/Likely7/Veyra-NRVideo/releases/download/v{VERSION}"
ROOT = Path(__file__).resolve().parents[1]


def find_engine_root(extracted, vendor):
    for root in (extracted, *(path for path in extracted.iterdir() if path.is_dir())):
        for folder in (root, root / "versions" / VERSION):
            manifest = folder / "package-manifest.json"
            if not (folder / "veyra_qml_ui.exe").is_file() or not manifest.is_file():
                continue
            package = json.loads(manifest.read_text(encoding="utf-8-sig"))
            if package.get("version") == VERSION and str(package.get("gpuPackage")).upper() == vendor:
                return root
    raise ValueError(f"Pacote {vendor} não contém o motor Veyra {VERSION} esperado.")


def make_package(vendor, editor, output):
    size, digest = PACKAGES[vendor]
    name = f"Veyra-{VERSION}-{vendor}-win64-portable.zip"
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f"VideoNR-{vendor}-win64.zip"
    with tempfile.TemporaryDirectory(dir=output) as scratch:
        workspace = Path(scratch)
        archive = workspace / name
        print(f"Baixando {name}", flush=True)
        with urlopen(f"{DOWNLOAD_BASE}/{name}", timeout=90) as response, archive.open("wb") as target:
            shutil.copyfileobj(response, target, length=1024 * 1024)
        with archive.open("rb") as source:
            if archive.stat().st_size != size or hashlib.file_digest(source, "sha256").hexdigest() != digest:
                raise ValueError(f"Falha na verificação do pacote {name}.")

        extracted = workspace / "veyra"
        with ZipFile(archive) as package:
            package.extractall(extracted)
        engine = find_engine_root(extracted, vendor)
        archive.unlink()

        print(f"Montando {destination.name}", flush=True)
        staged = workspace / destination.name
        with ZipFile(staged, "w", compression=ZIP_DEFLATED, compresslevel=1) as package:
            for folder, prefix in ((editor, Path()), (engine, Path("motor"))):
                for file in sorted(folder.rglob("*")):
                    package.write(file, (prefix / file.relative_to(folder)).as_posix())
            for name in ("LICENSE.txt", "README.md", "README.pt-BR.md", "README.zh-CN.md", "DEPENDENCIAS.md"):
                package.write(ROOT / name, name)
            for image in sorted((ROOT / "docs/screenshots").glob("*.png")):
                package.write(image, image.relative_to(ROOT).as_posix())
        staged.replace(destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--editor", type=Path, default=ROOT / "build")
    parser.add_argument("--output", type=Path, default=ROOT / "dist")
    args = parser.parse_args()
    if not (args.editor / "VideoNR.exe").is_file() or not (args.editor / "Engine.exe").is_file():
        parser.error("Compile o editor e o adaptador com Build.ps1 -SelfContained antes de empacotar.")
    for vendor in PACKAGES:
        make_package(vendor, args.editor, args.output)


if __name__ == "__main__":
    main()
