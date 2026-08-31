#!/usr/bin/env python3
"""Validate the source tree before a Marrow NPC Toolkit release."""

from __future__ import annotations

import json
import re
import sys
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CORE = ROOT / "Packages" / "com.vergil333.marrow-npc-toolkit"
PROVIDER = ROOT / "Packages" / "com.vergil333.marrow-npc-toolkit.patch6"
PACKAGES = (CORE, PROVIDER)

FORBIDDEN_SUFFIXES = {
    ".aac",
    ".aiff",
    ".anim",
    ".asset",
    ".avi",
    ".bank",
    ".blend",
    ".bmp",
    ".bundle",
    ".bytes",
    ".controller",
    ".cubemap",
    ".dll",
    ".exr",
    ".fbx",
    ".flac",
    ".gif",
    ".jpeg",
    ".jpg",
    ".m4a",
    ".mat",
    ".mov",
    ".mp3",
    ".mp4",
    ".ogg",
    ".physicmaterial",
    ".png",
    ".prefab",
    ".psd",
    ".rendertexture",
    ".tga",
    ".tif",
    ".tiff",
    ".unity",
    ".unitypackage",
    ".wav",
    ".webm",
    ".wem",
    ".zip",
}

FORBIDDEN_NAMES = {
    ".DS_Store",
    "MarrowNpcToolkitPatch6BehaviourSettings.json",
    "MarrowNpcToolkitPatch6Declarations.json",
}

MARROW_ASMDEF_GUID = "20441fdfffb12d24da9276657491883e"
SLZ_DECLARATION_COUNT = 32
GAME_DECLARATIONS = {
    "AgentLinkControl.cs",
    "BehaviourPowerLegs.cs",
    "EyeAndHeadAnimator.cs",
    "GenericSpawnDelayEvent.cs",
    "LimbIKSlz.cs",
    "LookTargetController.cs",
    "PuppetMastaRefs.cs",
    "VisualDamageReceiver.cs",
}

GENERATED_DIRECTORY_NAMES = {
    ".git",
    "Artifacts",
    "Library",
    "Logs",
    "Obj",
    "Temp",
    "UserSettings",
    "__pycache__",
}

TEST_FIXTURE_ASSET_PREFIXES = (
    Path("TestProjects/OfficialSdk-1.2.0/Assets/AddressableAssetsData"),
    Path("TestProjects/OfficialSdk-1.2.0/Assets/XR"),
    Path("TestProjects/OfficialSdk-1.2.0/ProjectSettings"),
)


def fail(message: str) -> None:
    raise RuntimeError(message)


def load_manifest(package: Path) -> dict:
    path = package / "package.json"
    if not path.is_file():
        fail(f"Missing package manifest: {path.relative_to(ROOT)}")
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Invalid package manifest {path.relative_to(ROOT)}: {exc}")


def require_file(path: Path) -> None:
    if not path.is_file():
        fail(f"Missing required file: {path.relative_to(ROOT)}")


def validate_versions(core: dict, provider: dict) -> str:
    core_version = core.get("version")
    provider_version = provider.get("version")
    if not isinstance(core_version, str) or not core_version:
        fail("Core package has no version")
    if provider_version != core_version:
        fail(
            "Core/provider versions differ: "
            f"{core_version!r} != {provider_version!r}"
        )

    dependency_version = provider.get("dependencies", {}).get(
        "com.vergil333.marrow-npc-toolkit"
    )
    if dependency_version != core_version:
        fail(
            "Provider dependency does not match package version: "
            f"{dependency_version!r} != {core_version!r}"
        )

    version_source = CORE / "Runtime" / "NpcToolkitVersion.cs"
    text = version_source.read_text(encoding="utf-8")
    match = re.search(r'Current\s*=\s*"([^"]+)"', text)
    if match is None or match.group(1) != core_version:
        fail("NpcToolkitVersion.Current does not match package.json")

    for changelog in (CORE / "CHANGELOG.md", PROVIDER / "CHANGELOG.md"):
        if f"## {core_version}" not in changelog.read_text(encoding="utf-8"):
            fail(f"Version is missing from {changelog.relative_to(ROOT)}")

    return core_version


