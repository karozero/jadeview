"""Render four head views from a Sally GLB for material/UV inspection."""
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector

arguments = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(Path(arguments[0]).resolve()))
scene = bpy.context.scene
scene.frame_set(0)
if len(arguments) > 2:
    for obj in scene.objects:
        if obj.type != 'MESH' or not obj.data.shape_keys:
            continue
        for name in arguments[2].split(','):
            if name in obj.data.shape_keys.key_blocks:
                obj.data.shape_keys.key_blocks[name].value = 1
    bpy.context.view_layer.update()
head = next(obj for obj in scene.objects if obj.type == 'MESH' and obj.name.startswith('B sal_tete.gao'))
evaluated = head.evaluated_get(bpy.context.evaluated_depsgraph_get())
points = [evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices]
minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
center = (minimum + maximum) * .5
bpy.ops.object.camera_add()
camera = bpy.context.object
camera.data.type = 'ORTHO'
camera.data.ortho_scale = max(maximum - minimum) * 1.5
scene.camera = camera
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 8
scene.world.color = (.2, .2, .2)
scene.render.resolution_x = 384
scene.render.resolution_y = 384
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'Standard'
output = Path(arguments[1]).resolve()
output.mkdir(parents=True, exist_ok=True)
for index in range(4):
    angle = index * math.pi / 2
    camera.location = center + Vector((math.sin(angle) * 2, math.cos(angle) * 2, .02))
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(output / f'head-{index}.png')
    bpy.ops.render.render(write_still=True)
