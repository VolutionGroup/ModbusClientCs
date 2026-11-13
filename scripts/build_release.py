"""Build version-tagged .NET Framework client assets and a VVG.Modbus NuGet package."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile


def execute(command, cwd):
    subprocess.run([str(part) for part in command], cwd=cwd, check=True, timeout=180)


def git(repo, *args):
    return subprocess.check_output(["git", "-c", "safe.directory=" + repo.as_posix(), "-C", str(repo), *args], text=True).strip()


def assembly_version(path):
    escaped = str(path).replace("'", "''")
    return subprocess.check_output(["powershell", "-NoProfile", "-Command",
        "[Reflection.AssemblyName]::GetAssemblyName('" + escaped + "').Version.ToString()"], text=True).strip()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--nuget", type=Path, help="path to Microsoft's signed nuget.exe")
    parser.add_argument("--output", type=Path, help="empty output directory; defaults to artifacts/release")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    if git(repo, "status", "--porcelain", "--untracked-files=no"):
        raise RuntimeError("Commit tracked changes before building a release")
    commit = git(repo, "rev-parse", "HEAD")
    tracked = set(git(repo, "ls-files").splitlines())
    for project in (repo / "VVG.Modbus/VVG.Modbus.csproj", repo / "ModbusClientTest/ModbusClientTest.csproj"):
        for item in ET.parse(project).getroot().iter():
            if item.tag.rsplit("}", 1)[-1] in ("Compile", "Page", "ApplicationDefinition", "EmbeddedResource"):
                name = (project.parent / item.attrib["Include"].replace("\\", "/")).relative_to(repo).as_posix()
                if name not in tracked:
                    raise RuntimeError("Release source is not committed: " + name)
    output = (args.output or repo / "artifacts/release").resolve()
    if output.exists() and any(output.iterdir()):
        raise RuntimeError("Release output must be an empty directory")
    nuget = args.nuget or shutil.which("nuget")
    if not nuget or not Path(nuget).is_file():
        raise RuntimeError("Supply --nuget PATH to the NuGet CLI")
    locator = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
    installations = subprocess.check_output([str(locator), "-all", "-products", "*", "-property", "installationPath"], text=True).splitlines()
    msbuild = next((Path(p) / "MSBuild/Current/Bin/MSBuild.exe" for p in installations
                    if (Path(p) / "MSBuild/Current/Bin/MSBuild.exe").is_file()), None)
    if msbuild is None:
        raise RuntimeError("Visual Studio MSBuild and .NET Framework 4.8/WPF targeting tools are required")
    output.mkdir(parents=True, exist_ok=True)
    build = output / "_build"
    intermediate = output / "_obj"
    execute([msbuild, repo / "ModbusClientTest/ModbusClientTest.csproj", "/t:Rebuild", "/p:Configuration=Release",
        f"/p:OutputPath={build}/", f"/p:BaseIntermediateOutputPath={intermediate}/",
        f"/p:IntermediateOutputPath={intermediate}/", "/verbosity:minimal", "/nologo"], repo)
    client_version = assembly_version(build / "VVG.Modbus.ClientTest.exe")
    library_version = assembly_version(build / "VVG.Modbus.dll")
    tag = "V" + client_version
    names = ["VVG.Modbus.ClientTest.exe", "VVG.Modbus.dll", "log4net.dll", "VVG.Modbus.ClientTest.exe.config"]
    for name in names:
        shutil.copy2(build / name, output / name)
    shutil.copy2(repo / "RELEASE_NOTES.md", output / "RELEASE_NOTES.md")
    shutil.copy2(repo / "LICENSE", output / "LICENSE")
    shutil.copy2(repo / "third-party/NOTICES.txt", output / "THIRD_PARTY_NOTICES.txt")
    shutil.copy2(repo / "third-party/log4net.LICENSE.txt", output / "log4net.LICENSE.txt")
    package = output / "_package"
    package.mkdir()
    shutil.copy2(build / "VVG.Modbus.dll", package / "VVG.Modbus.dll")
    shutil.copy2(repo / "LICENSE", package / "LICENSE")
    shutil.copy2(repo / "README.md", package / "README.md")
    namespace = "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"
    ET.register_namespace("", namespace)
    q = lambda name: "{" + namespace + "}" + name
    spec = ET.parse(repo / "VVG.Modbus/VVG.Modbus.nuspec")
    metadata = spec.getroot().find(q("metadata"))
    raw_version = metadata.find(q("version")).text
    parts = raw_version.split(".")
    package_version = ".".join(parts[:3]) if len(parts) == 4 and parts[3] == "0" else raw_version
    ET.SubElement(metadata, q("license"), {"type": "file"}).text = "LICENSE"
    ET.SubElement(metadata, q("readme")).text = "README.md"
    ET.SubElement(metadata, q("repository"), {"type": "git", "url": "https://github.com/VolutionGroup/ModbusClientCs", "commit": commit})
    ET.SubElement(metadata, q("releaseNotes")).text = "Bounds-first response validation, corrected coil encoding and opt-in extended file-record addresses. See GitHub release notes for compatibility changes."
    files = ET.SubElement(spec.getroot(), q("files"))
    for source, target in (("VVG.Modbus.dll", "lib/net48"), ("LICENSE", ""), ("README.md", "")):
        ET.SubElement(files, q("file"), {"src": source, "target": target})
    spec_path = package / "VVG.Modbus.nuspec"
    spec.write(spec_path, encoding="utf-8", xml_declaration=True)
    execute([nuget, "pack", spec_path, "-OutputDirectory", output, "-NonInteractive"], repo)
    nupkg = output / ("VVG.Modbus." + package_version + ".nupkg")
    with zipfile.ZipFile(nupkg) as archive:
        if hashlib.sha256(archive.read("lib/net48/VVG.Modbus.dll")).hexdigest() != digest(build / "VVG.Modbus.dll"):
            raise RuntimeError("Packaged library differs from the client release library")
    distribution = output / ("VVG.Modbus.ClientTest_" + tag + ".zip")
    with zipfile.ZipFile(distribution, "w", zipfile.ZIP_DEFLATED) as archive:
        for name in names + ["LICENSE", "RELEASE_NOTES.md", "THIRD_PARTY_NOTICES.txt", "log4net.LICENSE.txt"]:
            archive.write(output / name, name)
        archive.write(repo / "ModbusClientTest/README.md", "README.md")
    assets = names + [distribution.name, nupkg.name, "RELEASE_NOTES.md", "LICENSE", "THIRD_PARTY_NOTICES.txt", "log4net.LICENSE.txt",
                     "release-manifest.json", "SHA256SUMS.txt"]
    manifest = dict(sourceCommit=commit, tag=tag, clientVersion=client_version, libraryAssemblyVersion=library_version,
                    packageVersion=package_version, packageLibrarySha256=digest(build / "VVG.Modbus.dll"), assets=assets)
    (output / "release-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    checksums = [digest(output / name) + "  " + name for name in assets if name != "SHA256SUMS.txt"]
    (output / "SHA256SUMS.txt").write_text("\n".join(checksums) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    print("Release assets: " + str(output))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, subprocess.SubprocessError, ET.ParseError) as error:
        print("FAIL release build: " + str(error), file=sys.stderr)
        sys.exit(1)
