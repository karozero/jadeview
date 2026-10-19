"""Compare imported military head to source-evaluated vertices and render it."""
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree


arguments = sys.argv[sys.argv.index('--') + 1:]
glb, reference_path, report_path = map(Path, arguments[:3])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(glb.resolve()))
bpy.context.scene.frame_set(0)
bpy.context.view_layer.update()
reference = json.loads(reference_path.read_text())
head = next(obj for obj in bpy.data.objects if obj.type == 'MESH' and obj.name == reference['Head'])
tree = KDTree(len(reference['Positions']))
for index, point in enumerate(reference['Positions']):
    tree.insert(Vector((point['X'], -point['Z'], point['Y'])), index)
tree.balance()
evaluated = head.evaluated_get(bpy.context.evaluated_depsgraph_get())
positions = [evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices]
error = max(tree.find(point)[2] for point in positions)
assert error < .0001, f'Imported face differs from the evaluated source: {error}'
assert all(abs(key.value) < .000001 for key in head.data.shape_keys.key_blocks[1:])
baked = [material for material in head.data.materials if material and material.get('bakedMaskedLayers')]
assert len(baked) == 2, 'Face overlay is missing from imported eye materials'
report_path.write_text(json.dumps(dict(blender=bpy.app.version_string, head=head.name,
                                      vertices=len(positions), maximum_source_error=error,
                                      baked_eye_materials=len(baked), neutral=True), indent=2), encoding='utf-8')
print('MILITARY_FACE_REPORT', report_path.read_text())
if len(arguments) > 3:
    render_positions = positions
    if '--full-body' in arguments[4:]:
        render_positions = []
        for obj in bpy.data.objects:
            if obj.type != 'MESH':
                continue
            mesh = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
            render_positions.extend(mesh.matrix_world @ vertex.co for vertex in mesh.data.vertices)
    minimum = Vector(tuple(min(point[axis] for point in render_positions) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in render_positions) for axis in range(3)))
    center = (minimum + maximum) * .5
    bpy.ops.object.camera_add(location=center + Vector((0, -2, .03)))
    camera = bpy.context.object
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = max(maximum.x - minimum.x, maximum.z - minimum.z) * 1.25
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 768
    scene.render.resolution_y = 768
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = 'Standard'
    scene.world = bpy.data.worlds.new('Verification background')
    scene.world.color = (.1, .1, .1)
    scene.render.filepath = str(Path(arguments[3]).resolve())
    bpy.ops.render.render(write_still=True)
