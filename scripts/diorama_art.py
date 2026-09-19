"""Original cartoon art, authored in Blender and baked to compact painted atlases."""
import math
import bpy
import numpy as np
from mathutils import Vector

M = {}


def root(name, parent=None, pos=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent, obj.location = parent, pos
    return obj


def finish(obj, name, mat, parent):
    obj.name, obj.parent = name, parent
    obj.data.materials.append(M[mat])
    return obj


def box(name, pos, size, mat, parent, bevel=0.045):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = finish(bpy.context.object, name, mat, parent)
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    mod = obj.modifiers.new('Carved edges', 'BEVEL')
    mod.width, mod.segments = bevel, 2
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def ball(name, pos, size, mat, parent):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1, location=pos)
    obj = finish(bpy.context.object, name, mat, parent)
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for poly in obj.data.polygons: poly.use_smooth = True
    return obj


def beam(name, a, b, radius, mat, parent):
    delta = Vector(b) - Vector(a)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=radius, depth=delta.length, location=(Vector(a) + Vector(b)) / 2)
    obj = finish(bpy.context.object, name, mat, parent)
    obj.rotation_euler = delta.to_track_quat('Z', 'Y').to_euler()
    mod = obj.modifiers.new('Rounded ends', 'BEVEL')
    mod.width, mod.segments = radius * 0.2, 2
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def roof(parent, width, depth, base, mat):
    for side in (-1, 1):
        for row in range(4):
            for col in range(7):
                tile = box('RoofShingle', ((col - 3) * width / 6.4, side * (0.16 + row * depth / 8), base + 0.66 - row * 0.16), (width / 6.05, depth / 3.9, 0.12), mat if (row + col) % 3 else mat + 'Light', parent, 0.035)
                tile.rotation_euler.x = -side * 0.57
    beam('RidgeCap', (-width * 0.59, 0, base + 0.76), (width * 0.59, 0, base + 0.76), 0.1, 'Timber', parent)
    for x in (-width * 0.58, width * 0.58):
        for side in (-1, 1): beam('Fascia', (x, 0, base + 0.71), (x, side * depth * 0.56, base + 0.02), 0.08, 'Timber', parent)


def house(name, wall):
    value = root(name)
    box('Foundation', (0, 0, 0.12), (2, 1.65, 0.24), 'Stone', value, 0.08)
    box('Walls', (0, 0, 0.85), (1.72, 1.36, 1.4), wall, value, 0.1)
    for x in (-0.82, 0.82):
        for y in (-0.65, 0.65): beam('Timber', (x, y, 0.2), (x, y, 1.57), 0.095, 'Timber', value)
    for z in (0.36, 1.37): box('CrossBeam', (0, -0.71, z), (1.84, 0.12, 0.13), 'Timber', value)
    box('DoorRecess', (-0.27, -0.732, 0.72), (0.57, 0.08, 0.97), 'Dark', value)
    for x in range(4): box('DoorPlank', (-0.47 + x * 0.135, -0.79, 0.72), (0.125, 0.08, 0.87), 'Teal', value, 0.015)
    ball('Handle', (-0.09, -0.85, 0.72), (0.04, 0.03, 0.04), 'Gold', value)
    box('Step', (-0.27, -0.92, 0.17), (0.78, 0.45, 0.22), 'StoneLight', value)
    box('WindowFrame', (0.48, -0.74, 1.02), (0.48, 0.12, 0.5), 'Timber', value)
    box('Glass', (0.48, -0.81, 1.02), (0.34, 0.04, 0.34), 'Window', value)
    box('Mullion', (0.48, -0.85, 1.02), (0.05, 0.04, 0.4), 'Plaster', value, 0.01)
    box('Sill', (0.48, -0.78, 0.78), (0.6, 0.23, 0.1), 'Timber', value)
    for y in (-0.28, 0.3):
        box('SideFrame', (0.875, y, 1.03), (0.06, 0.34, 0.42), 'Timber', value)
        box('SideGlass', (0.913, y, 1.03), (0.035, 0.23, 0.29), 'Window', value)
    return value


