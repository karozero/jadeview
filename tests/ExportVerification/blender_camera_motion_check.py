"""Verify routed camera translation and orientation after a real Blender import."""
import json
import math
import struct
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


arguments = sys.argv[sys.argv.index('--') + 1:]
path, report_path = map(Path, arguments[:2])
with path.open('rb') as stream:
    stream.seek(12)
    length, _ = struct.unpack('<II', stream.read(8))
    document = json.loads(stream.read(length))
    length, _ = struct.unpack('<II', stream.read(8))
    binary = stream.read(length)


def values(index):
    accessor = document['accessors'][index]
    view = document['bufferViews'][accessor['bufferView']]
    offset = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[accessor['type']]
    return [struct.unpack_from('<' + 'f' * width, binary, offset + sample * width * 4)
            for sample in range(accessor['count'])]


bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(path.resolve()))
fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
correction = Quaternion((math.sqrt(.5), math.sqrt(.5), 0, 0))
results = []
for animation_index, animation in enumerate(document.get('animations', [])):
    track_name = animation.get('name') or f'Anim_{animation_index}'
    channels = {(channel['target'].get('node'), channel['target']['path']): channel
                for channel in animation['channels']}
    for node_index, node in enumerate(document['nodes']):
        if 'cameraReceiverKey' not in node.get('extras', {}):
            continue
        position_channel = channels.get((node_index, 'translation'))
        rotation_channel = channels.get((node_index, 'rotation'))
        if position_channel is None or rotation_channel is None:
            continue
        camera = bpy.data.objects[node['name']]
        selected_track = next(track for track in camera.animation_data.nla_tracks if track.name == track_name)
        selected_strip = selected_track.strips[0]
        camera.animation_data.action = selected_strip.action
        camera.animation_data.action_slot = selected_strip.action_slot
        for track in camera.animation_data.nla_tracks:
            track.mute = True
        position_sampler = animation['samplers'][position_channel['sampler']]
        rotation_sampler = animation['samplers'][rotation_channel['sampler']]
        times = values(position_sampler['input'])
        positions = values(position_sampler['output'])
        rotations = values(rotation_sampler['output'])
        assert len(times) == len(rotations), 'Camera samplers use different clocks'
        errors = []
        for sample in sorted({0, len(times) // 4, len(times) // 2, len(times) * 3 // 4, len(times) - 1}):
            frame = times[sample][0] * fps
            bpy.context.scene.frame_set(math.floor(frame), subframe=frame - math.floor(frame))
            position = positions[sample]
            expected_position = Vector((position[0], -position[2], position[1]))
            rotation = rotations[sample]
            expected_rotation = Quaternion((rotation[3], rotation[0], -rotation[2], rotation[1])) @ correction
            actual = camera.matrix_world
            distance = (actual.translation - expected_position).length
            angle = actual.to_quaternion().rotation_difference(expected_rotation).angle
            angle = min(angle, abs(2 * math.pi - angle))
            assert distance < .005 and angle < .002, (camera.name, frame, distance, angle,
                tuple(actual.to_quaternion()), tuple(expected_rotation),
                [(track.name, [(strip.frame_start, strip.action_frame_start, strip.scale) for strip in track.strips])
                 for track in camera.animation_data.nla_tracks])
            errors.append({'time': times[sample][0], 'position_error': distance, 'angle_error': angle})
        results.append({'camera': camera.name, 'track': animation['name'], 'samples': errors})

assert results, 'No routed camera animation imported'
report_path.write_text(json.dumps(results, indent=2), encoding='utf-8')
print('CAMERA_MOTION_IMPORT_REPORT', json.dumps(results))
