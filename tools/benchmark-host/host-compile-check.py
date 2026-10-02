"""Roslyn compile check of every compiled Onity package assembly in the benchmark host.

Collects all C# files of Packages/com.onity.framework whose owning assembly Unity currently compiles in
the host (Library/ScriptAssemblies/<assembly>.dll exists; define-gated assemblies that are off, such as
Onity.Benchmarks, are skipped and listed), adds the .cs files a staging run deleted from the host, and
calls unity_compile_check.main() in-process with --dependents, so the file list never hits the Windows
command-line limit. Exit codes are the checker's: 0 COMPILE_OK, 1 COMPILE_ERRORS, 2 COMPILE_UNVERIFIED,
3 usage error.

An .asmdef/.asmref change makes the generated projects stale; regenerate them first (Unity
-executeMethod UnityEditor.SyncVS.SyncSolution), as run-attempt.ps1 does.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

DEFAULT_CHECKER = "C:/Users/e-fur/.claude/skills/unity-cli/scripts/unity_compile_check.py"
PACKAGE = Path("Packages") / "com.onity.framework"


def owning_assembly(directory, package_root, cache):
    current = directory
    while True:
        if current in cache:
            return cache[current]
        asmdefs = sorted(current.glob("*.asmdef"))
        if asmdefs:
            name = json.loads(asmdefs[0].read_text(encoding="utf-8-sig")).get("name")
            cache[current] = name
            return name
        if current == package_root or current.parent == current:
            cache[current] = None
            return None
        current = current.parent


def collect(project):
    package_root = project / PACKAGE
    compiled = {path.stem.lower() for path in (project / "Library" / "ScriptAssemblies").glob("*.dll")}
    cache = {}
    files = []
    skipped = {}
    for path in sorted(package_root.rglob("*.cs")):
        parts = path.relative_to(package_root).parts[:-1]
        if any(part.startswith(".") or part.endswith("~") for part in parts):
            continue
        assembly = owning_assembly(path.parent, package_root, cache)
        if assembly and assembly.lower() in compiled:
            files.append(str(path))
        else:
            skipped[str(assembly)] = skipped.get(str(assembly), 0) + 1
    return files, skipped


def deleted_from_staging(project, staging):
    data = json.loads(Path(staging).read_text(encoding="utf-8-sig"))
    deleted = []
    for relative in data.get("extraInHost") or []:
        if relative.lower().endswith(".cs"):
            path = project / PACKAGE / Path(relative)
            if not path.exists():
                deleted.append(str(path))
    return deleted


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--project", required=True, help="Host Unity project root.")
    parser.add_argument("--staging", help="staging.json from stage-host.ps1; its deleted .cs files are passed too.")
    parser.add_argument("--checker", default=DEFAULT_CHECKER, help="Path to unity_compile_check.py.")
    parser.add_argument("--list", help="Optional file that receives the checked file list.")
    args = parser.parse_args(argv)
    project = Path(args.project).resolve()
    checker = Path(args.checker)
    if not checker.is_file():
        print(f"USAGE: checker not found: {checker}")
        return 3
    files, skipped = collect(project)
    if args.staging:
        files += deleted_from_staging(project, args.staging)
    if not files:
        print("USAGE: no compiled package C# files found; has Unity compiled the host?")
        return 3
    print(f"host-compile-check: {len(files)} file(s); skipped (assembly not compiled in host): "
          + (", ".join(f"{name}={count}" for name, count in sorted(skipped.items())) or "none"))
    if args.list:
        Path(args.list).write_text("\n".join(files) + "\n", encoding="utf-8")
    sys.stdout.flush()
    spec = importlib.util.spec_from_file_location("unity_compile_check", checker)
    module = importlib.util.module_from_spec(spec)
    sys.modules["unity_compile_check"] = module
    spec.loader.exec_module(module)
    return module.main(["--project", str(project), "--dependents", "--files", *files])


if __name__ == "__main__":
    sys.exit(main())