def shelter():
    value = house('Shelter', 'Plaster')
    roof(value, 1.95, 1.95, 1.46, 'Terracotta')
    box('Chimney', (0.55, 0.25, 2.08), (0.3, 0.34, 0.85), 'Brick', value)
    box('Cap', (0.55, 0.25, 2.52), (0.42, 0.45, 0.14), 'StoneLight', value)
    box('Opening', (0.55, 0.25, 2.6), (0.23, 0.26, 0.03), 'Dark', value)
    box('Planter', (0.63, -1.02, 0.35), (0.55, 0.3, 0.25), 'Timber', value)
    for i in range(3): ball('Flowers', (0.46 + i * 0.15, -1.03, 0.56), (0.13, 0.13, 0.13), 'LeafLight' if i % 2 else 'Gold', value)
    return value


def workshop():
    value = house('Workshop', 'Brick')
    roof(value, 2.05, 2, 1.46, 'Slate')
    box('ForgeChimney', (-0.67, 0.34, 1.67), (0.48, 0.48, 2.9), 'Stone', value)
    for z in (1.5, 2.2, 3.05): box('ChimneyBand', (-0.67, 0.34, z), (0.59, 0.59, 0.13), 'StoneLight', value)
    box('Opening', (-0.67, 0.34, 3.13), (0.33, 0.33, 0.03), 'Dark', value)
    for x in (0.05, 1.25): beam('AwningPost', (x, -1.43, 0.15), (x, -1.43, 1.3), 0.065, 'Timber', value)
    for i in range(6):
        stripe = box('AwningStripe', (0.1 + i * 0.23, -1.1, 1.42), (0.23, 0.94, 0.065), 'Gold' if i % 2 else 'Plaster', value, 0.02)
        stripe.rotation_euler.x = 0.2
    box('AnvilStump', (0.64, -1.36, 0.37), (0.47, 0.44, 0.6), 'Timber', value)
    box('AnvilWaist', (0.64, -1.36, 0.76), (0.28, 0.25, 0.28), 'Iron', value)
    box('AnvilTop', (0.64, -1.36, 0.91), (0.77, 0.34, 0.16), 'Iron', value)
    return value


