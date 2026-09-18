"""Build the small, repository-owned Little Ages diorama GLB kit.

Run with Blender, not host Python:
  blender --background --factory-startup --python scripts/build-diorama-assets.py

The source is intentionally procedural and texture-free. This keeps the art
reproducible, makes material reuse explicit, and avoids opaque DCC-only edits.
"""

from __future__ import annotations

from pathlib import Path
import math

import bpy


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "LittleAges.Web" / "public" / "assets" / "diorama"
SOURCE = ROOT / "art" / "diorama" / "little-ages-diorama-kit.blend"


def reset() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in bpy.data.materials:
        bpy.data.materials.remove(block)


def material(name: str, color: tuple[float, float, float, float]) -> bpy.types.Material:
    value = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    value.diffuse_color = color
    value.roughness = 0.9
    return value


MATERIALS: dict[str, bpy.types.Material]


def finish(obj: bpy.types.Object, name: str, mat: str, parent: bpy.types.Object | None = None) -> bpy.types.Object:
    obj.name = name
    obj.data.materials.append(MATERIALS[mat])
    if parent is not None:
        obj.parent = parent
    return obj


def cube(name: str, scale: tuple[float, float, float], location: tuple[float, float, float], mat: str, parent: bpy.types.Object) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = finish(bpy.context.object, name, mat, parent)
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def cone(name: str, radius: float, depth: float, location: tuple[float, float, float], mat: str, parent: bpy.types.Object, vertices: int = 6) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=0, depth=depth, location=location)
    return finish(bpy.context.object, name, mat, parent)


def cylinder(name: str, radius: float, depth: float, location: tuple[float, float, float], mat: str, parent: bpy.types.Object, rotation=(0.0, 0.0, 0.0)) -> bpy.types.Object:
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=radius, depth=depth, location=location, rotation=rotation)
    return finish(bpy.context.object, name, mat, parent)


def ico(name: str, radius: float, location: tuple[float, float, float], mat: str, parent: bpy.types.Object) -> bpy.types.Object:
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=radius, location=location)
    return finish(bpy.context.object, name, mat, parent)


def root(name: str) -> bpy.types.Object:
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    return obj


def shelter() -> bpy.types.Object:
    value = root("Shelter")
    cube("ShelterWalls", (0.52, 0.42, 0.48), (0, 0, 0.42), "Clay", value)
    roof = cone("ShelterRoof", 0.82, 0.68, (0, 0, 1.06), "Thatch", value, 4)
    roof.rotation_euler[2] = math.pi / 4
    cube("ShelterDoor", (0.12, 0.025, 0.22), (0, -0.49, 0.25), "DarkWood", value)
    return value


def stockpile() -> bpy.types.Object:
    value = root("Stockpile")
    cube("StockpileDeck", (0.58, 0.12, 0.58), (0, 0, 0.12), "DarkWood", value)
    for index, (x, y, angle) in enumerate(((-0.27, 0.08, 0.0), (0.18, 0.14, 0.25), (0.02, -0.23, -0.18))):
        cylinder(f"StockpileLog{index}", 0.14, 0.72, (x, y, 0.38), "Wood", value, (0, math.pi / 2, angle))
    return value


def workshop() -> bpy.types.Object:
    value = root("Workshop")
    cube("WorkshopWalls", (0.68, 0.48, 0.56), (0, 0, 0.48), "Stone", value)
    roof = cone("WorkshopRoof", 0.98, 0.62, (0, 0, 1.17), "DarkWood", value, 4)
    roof.rotation_euler[2] = math.pi / 4
    cube("WorkshopChimney", (0.11, 0.11, 0.38), (0.36, 0.12, 1.34), "Charcoal", value)
    return value


def resource_models() -> list[bpy.types.Object]:
    food = root("FoodCluster")
    for index, (x, y, height) in enumerate(((-0.18, 0.0, 0.24), (0.12, 0.08, 0.31), (0.02, -0.14, 0.2))):
        cylinder(f"FoodStem{index}", 0.025, height, (x, y, height / 2), "Leaf", food)
        ico(f"FoodBerry{index}", 0.12, (x, y, height + 0.06), "Berry", food)
    wood = root("WoodCluster")
    cylinder("WoodTrunk", 0.12, 0.72, (0, 0, 0.36), "Wood", wood)
    cone("WoodCrownLow", 0.52, 0.72, (0, 0, 0.86), "Pine", wood, 7)
    cone("WoodCrownHigh", 0.38, 0.62, (0, 0, 1.23), "Leaf", wood, 7)
    stone = root("StoneCluster")
    for index, (x, y, size) in enumerate(((-0.16, 0.02, 0.22), (0.15, 0.08, 0.27), (0.03, -0.17, 0.17))):
        ico(f"Stone{index}", size, (x, y, size * 0.72), "Stone", stone)
    return [food, wood, stone]


