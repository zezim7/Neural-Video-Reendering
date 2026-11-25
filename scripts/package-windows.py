"""Package the editor and its Windows video and neural processing tools."""

import hashlib
import shutil
import tempfile
from pathlib import Path
from urllib.request import urlopen
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parents[1]
FFMPEG = (
    "https://github.com/GyanD/codexffmpeg/releases/download/9.1/ffmpeg-9.1-essentials_build.zip",
    119467219,
    "b9bbbb3acece000cbeec994f856649354e52c773807841e78908374b24d658fd",
)
NEURAL = (
    "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip",
    45474481,
    "abc02804e17982a3be33675e4d471e91ea374e65b70167abc09e31acb412802d",
)


def unpack(work, name, asset):
    url, size, sha256 = asset
    path = work / f"{name}.zip"
    with urlopen(url, timeout=90) as source, path.open("wb") as target:
        shutil.copyfileobj(source, target, length=1024 * 1024)
    with path.open("rb") as package:
        digest = hashlib.file_digest(package, "sha256").hexdigest()
    print(f"{name} SHA-256: {digest}", flush=True)
    if path.stat().st_size != size or digest != sha256:
        raise ValueError(f"Falha na verificação de {name}.")
    destination = work / name
    with ZipFile(path) as package:
        package.extractall(destination)
    return destination


def main():
    editor = ROOT / "build"
    if not (editor / "VideoNR.exe").is_file():
        raise ValueError("Compile o editor com Build.ps1 -SelfContained.")
    output = ROOT / "dist"
    output.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output) as temp:
        work = Path(temp)
        ffmpeg = unpack(work, "ffmpeg", FFMPEG)
        ffmpeg_bin = next(ffmpeg.rglob("ffmpeg.exe")).parent
        real = unpack(work, "realesrgan", NEURAL)
        real_root = next(real.rglob("realesrgan-ncnn-vulkan.exe")).parent
        for file in ("realesr-animevideov3-x2.param", "realesr-animevideov3-x2.bin",
                     "realesrgan-x4plus.param", "realesrgan-x4plus.bin"):
            if not (real_root / "models" / file).is_file():
                raise ValueError(f"Modelo ausente no pacote neural: {file}")
        motor = work / "motor"
        shutil.copytree(real_root, motor)
        for file in ("ffmpeg.exe", "ffprobe.exe"):
            shutil.copy2(ffmpeg_bin / file, motor / file)
        for pattern in ("[Ll][Ii][Cc][Ee][Nn][Ss][Ee]*", "README.txt"):
            for notice in ffmpeg_bin.parent.glob(pattern):
                if notice.is_file():
                    shutil.copy2(notice, motor / f"FFmpeg-{notice.name}")
        staged = work / "VideoNR-win64.zip"
        with ZipFile(staged, "w", compression=ZIP_DEFLATED, compresslevel=1) as package:
            for folder, prefix in ((editor, Path()), (motor, Path("motor"))):
                for file in sorted(folder.rglob("*")):
                    package.write(file, (prefix / file.relative_to(folder)).as_posix())
            for name in ("README.md", "DEPENDENCIAS.md", "LICENSE.txt"):
                package.write(ROOT / name, name)
        staged.replace(output / staged.name)


if __name__ == "__main__":
    main()