def stockpile():
    value = root('Stockpile')
    box('Footing', (0, 0, 0.1), (2, 1.75, 0.2), 'Stone', value)
    for i in range(8): box('DeckPlank', (-0.84 + i * 0.24, 0, 0.25), (0.23, 1.65, 0.14), 'Wood', value, 0.025)
    for x in (-0.86, 0.86):
        for y in (-0.67, 0.67): beam('Post', (x, y, 0.3), (x, y, 1.67), 0.1, 'Timber', value)
    roof(value, 2, 2, 1.6, 'Thatch')
    for i in range(5):
        x, z = -0.36 + (i % 3) * 0.28, 0.46 + (i // 3) * 0.26
        beam('Log', (x, -0.52, z), (x, 0.55, z), 0.14, 'Wood', value)
        beam('LogEnd', (x, -0.55, z), (x, -0.57, z), 0.115, 'ThatchLight', value)
    for x, y in ((0.49, -0.32), (0.45, 0.3)):
        box('Crate', (x, y, 0.55), (0.47, 0.48, 0.5), 'Wood', value)
        for z in (0.37, 0.7): box('Strap', (x, y - 0.25, z), (0.5, 0.035, 0.055), 'Timber', value, 0.01)
    ball('Sack', (-0.78, -1, 0.36), (0.25, 0.26, 0.34), 'ThatchLight', value)
    return value


def resources():
    tree = root('WoodCluster')
    beam('Trunk', (0, 0, 0), (0.03, 0, 1.9), 0.19, 'Timber', tree)
    for angle in range(0, 360, 90):
        a = math.radians(angle)
        beam('Root', (0, 0, 0.2), (math.cos(a) * 0.4, math.sin(a) * 0.4, 0.035), 0.09, 'Timber', tree)
    for i, (x, y, z, s) in enumerate(((-0.45, 0, 1.55, 0.65), (0.43, 0.1, 1.65, 0.67), (0, -0.38, 1.87, 0.67), (0, 0.35, 2.08, 0.64), (-0.12, 0, 2.4, 0.59))):
        ball('Canopy', (x, y, z), (s, s * 0.88, s * 0.8), 'LeafLight' if i > 2 else 'Leaf', tree)
    food = root('FoodCluster')
    for i, (x, y) in enumerate(((-0.22, 0.05), (0.25, 0.1), (0, -0.18))):
        ball('Bush', (x, y, 0.3), (0.31, 0.3, 0.32), 'Leaf', food)
        for j in range(3):
            angle = j * 2.1 + i
            ball('Berry', (x + math.cos(angle) * 0.22, y + math.sin(angle) * 0.22, 0.5), (0.075, 0.075, 0.08), 'Berry', food)
    stone = root('StoneCluster')
    for i, (x, y, s) in enumerate(((-0.25, 0.02, 0.35), (0.19, 0.12, 0.45), (0.05, -0.27, 0.26))):
        rock = box('RoundedStone', (x, y, s * 0.48), (s * 1.5, s * 1.3, s), 'StoneLight' if i % 2 else 'Stone', stone, 0.13)
        rock.rotation_euler.z = i * 0.7
    return [food, tree, stone]


def villager():
    value = root('Villager')
    ball('Tunic', (0, 0, 0.63), (0.25, 0.19, 0.3), 'Tunic', value)
    box('Belt', (0, -0.01, 0.48), (0.47, 0.36, 0.08), 'Timber', value)
    box('Buckle', (0, -0.2, 0.48), (0.1, 0.04, 0.1), 'Gold', value, 0.02)
    ball('Head', (0, -0.015, 1.11), (0.28, 0.235, 0.29), 'Skin', value)
    for x in (-0.28, 0.28): ball('Ear', (x, 0, 1.1), (0.065, 0.07, 0.1), 'Skin', value)
    ball('HairCap', (0, 0.025, 1.3), (0.285, 0.237, 0.17), 'Hair', value)
    for x in (-0.16, 0, 0.15): ball('HairLock', (x, -0.17, 1.32), (0.12, 0.13, 0.105), 'Hair', value)
    ball('Nose', (0, -0.25, 1.075), (0.07, 0.08, 0.075), 'Skin', value)
    for x in (-0.105, 0.105):
        ball('Eye', (x, -0.219, 1.14), (0.062, 0.027, 0.073), 'Cream', value)
        ball('Pupil', (x, -0.244, 1.14), (0.028, 0.018, 0.043), 'Dark', value)
        box('Brow', (x, -0.232, 1.233), (0.12, 0.04, 0.032), 'Hair', value, 0.012)
    box('Smile', (0, -0.232, 0.987), (0.09, 0.028, 0.021), 'Hair', value, 0.01)
    limbs = []
    for side, x in (('L', -0.29), ('R', 0.29)):
        arm = root('VillagerArm' + side, value, (x, 0, 0.81))
        ball('Sleeve', (0, 0, -0.045), (0.115, 0.115, 0.16), 'Tunic', arm)
        ball('Forearm', (0, 0, -0.21), (0.085, 0.085, 0.15), 'Skin', arm)
        ball('Hand', (0, -0.01, -0.33), (0.105, 0.095, 0.115), 'Skin', arm)
        leg = root('VillagerLeg' + side, value, (x * 0.4, 0, 0.4))
        ball('Trousers', (0, 0, -0.13), (0.09, 0.09, 0.2), 'Slate', leg)
        box('Boot', (0, -0.065, -0.32), (0.19, 0.31, 0.16), 'Timber', leg, 0.055)
        limbs.extend([arm, leg])
    # A single rigid-weighted skinned mesh keeps a village of twenty actors
    # affordable. Every vertex follows one body/limb bone; no deformation drift.
    bpy.ops.object.armature_add(enter_editmode=True, location=(0, 0, 0))
    rig = bpy.context.object
    rig.name = 'VillagerRig'
    body_bone = rig.data.edit_bones[0]
    body_bone.name = 'Body'
    body_bone.head, body_bone.tail = (0, 0, 0), (0, 0, 0.4)
    for limb in limbs:
        bone = rig.data.edit_bones.new(limb.name)
        bone.head = limb.location
        bone.tail = limb.location + Vector((0, 0, -0.4))
        bone.parent = body_bone
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.parent = value
    bpy.context.view_layer.update()
    for obj in [obj for obj in hierarchy(value) if obj.type == 'MESH']:
        bone_name = obj.parent.name if obj.parent in limbs else 'Body'
        world = obj.matrix_world.copy()
        obj.parent = value
        obj.matrix_world = world
        weights = obj.vertex_groups.new(name=bone_name)
        weights.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
        modifier = obj.modifiers.new('Rigid character rig', 'ARMATURE')
        modifier.object = rig
    limb_names = [limb.name for limb in limbs]
    for limb in limbs: bpy.data.objects.remove(limb, do_unlink=True)
    for clip in ('Idle', 'Walk', 'Carry', 'Gather', 'Build', 'Socialize', 'Rest'):
        action = bpy.data.actions.new(clip + '_Villager')
        rig.animation_data_create()
        rig.animation_data.action = action
        for name in limb_names:
            bone = rig.pose.bones[name]
            bone.rotation_mode = 'XYZ'
            arm, sign = 'Arm' in name, 1 if name.endswith('L') else -1
            if clip == 'Walk': values = [0, 0.65 * sign * (1 if arm else -1), 0, -0.65 * sign * (1 if arm else -1), 0]
            elif clip == 'Carry': values = [-1.05] * 5 if arm else [0, 0.35 * sign, 0, -0.35 * sign, 0]
            elif clip in ('Gather', 'Build'): values = [-0.4, -1.8, -0.4, -1.8, -0.4] if arm else [0] * 5
            elif clip == 'Socialize': values = [-0.3, -1.1, -0.7, -1.3, -0.3] if arm else [0] * 5
            elif clip == 'Rest': values = [-0.3] * 5 if arm else [-1.25] * 5
            else: values = [0, 0.035, 0, -0.035, 0]
            for frame, angle in zip((1, 7, 13, 19, 25), values):
                bone.rotation_euler.x = angle
                bone.keyframe_insert('rotation_euler', index=0, frame=frame)
        track = rig.animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, 1, action)
        strip.extrapolation = 'NOTHING'
        rig.animation_data.action = None
        for bone in rig.pose.bones: bone.rotation_euler = (0, 0, 0)
    return value


