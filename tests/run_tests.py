"""Compile the actual .NET Framework client sources and run transport-double regressions."""
from pathlib import Path
import argparse
import os
import shutil
import subprocess
import sys
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-source", type=Path, help="optional baseline ClientRTU_Base.cs for comparison")
    args = parser.parse_args()
    tests = Path(__file__).resolve().parent
    repo = tests.parent
    compiler = os.environ.get("CSC")
    if not compiler:
        locator = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
        installs = subprocess.check_output([str(locator), "-all", "-products", "*", "-property", "installationPath"], text=True).splitlines()
        compiler = next((str(Path(p) / "MSBuild/Current/Bin/Roslyn/csc.exe") for p in installs
                         if (Path(p) / "MSBuild/Current/Bin/Roslyn/csc.exe").is_file()), None)
    if not compiler:
        raise RuntimeError("Set CSC to the Visual Studio Roslyn C# compiler")
    candidates = list((repo / "packages").glob("log4net.*/lib/net462/log4net.dll"))
    candidates += [repo / "dependencies/log4net.dll"]
    dependency = next((p for p in candidates if p.is_file()), None)
    if dependency is None:
        raise RuntimeError("Restore the project's log4net package before running")
    with tempfile.TemporaryDirectory(prefix="modbus_cs_regression_") as directory:
        build = Path(directory)
        shutil.copy2(dependency, build / "log4net.dll")
        sources = sorted((repo / "VVG.Modbus").glob("*.cs"))
        if args.base_source:
            sources = [args.base_source.resolve() if p.name == "ClientRTU_Base.cs" else p for p in sources]
        executable = build / "RegressionTests.exe"
        command = [compiler, "/nologo", "/langversion:7.3", "/warn:4", "/target:exe", f"/out:{executable}",
                   f"/reference:{build / 'log4net.dll'}", "/reference:System.Xml.Linq.dll", "/reference:System.Net.Http.dll"]
        command += [str(p) for p in sources] + [str(tests / "RegressionTests.cs")]
        built = subprocess.run(command, cwd=build, capture_output=True, text=True, timeout=60)
        print(built.stdout, end="")
        print(built.stderr, end="")
        if built.returncode:
            return built.returncode
        run = subprocess.run([str(executable)], cwd=build, capture_output=True, text=True, timeout=60)
        print(run.stdout, end="")
        print(run.stderr, end="")
        return run.returncode


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
        print("FAIL infrastructure: " + str(error), file=sys.stderr)
        sys.exit(2)
