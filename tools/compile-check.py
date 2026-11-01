#!/usr/bin/env python3
"""Fast compile check for the game code, without going through the Unity editor.

Builds Assembly-CSharp and Assembly-CSharp-Editor with `dotnet build`. It uses
the Unity-generated *.csproj files as templates, with two changes:
  * Compile items come from globbing Assets/**/*.cs, so new or renamed files are
    picked up even when Unity hasn't regenerated the csproj files yet.
  * ProjectReferences to package assemblies point at the DLLs Unity has already
    compiled in Library/ScriptAssemblies, so packages aren't rebuilt.

It works whether or not the editor is open. It does NOT run Unity's IL
post-processors (Netcode RPC codegen), so a few Netcode-specific errors (e.g.
invalid RPC signatures) only show up in the editor.

Usage:  python3 tools/compile-check.py
Exit code 0 = compiles, 1 = compile errors, 2 = setup problem.
"""
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Temp" / "AgentCompile"
SCRIPT_ASSEMBLIES = ROOT / "Library" / "ScriptAssemblies"
NS = "http://schemas.microsoft.com/developer/msbuild/2003"
ET.register_namespace("", NS)


def q(tag):
    return f"{{{NS}}}{tag}"


def rewrite(name, compile_include, compile_exclude, own_outputs):
    src = ROOT / f"{name}.csproj"
    if not src.exists():
        sys.exit(f"[compile-check] {src.name} is missing. Open the project in Unity once "
                 "(or Preferences > External Tools > Regenerate project files).")
    tree = ET.parse(src)
    proj = tree.getroot()

    for group in proj.findall(q("ItemGroup")):
        for item in list(group):
            if item.tag == q("Compile"):
                group.remove(item)
            elif item.tag == q("ProjectReference"):
                ref_name = Path(item.get("Include")).stem
                dll = own_outputs.get(ref_name, SCRIPT_ASSEMBLIES / f"{ref_name}.dll")
                group.remove(item)
                ref = ET.SubElement(group, q("Reference"), Include=ref_name)
                ET.SubElement(ref, q("HintPath")).text = str(dll)
                ET.SubElement(ref, q("Private")).text = "false"

    # Don't copy hundreds of reference DLLs next to the output.
    for ref in proj.iter(q("Reference")):
        if ref.find(q("Private")) is None:
            ET.SubElement(ref, q("Private")).text = "false"

    group = ET.SubElement(proj, q("ItemGroup"))
    ET.SubElement(group, q("Compile"), Include=str(ROOT / compile_include),
                  Exclude=";".join(str(ROOT / e) for e in compile_exclude))

    out_dir = OUT / "bin" / name
    for pg in proj.findall(q("PropertyGroup")):
        for op in pg.findall(q("OutputPath")):
            op.text = str(out_dir) + "/"
    props = ET.SubElement(proj, q("PropertyGroup"))
    ET.SubElement(props, q("BaseIntermediateOutputPath")).text = str(OUT / "obj" / name) + "/"
    ET.SubElement(props, q("GenerateDocumentationFile")).text = "false"

    # The import must stay last so the properties above are seen by the targets.
    imp = proj.find(q("Import"))
    proj.remove(imp)
    proj.append(imp)

    OUT.mkdir(parents=True, exist_ok=True)
    dst = OUT / f"{name}.csproj"
    tree.write(dst, xml_declaration=True, encoding="utf-8")
    return dst, out_dir / f"{name}.dll"


def build(csproj):
    proc = subprocess.run(
        ["dotnet", "build", str(csproj), "-nologo", "-v", "q", "-clp:NoSummary",
         "-p:BuildProjectReferences=false"],
        cwd=ROOT, capture_output=True, text=True)
    lines = (proc.stdout + proc.stderr).splitlines()
    diag = re.compile(r"^(.*?)\((\d+),(\d+)\): (error|warning) (\w+): (.*?)(?: \[.*\])?$")
    errors, seen = [], set()
    for line in lines:
        m = diag.match(line.strip())
        if m and m.group(4) == "error":
            path = Path(m.group(1))
            rel = path.relative_to(ROOT) if path.is_absolute() and ROOT in path.parents else path
            msg = f"{rel}:{m.group(2)}:{m.group(3)}: {m.group(5)}: {m.group(6)}"
            if msg not in seen:
                seen.add(msg)
                errors.append(msg)
    if proc.returncode != 0 and not errors:
        # Setup problem (missing DLL, SDK issue...): show the raw output.
        errors = [l for l in lines if "error" in l.lower()] or lines[-20:]
    return proc.returncode == 0, errors


def main():
    if not SCRIPT_ASSEMBLIES.exists():
        print("[compile-check] Library/ScriptAssemblies is missing. Open the project in Unity once.")
        return 2

    runtime_proj, runtime_dll = rewrite(
        "Assembly-CSharp", "Assets/**/*.cs", ["Assets/**/Editor/**"], {})
    ok, errors = build(runtime_proj)
    if ok:
        editor_proj, _ = rewrite(
            "Assembly-CSharp-Editor", "Assets/**/Editor/**/*.cs", [],
            {"Assembly-CSharp": runtime_dll})
        ok, errors = build(editor_proj)

    if ok:
        print("[compile-check] OK: Assembly-CSharp and Assembly-CSharp-Editor compile.")
        return 0
    print(f"[compile-check] FAILED ({len(errors)} error(s)):")
    for e in errors:
        print("  " + e)
    return 1


if __name__ == "__main__":
    sys.exit(main())