def hierarchy(obj):
    return [obj] + [desc for child in obj.children for desc in hierarchy(child)]


def bake_atlas(model):
    meshes = [obj for obj in hierarchy(model) if obj.type == 'MESH']
    for parent in list(dict.fromkeys(obj.parent for obj in meshes)):
        group = [obj for obj in parent.children if obj.type == 'MESH']
        bpy.ops.object.select_all(action='DESELECT')
        for obj in group: obj.select_set(True)
        bpy.context.view_layer.objects.active = group[0]
        bpy.ops.object.join()
    meshes = [obj for obj in hierarchy(model) if obj.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes: obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.018)
    bpy.ops.object.mode_set(mode='OBJECT')
    size = 1024 if model.name in ('Shelter', 'Workshop', 'Stockpile') else 512
    color = bpy.data.images.new(model.name + '_Paint', width=size, height=size)
    ao = bpy.data.images.new(model.name + '_AO', width=size, height=size)
    ao.colorspace_settings.name = 'Non-Color'
    mats = {slot.material for obj in meshes for slot in obj.material_slots}
    bake_nodes = []
    for mat in mats:
        node = mat.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = color
        mat.node_tree.nodes.active = node
        bake_nodes.append(node)
    scene = bpy.context.scene
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    scene.render.bake.margin = 8
    bpy.ops.object.bake(type='DIFFUSE')
    for node in bake_nodes: node.image = ao
    bpy.ops.object.bake(type='AO')
    pixels = np.array(color.pixels[:], dtype=np.float32).reshape(-1, 4)
    occlusion = np.array(ao.pixels[:], dtype=np.float32).reshape(-1, 4)
    pixels[:, :3] *= 0.55 + 0.45 * occlusion[:, :3]
    pixels[:, 3] = 1
    color.pixels.foreach_set(pixels.ravel())
    color.pack()
    atlas = bpy.data.materials.new(model.name + '_PaintedAtlas')
    atlas.use_nodes = True
    shader = atlas.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value = 0.83
    tex = atlas.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = color
    atlas.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])
    for obj in meshes:
        obj.data.materials.clear()
        obj.data.materials.append(atlas)
        for poly in obj.data.polygons: poly.material_index = 0
    for mat in mats:
        for node in list(mat.node_tree.nodes):
            if node in bake_nodes: mat.node_tree.nodes.remove(node)
    bpy.data.images.remove(ao)


