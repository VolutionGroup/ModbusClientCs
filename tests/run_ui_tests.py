"""Build the WPF test harness and exercise polling, logging and file transfers without hardware."""
from pathlib import Path
import argparse
import os
import shutil
import subprocess
import sys
import tempfile


def run(command, cwd, timeout=120):
    result = subprocess.run([str(part) for part in command], cwd=cwd, capture_output=True, text=True, timeout=timeout)
    print(result.stdout, end="")
    print(result.stderr, end="")
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts", type=Path, help="save a rendered window preview here")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    locator = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
    installations = subprocess.check_output([str(locator), "-all", "-products", "*", "-property", "installationPath"], text=True).splitlines()
    installation = next((Path(p) for p in installations if (Path(p) / "MSBuild/Current/Bin/MSBuild.exe").is_file()), None)
    if installation is None:
        raise RuntimeError("Visual Studio MSBuild with .NET Framework 4.8/WPF targeting tools is required")
    framework = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8"
    with tempfile.TemporaryDirectory(prefix="modbus_ui_tests_") as directory:
        work = Path(directory)
        build = work / "build"
        command = [installation / "MSBuild/Current/Bin/MSBuild.exe", repo / "ModbusClientTest/ModbusClientTest.csproj",
                   "/t:Build", "/p:Configuration=Release", f"/p:OutputPath={build}/",
                   f"/p:BaseIntermediateOutputPath={work / 'obj'}/", f"/p:IntermediateOutputPath={work / 'obj'}/",
                   "/verbosity:minimal", "/nologo"]
        status = run(command, work)
        if status:
            return status
        executable = build / "ClientTestUiTests.exe"
        command = [installation / "MSBuild/Current/Bin/Roslyn/csc.exe", "/nologo", "/langversion:7.3", "/warn:4",
                   f"/out:{executable}", f"/reference:{build / 'VVG.Modbus.ClientTest.exe'}",
                   f"/reference:{build / 'VVG.Modbus.dll'}", f"/reference:{build / 'log4net.dll'}"]
        command += [f"/reference:{framework / (name + '.dll')}" for name in
                    ("WindowsBase", "PresentationCore", "PresentationFramework", "System.Xaml")]
        command += [repo / "ModbusClientTest" / name for name in
                    ("RefreshCoordinator.cs", "ActivityLogAppender.cs", "FileTransferRunner.cs")]
        command += [repo / "tests/ClientTestUiTests.cs"]
        status = run(command, work)
        if status:
            return status
        status = run([executable, work], build, timeout=90)
        if args.artifacts:
            args.artifacts.mkdir(parents=True, exist_ok=True)
            preview = work / "window-preview.png"
            if preview.is_file():
                shutil.copy2(preview, args.artifacts / preview.name)
        return status


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
        print("FAIL infrastructure: " + str(error), file=sys.stderr)
        sys.exit(2)