def validate_required_files() -> None:
    for path in (
        ROOT / "README.md",
        ROOT / "LICENSE.md",
        ROOT / "THIRD_PARTY_NOTICES.md",
        ROOT / "Documentation" / "INSTALLATION.md",
        ROOT / "Documentation" / "COMPATIBILITY.md",
        ROOT / "Documentation" / "KNOWN_ISSUES.md",
        ROOT / "Documentation" / "RELEASE_CHECKLIST.md",
        ROOT / "Documentation" / "VALIDATION_EVIDENCE.md",
        CORE / "README.md",
        CORE / "LICENSE.md",
        CORE / "THIRD_PARTY_NOTICES.md",
        PROVIDER / "README.md",
        PROVIDER / "LICENSE.md",
        PROVIDER / "THIRD_PARTY_NOTICES.md",
        PROVIDER
        / "Editor"
        / "Vergil333.MarrowNpcToolkit.Patch6.Editor.asmdef",
        PROVIDER
        / "Editor"
        / "MarrowNpcToolkitPatch6DeclarationBootstrap.cs",
        CORE
        / "Editor"
        / "Build"
        / "NpcNativeBuildCoordinator.cs",
        PROVIDER
        / "Runtime"
        / "SLZMarrowDeclarations"
        / "SLZ.Marrow.Patch6.asmref",
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "Packages"
        / "manifest.json",
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "ProjectSettings"
        / "ProjectVersion.txt",
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "Assets"
        / "Editor"
        / "ReleaseSmokeProbe.cs",
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "Assets"
        / "Editor"
        / "ReleaseSmokeProbe.cs.meta",
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "Assets"
        / "Editor"
        / "ProviderPreflightProbe.cs",
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "Assets"
        / "Editor"
        / "ProviderPreflightProbe.cs.meta",
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "Assets"
        / "Editor"
        / "ExactCandidateBuildProbe.cs",
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "Assets"
        / "Editor"
        / "ExactCandidateBuildProbe.cs.meta",
    ):
        require_file(path)