def build(output, source):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    palette = {
        'Plaster': (0.88, 0.66, 0.36), 'Cream': (0.95, 0.91, 0.74), 'Timber': (0.22, 0.085, 0.025), 'Wood': (0.49, 0.26, 0.07),
        'Stone': (0.28, 0.35, 0.39), 'StoneLight': (0.48, 0.56, 0.56), 'Terracotta': (0.65, 0.115, 0.028), 'TerracottaLight': (0.85, 0.23, 0.045),
        'Slate': (0.055, 0.22, 0.32), 'SlateLight': (0.08, 0.35, 0.46), 'Thatch': (0.64, 0.36, 0.065), 'ThatchLight': (0.88, 0.63, 0.2),
        'Brick': (0.57, 0.25, 0.11), 'Dark': (0.025, 0.019, 0.018), 'Teal': (0.035, 0.32, 0.34), 'Window': (0.13, 0.5, 0.63),
        'Gold': (0.95, 0.56, 0.06), 'Iron': (0.13, 0.19, 0.24), 'Leaf': (0.13, 0.4, 0.025), 'LeafLight': (0.32, 0.62, 0.055),
        'Berry': (0.7, 0.025, 0.07), 'Skin': (0.84, 0.46, 0.24), 'Hair': (0.18, 0.055, 0.02), 'Tunic': (0.08, 0.36, 0.54),
    }
    for name, color in palette.items():
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (*color, 1)
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        bsdf = nodes.get('Principled BSDF')
        bsdf.inputs['Roughness'].default_value = 0.82
        noise = nodes.new('ShaderNodeTexNoise')
        noise.inputs['Scale'].default_value = 7
        noise.inputs['Detail'].default_value = 2
        ramp = nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].position = 0.15
        ramp.color_ramp.elements[0].color = (*(v * 0.78 for v in color), 1)
        ramp.color_ramp.elements[1].position = 0.85
        ramp.color_ramp.elements[1].color = (*(min(1, v * 1.14) for v in color), 1)
        links.new(noise.outputs['Fac'], ramp.inputs[0])
        links.new(ramp.outputs[0], bsdf.inputs['Base Color'])
        M[name] = mat
    bpy.context.scene.render.engine = 'CYCLES'
    bpy.context.scene.cycles.samples = 12
    bpy.context.scene.render.fps = 24
    bpy.context.preferences.filepaths.save_version = 0
    models = [shelter(), stockpile(), workshop(), *resources(), villager()]
    bpy.context.scene.frame_set(0)
    for i, model in enumerate(models): model.location.x = i * 6
    for model, name in zip(models, ('shelter', 'stockpile', 'workshop', 'food', 'wood', 'stone', 'villager')):
        print('Baking ' + name, flush=True)
        bake_atlas(model)
        saved = model.location.copy()
        model.location = (0, 0, 0)
        bpy.context.view_layer.update()
        objects = hierarchy(model)
        # glTF skinned meshes must be scene roots; their skeleton still retains
        # the stable Villager root. Bake the mesh transform before detaching.
        detached = []
        for obj in objects:
            if obj.type == 'MESH' and any(mod.type == 'ARMATURE' for mod in obj.modifiers):
                world = obj.matrix_world.copy()
                obj.parent = None
                obj.matrix_world = world
                bpy.ops.object.select_all(action='DESELECT')
                obj.select_set(True)
                bpy.context.view_layer.objects.active = obj
                bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
                detached.append(obj)
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objects: obj.select_set(True)
        bpy.context.view_layer.objects.active = model
        bpy.ops.export_scene.gltf(filepath=str(output / (name + '.glb')), export_format='GLB', use_selection=True, export_yup=True, export_animations=True, export_animation_mode='NLA_TRACKS', export_optimize_animation_size=False, export_optimize_animation_keep_anim_object=True, export_apply=True)
        for obj in detached: obj.parent = model
        model.location = saved
    bpy.ops.wm.save_as_mainfile(filepath=str(source))