def villager() -> bpy.types.Object:
    value = root("Villager")
    torso = cube("VillagerTunic", (0.17, 0.12, 0.3), (0, 0, 0.58), "Tunic", value)
    ico("VillagerHead", 0.19, (0, 0, 1.05), "Skin", value)
    left_arm = cylinder("VillagerArmL", 0.055, 0.48, (-0.24, 0, 0.58), "Skin", value)
    right_arm = cylinder("VillagerArmR", 0.055, 0.48, (0.24, 0, 0.58), "Skin", value)
    left_leg = cylinder("VillagerLegL", 0.065, 0.46, (-0.1, 0, 0.23), "DarkWood", value)
    right_leg = cylinder("VillagerLegR", 0.065, 0.46, (0.1, 0, 0.23), "DarkWood", value)
    # Named actions are exported as lightweight object animation clips. Runtime
    # may substitute procedural motion while retaining this stable clip contract.
    for clip, amplitude in (("Idle", 0.02), ("Walk", 0.65), ("Carry", 0.25), ("Gather", 0.8), ("Build", 1.05), ("Socialize", 0.45), ("Rest", 0.08)):
        for obj, sign in ((left_arm, 1), (right_arm, -1), (left_leg, -1), (right_leg, 1)):
            action = bpy.data.actions.new(f"{clip}_{obj.name}")
            obj.animation_data_create()
            obj.animation_data.action = action
            obj.rotation_mode = "XYZ"
            obj.rotation_euler[1] = 0
            obj.keyframe_insert("rotation_euler", index=1, frame=1)
            obj.rotation_euler[1] = amplitude * sign
            obj.keyframe_insert("rotation_euler", index=1, frame=12)
            obj.rotation_euler[1] = 0
            obj.keyframe_insert("rotation_euler", index=1, frame=24)
            track = obj.animation_data.nla_tracks.new()
            track.name = clip
            track.strips.new(clip, 1, action)
            obj.animation_data.action = None
        if clip == "Idle":
            torso.scale.z = 1
    return value


def hierarchy(value: bpy.types.Object) -> list[bpy.types.Object]:
    result = [value]
    for child in value.children:
        result.extend(hierarchy(child))
    return result


def export(value: bpy.types.Object, filename: str) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in hierarchy(value):
        obj.select_set(True)
    bpy.context.view_layer.objects.active = value
    bpy.ops.export_scene.gltf(
        filepath=str(OUTPUT / filename),
        export_format="GLB",
        use_selection=True,
        export_yup=True,
        export_animations=True,
        export_nla_strips=True,
        export_apply=True,
    )


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    SOURCE.parent.mkdir(parents=True, exist_ok=True)
    reset()
    global MATERIALS
    MATERIALS = {
        "Clay": material("Clay", (0.49, 0.23, 0.14, 1)),
        "Thatch": material("Thatch", (0.28, 0.16, 0.08, 1)),
        "DarkWood": material("DarkWood", (0.14, 0.08, 0.045, 1)),
        "Wood": material("Wood", (0.32, 0.18, 0.08, 1)),
        "Stone": material("Stone", (0.39, 0.36, 0.32, 1)),
        "Charcoal": material("Charcoal", (0.08, 0.07, 0.065, 1)),
        "Leaf": material("Leaf", (0.22, 0.38, 0.18, 1)),
        "Pine": material("Pine", (0.12, 0.29, 0.17, 1)),
        "Berry": material("Berry", (0.48, 0.12, 0.1, 1)),
        "Skin": material("Skin", (0.68, 0.43, 0.28, 1)),
        "Tunic": material("VillagerTunic", (0.42, 0.18, 0.12, 1)),
    }
    models = [shelter(), stockpile(), workshop(), *resource_models(), villager()]
    names = ["shelter.glb", "stockpile.glb", "workshop.glb", "food.glb", "wood.glb", "stone.glb", "villager.glb"]
    for model, filename in zip(models, names, strict=True):
        export(model, filename)
    for index, model in enumerate(models):
        model.location.x = (index % 4) * 2.5
        model.location.y = -(index // 4) * 2.5
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE))
    print(f"Wrote {len(names)} GLBs to {OUTPUT} and source to {SOURCE}")


if __name__ == "__main__":
    main()