def validate_no_private_content() -> None:
    violations: list[str] = []
    for path in sorted(ROOT.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(ROOT)
        if GENERATED_DIRECTORY_NAMES.intersection(relative.parts):
            continue
        if path.name in FORBIDDEN_NAMES:
            violations.append(str(relative))
            continue
        if path.suffix.lower() not in FORBIDDEN_SUFFIXES:
            continue
        if path.suffix.lower() == ".asset" and any(
            relative == prefix or prefix in relative.parents
            for prefix in TEST_FIXTURE_ASSET_PREFIXES
        ):
            continue
        violations.append(str(relative))

    if violations:
        fail("Forbidden release content:\n  " + "\n  ".join(violations))


def validate_generated_test_declarations() -> None:
    fixture_assets = (
        ROOT / "TestProjects" / "OfficialSdk-1.2.0" / "Assets"
    )
    generated_root = fixture_assets / "MarrowNpcToolkit"
    root_meta = Path(str(generated_root) + ".meta")
    if not generated_root.exists() and not root_meta.exists():
        return

    template_root = (
        PROVIDER
        / "Editor"
        / "DeclarationTemplates~"
        / "AssemblyCSharp"
    )
    assembly_root = (
        generated_root / "Patch6Declarations" / "AssemblyCSharp"
    )
    expected = {
        root_meta,
        Path(str(generated_root / "Patch6Declarations") + ".meta"),
        Path(str(assembly_root) + ".meta"),
    }
    for name in GAME_DECLARATIONS:
        expected.add(assembly_root / name)
        expected.add(assembly_root / f"{name}.meta")

    found = {path for path in generated_root.rglob("*") if path.is_file()}
    if root_meta.is_file():
        found.add(root_meta)
    extra = sorted(str(path.relative_to(ROOT)) for path in found - expected)
    missing = sorted(str(path.relative_to(ROOT)) for path in expected - found)
    if extra or missing:
        fail(
            "Generated test declaration fixture differs from the exact "
            f"allowlist; missing={missing}, extra={extra}"
        )

    for name in sorted(GAME_DECLARATIONS):
        installed = assembly_root / name
        template = template_root / f"{name}.txt"
        installed_meta = assembly_root / f"{name}.meta"
        template_meta = template_root / f"{name}.meta.txt"
        if installed.read_bytes() != template.read_bytes():
            fail(f"Generated test declaration differs from template: {name}")
        if installed_meta.read_bytes() != template_meta.read_bytes():
            fail(f"Generated test declaration meta differs from template: {name}")


def validate_declaration_only_sources() -> None:
    method_body = re.compile(
        r"\b(?:public|protected|internal|private)\s+"
        r"(?:static\s+|virtual\s+|override\s+|abstract\s+|sealed\s+|new\s+)*"
        r"[A-Za-z_][A-Za-z0-9_<>.,\[\]?\s]*\s+"
        r"[A-Za-z_][A-Za-z0-9_]*\s*\([^;{}]*\)\s*\{",
        re.MULTILINE,
    )
    constructor_body = re.compile(
        r"\b(?:public|protected|internal|private)\s+"
        r"[A-Za-z_][A-Za-z0-9_]*\s*\([^;{}]*\)\s*\{",
        re.MULTILINE,
    )
    declaration_sources = list(
        (PROVIDER / "Runtime" / "SLZMarrowDeclarations").glob("*.cs")
    ) + list(
        (
            PROVIDER
            / "Editor"
            / "DeclarationTemplates~"
            / "AssemblyCSharp"
        ).glob("*.cs.txt")
    )
    for path in sorted(declaration_sources):
        text = path.read_text(encoding="utf-8")
        if (
            "=>" in text
            or method_body.search(text)
            or constructor_body.search(text)
            or re.search(r"\b(?:return|throw|yield)\b", text)
        ):
            fail(
                "Declaration-only compatibility source contains a method "
                f"implementation: {path.relative_to(ROOT)}"
            )


def validate_manifests(core: dict, provider: dict) -> None:
    expected = {
        "com.vergil333.marrow-npc-toolkit": CORE,
        "com.vergil333.marrow-npc-toolkit.patch6": PROVIDER,
    }
    for manifest, package in ((core, CORE), (provider, PROVIDER)):
        name = manifest.get("name")
        if expected.get(name) != package:
            fail(f"Unexpected package name {name!r} in {package.relative_to(ROOT)}")
        if manifest.get("unity") != "2021.3" or manifest.get("unityRelease") != "16f1":
            fail(f"Unexpected Unity version in {package.relative_to(ROOT)}")
        for field in (
            "displayName",
            "description",
            "documentationUrl",
            "changelogUrl",
            "licensesUrl",
            "repository",
            "bugs",
        ):
            if not manifest.get(field):
                fail(f"Missing {field} in {package.relative_to(ROOT)}/package.json")

    if core.get("dependencies", {}).get(
        "com.stresslevelzero.marrow.sdk"
    ) != "1.2.0":
        fail("Core package must pin the official Marrow SDK 1.2.0")
    if provider.get("dependencies", {}).get(
        "com.stresslevelzero.marrow.sdk"
    ) != "1.2.0":
        fail("Provider package must pin the official Marrow SDK 1.2.0")


def validate_declaration_boundary() -> None:
    declaration_root = PROVIDER / "Runtime" / "SLZMarrowDeclarations"
    declarations = sorted(declaration_root.glob("*.cs"))
    if len(declarations) != SLZ_DECLARATION_COUNT:
        fail(
            "Expected "
            f"{SLZ_DECLARATION_COUNT} SLZ.Marrow declarations, found "
            f"{len(declarations)}"
        )

    asmref_path = declaration_root / "SLZ.Marrow.Patch6.asmref"
    try:
        asmref = json.loads(asmref_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Invalid declaration asmref: {exc}")
    if asmref.get("reference") != f"GUID:{MARROW_ASMDEF_GUID}":
        fail("Patch 6 declaration asmref does not target official SLZ.Marrow")

    template_root = (
        PROVIDER
        / "Editor"
        / "DeclarationTemplates~"
        / "AssemblyCSharp"
    )
    found_templates = {
        path.name.removesuffix(".txt")
        for path in template_root.glob("*.cs.txt")
    }
    if found_templates != GAME_DECLARATIONS:
        missing = sorted(GAME_DECLARATIONS - found_templates)
        extra = sorted(found_templates - GAME_DECLARATIONS)
        fail(
            "Assembly-CSharp declaration templates differ from the contract; "
            f"missing={missing}, extra={extra}"
        )
    for name in sorted(GAME_DECLARATIONS):
        require_file(template_root / f"{name}.meta.txt")

    bootstrap = (
        PROVIDER
        / "Editor"
        / "MarrowNpcToolkitPatch6DeclarationBootstrap.cs"
    ).read_text(encoding="utf-8")
    for name in sorted(GAME_DECLARATIONS):
        if f'"{name}"' not in bootstrap:
            fail(f"Declaration bootstrap does not list {name}")
    if "Assets/MarrowNpcToolkit/Patch6Declarations/AssemblyCSharp" not in bootstrap:
        fail("Declaration bootstrap output path changed unexpectedly")


def validate_provider_identity() -> None:
    probe = (
        PROVIDER / "Editor" / "MarrowNpcToolkitPatch6CompatibilityProbe.cs"
    ).read_text(encoding="utf-8")
    anatomy = (
        PROVIDER / "Editor" / "MarrowNpcToolkitPatch6NativeAnatomyProvider.cs"
    ).read_text(encoding="utf-8")
    probe_match = re.search(r'ProviderId\s*=>\s*"([^"]+)"', probe)
    anatomy_match = re.search(
        r'ProviderIdStatic\s*=\s*"([^"]+)"', anatomy, re.MULTILINE
    )
    expected = "vergil333.bonelab-patch6"
    if probe_match is None or probe_match.group(1) != expected:
        fail("Compatibility probe provider ID is not the public stable ID")
    if anatomy_match is None or anatomy_match.group(1) != expected:
        fail("Anatomy fingerprint provider ID does not match the public probe")


def validate_unity_metadata() -> None:
    guid_paths: dict[str, list[str]] = defaultdict(list)
    for package in PACKAGES:
        for path in sorted(package.rglob("*")):
            relative_parts = path.relative_to(package).parts
            ignored_by_unity = any(part.endswith("~") for part in relative_parts)
            if path.is_dir():
                if not ignored_by_unity and not Path(str(path) + ".meta").is_file():
                    fail(f"Missing folder meta: {path.relative_to(ROOT)}")
                continue
            if path.suffix in {".cs", ".asmdef", ".asmref"}:
                meta = Path(str(path) + ".meta")
                if not meta.is_file():
                    fail(f"Missing Unity meta: {path.relative_to(ROOT)}")
            if path.suffix in {".json", ".asmdef", ".asmref"}:
                try:
                    json.loads(path.read_text(encoding="utf-8"))
                except (OSError, json.JSONDecodeError) as exc:
                    fail(f"Invalid JSON file {path.relative_to(ROOT)}: {exc}")
            if path.suffix == ".meta":
                match = re.search(
                    r"^guid:\s*([0-9a-f]{32})\s*$",
                    path.read_text(encoding="utf-8"),
                    re.MULTILINE,
                )
                if match is None:
                    fail(f"Missing/invalid GUID in {path.relative_to(ROOT)}")
                guid_paths[match.group(1)].append(str(path.relative_to(ROOT)))

    duplicates = {
        guid: paths for guid, paths in guid_paths.items() if len(paths) > 1
    }
    if duplicates:
        detail = "\n".join(
            f"  {guid}: {', '.join(paths)}"
            for guid, paths in sorted(duplicates.items())
        )
        fail("Duplicate Unity GUIDs:\n" + detail)


def validate_text_safety() -> None:
    absolute_path = re.compile(
        r"(?:/Users/[^\s`'\"]+|(?<![A-Za-z0-9_])[A-Za-z]:\\[^\r\n]+)"
    )
    for path in sorted(ROOT.rglob("*")):
        if not path.is_file() or path.suffix in {".meta", ".pyc"}:
            continue
        if GENERATED_DIRECTORY_NAMES.intersection(path.relative_to(ROOT).parts):
            continue
        if path.resolve() == Path(__file__).resolve():
            continue
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        match = absolute_path.search(text)
        if match:
            fail(
                "Local absolute path in release text "
                f"{path.relative_to(ROOT)}: {match.group(0)!r}"
            )


def validate_test_project() -> None:
    manifest_path = (
        ROOT
        / "TestProjects"
        / "OfficialSdk-1.2.0"
        / "Packages"
        / "manifest.json"
    )
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Invalid official-SDK test manifest: {exc}")
    dependencies = manifest.get("dependencies", {})
    if dependencies.get("com.stresslevelzero.marrow.sdk") != "1.2.0":
        fail("Official-SDK test project must pin Marrow SDK 1.2.0")
    if dependencies.get("com.vergil333.marrow-npc-toolkit") != (
        "file:../../../Packages/com.vergil333.marrow-npc-toolkit"
    ):
        fail("Test project core package path is not the release source")
    if dependencies.get("com.vergil333.marrow-npc-toolkit.patch6") != (
        "file:../../../Packages/com.vergil333.marrow-npc-toolkit.patch6"
    ):
        fail("Test project provider package path is not the release source")


def validate_tag(version: str) -> None:
    tag_file = ROOT / ".git" / "HEAD"
    if not tag_file.exists():
        return
    # CI supplies the candidate tag explicitly. Local validation does not
    # require the worktree to already have a release tag.
    import os

    candidate = os.environ.get("RELEASE_TAG", "").strip()
    if candidate and candidate != f"v{version}":
        fail(f"RELEASE_TAG {candidate!r} does not match v{version}")


def main() -> int:
    try:
        core = load_manifest(CORE)
        provider = load_manifest(PROVIDER)
        validate_required_files()
        validate_manifests(core, provider)
        version = validate_versions(core, provider)
        require_file(
            ROOT
            / "Documentation"
            / f"RELEASE_NOTES_{version}.md"
        )
        validate_no_private_content()
        validate_generated_test_declarations()
        validate_declaration_only_sources()
        validate_declaration_boundary()
        validate_provider_identity()
        validate_unity_metadata()
        validate_text_safety()
        validate_test_project()
        validate_tag(version)
    except RuntimeError as exc:
        print(f"release validation failed: {exc}", file=sys.stderr)
        return 1

    print(f"release validation passed for {version}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
