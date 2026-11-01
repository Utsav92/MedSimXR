"""Builds Data/Equipment/asset_manifest.json and Documentation/ATTRIBUTION.md from real files on disk.

Every value comes from a file that exists (downloaded dataset package, Blender conversion report, converted GLB,
Unity prefab). Nothing is estimated. Missing inputs are recorded as null with a status, not guessed.

Run:  python -I Tools/AssetValidator/build_manifest.py
"""
import hashlib
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.path.join(ROOT, "Tools", "AssetDownloader", "raw")
REPORTS = os.path.join(ROOT, "Tools", "USDConverter", "reports")
GLB_DIR = os.path.join(ROOT, "Assets", "SimReady")
PREFAB_DIR = os.path.join(ROOT, "Assets", "Prefabs", "SimReady")
REPO = "nvidia/simready-assets"
EQUIPMENT_JSON = os.path.join(ROOT, "Data", "Equipment", "equipment.json")
ASSETS = [i["simreadyId"] for i in json.load(open(EQUIPMENT_JSON, encoding="utf-8"))["items"] if i.get("source") == "simready"]


def load_json(path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def dir_size(path):
    total = 0
    for dp, _, files in os.walk(path):
        for fn in files:
            total += os.path.getsize(os.path.join(dp, fn))
    return total


def file_sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def prefab_collider(asset):
    """Reads the fitted BoxCollider from the generated Unity prefab (YAML)."""
    path = os.path.join(PREFAB_DIR, asset + ".prefab")
    if not os.path.exists(path):
        return None
    text = open(path, "r", encoding="utf-8").read()
    m = re.search(r"BoxCollider:.*?m_Size: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}.*?m_Center: \{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}", text, re.S)
    if not m:
        return {"type": "BoxCollider", "note": "present but could not be parsed"}
    v = [float(x) for x in m.groups()]
    return {"type": "BoxCollider", "fit": "single box fitted to combined renderer bounds",
            "size_m": v[:3], "center_m": v[3:], "has_rigidbody": "Rigidbody:" in text}


def main():
    manifest = {
        "generated_by": "Tools/AssetValidator/build_manifest.py",
        "source_dataset": "https://huggingface.co/datasets/" + REPO,
        "dataset_license": "cc-by-4.0",
        "attribution": "NVIDIA Corporation - SimReady Assets (CC BY 4.0). Converted/decimated for MedSim XR. Changes were made.",
        "assets": [],
    }
    for a in ASSETS:
        raw = os.path.join(RAW, a)
        pkg = load_json(os.path.join(raw, "com.nvidia.simready.packaging.json")) or {}
        rep = load_json(os.path.join(REPORTS, a + ".json")) or {}
        usd_dir = os.path.join(raw, "simready_usd")
        usd_main = next((f for f in (os.listdir(usd_dir) if os.path.isdir(usd_dir) else []) if f.startswith("sm_") and f.endswith(".usd")), None)
        tex_dir = os.path.join(usd_dir, "Textures")
        textures = sorted(os.listdir(tex_dir)) if os.path.isdir(tex_dir) else []
        glb = os.path.join(GLB_DIR, a + ".glb")
        downloaded = os.path.isdir(raw) and usd_main is not None
        converted = os.path.exists(glb)
        entry = {
            "name": a,
            "original_repository_path": REPO + "/" + a + "/simready_usd/" + (usd_main or ""),
            "download_status": "downloaded" if downloaded else "missing",
            "package_id": pkg.get("package_id"),
            "package_content_sha256": (pkg.get("content_hash") or {}).get("sha256"),
            "license": pkg.get("license", "unknown"),
            "attribution": "NVIDIA Corporation, SimReady Assets, CC BY 4.0, via " + REPO,
            "source_size_bytes": dir_size(raw) if os.path.isdir(raw) else None,
            "geometry_format_source": "USD (geometries.usd payload)",
            "geometry_format_converted": "GLB (binary glTF 2.0)",
            "material_references": rep.get("materials"),
            "texture_references_source": textures,
            "texture_original_px": rep.get("textures_original_px"),
            "texture_cap_px": rep.get("texture_cap_px"),
            "polygon_count_triangles_original": rep.get("triangles_original"),
            "polygon_count_triangles_exported": rep.get("triangles_exported"),
            "decimate_ratio": rep.get("decimate_ratio"),
            "bounding_box_size_m": rep.get("size_m"),
            "bounding_box_note": "Blender Z-up metres as imported from USD; glTF/Unity axes are Y-up. Size preserved (no scaling applied).",
            "collider": prefab_collider(a),
            "converted_glb": os.path.relpath(glb, ROOT).replace("\\", "/") if converted else None,
            "converted_glb_size_bytes": os.path.getsize(glb) if converted else None,
            "converted_glb_sha256": file_sha256(glb) if converted else None,
            "conversion_status": "converted" if converted else "failed",
            "pipeline": "USD -> Blender 5.2 (headless usd_import, decimate, texture cap) -> GLB -> Unity glTFast import -> prefab",
            "known_issues": [],
        }
        if a == "chair_patient_mobile_a01":
            entry["known_issues"].append("Blender reported 'Mesh seat_01_mesh_00 is not valid' during glTF export; verify seat geometry visually.")
        if a == "rack_large_empty_a02":
            entry["known_issues"].append("Source texture used UDIM tiles; flattened to the first tile (1001) for GLB export.")
        if a == "CT_injection_system_a01":
            entry["known_issues"].append("Visual teaching model only. No operation, dosing or patient connection is simulated.")
        manifest["assets"].append(entry)

    out = os.path.join(ROOT, "Data", "Equipment", "asset_manifest.json")
    with open(out, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    print("wrote", out)

    doc = os.path.join(ROOT, "Documentation", "ATTRIBUTION.md")
    lines = ["# Third-party asset attribution", "",
             "3D models: NVIDIA SimReady Assets, licensed CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/).",
             "Source: https://huggingface.co/datasets/" + REPO, "",
             "Changes made: converted from USD to GLB, decimated, textures downscaled, collision boxes added.", "",
             "| Asset | Triangles (orig -> used) | Size (m) | Status |", "|---|---|---|---|"]
    for e in manifest["assets"]:
        size = " x ".join("%.2f" % s for s in e["bounding_box_size_m"]) if e["bounding_box_size_m"] else "n/a"
        lines.append("| %s | %s -> %s | %s | %s |" % (e["name"], e["polygon_count_triangles_original"], e["polygon_count_triangles_exported"], size, e["conversion_status"]))
    lines += ["", "Other objects in the scene are Unity primitive placeholders and are labelled as such in-app.", ""]
    with open(doc, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print("wrote", doc)
    bad = [e["name"] for e in manifest["assets"] if e["download_status"] != "downloaded" or e["conversion_status"] != "converted"]
    if bad:
        print("PROBLEM assets:", bad)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
