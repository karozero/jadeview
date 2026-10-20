"""Verify that a synchronized cutscene GLB imports with animated facial keys and cameras."""
import json
import sys
from pathlib import Path

import bpy


arguments = sys.argv[sys.argv.index('--') + 1:]
glb_path = Path(arguments[0]).resolve()
report_path = Path(arguments[1]).resolve()
expected_morph_meshes = int(arguments[2]) if len(arguments) > 2 else 1
expected_body_rigs = int(arguments[3]) if len(arguments) > 3 else 1

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(glb_path))


def action_curves(action, slot):
    """Read Blender 5 layered actions and fall back to legacy FCurves."""
    if hasattr(action, 'layers'):
        for layer in action.layers:
            for strip in layer.strips:
                channel_bag = strip.channelbag(slot)
                if channel_bag:
                    yield from channel_bag.fcurves
        return
    yield from action.fcurves


morph_tracks = []
for mesh in (obj for obj in bpy.data.objects if obj.type == 'MESH' and obj.data.shape_keys):
    animation_data = mesh.data.shape_keys.animation_data
    if animation_data is None:
        continue
    for track in animation_data.nla_tracks:
        for strip in track.strips:
            values = [point.co.y
                      for curve in action_curves(strip.action, strip.action_slot)
                      if curve.data_path.endswith('.value')
                      for point in curve.keyframe_points]
            if not values:
                continue
            moving_curves = sum(
                1 for curve in action_curves(strip.action, strip.action_slot)
                if curve.data_path.endswith('.value') and curve.keyframe_points
                and max(point.co.y for point in curve.keyframe_points)
                - min(point.co.y for point in curve.keyframe_points) > 0.0001)
            morph_tracks.append({
                'mesh': mesh.name,
                'track': track.name,
                'action': strip.action.name,
                'keyframes': len(values),
                'minimum': min(values),
                'maximum': max(values),
                'moving_curves': moving_curves,
            })

cameras = [obj for obj in bpy.data.objects if obj.type == 'CAMERA']
animated_cameras = [obj.name for obj in cameras
                    if obj.animation_data and obj.animation_data.nla_tracks]
animated_armatures = [obj.name for obj in bpy.data.objects
                      if obj.type == 'ARMATURE' and obj.animation_data
                      and obj.animation_data.nla_tracks]
non_neutral_morph_meshes = [track['mesh'] for track in morph_tracks
                            if track['maximum'] > 0.01]
moving_morph_meshes = [track['mesh'] for track in morph_tracks if track['moving_curves'] > 0]
moving_body_rigs = []
for armature in (obj for obj in bpy.data.objects if obj.type == 'ARMATURE' and obj.animation_data):
    moving_bone_curves = 0
    for track in armature.animation_data.nla_tracks:
        for strip in track.strips:
            for curve in action_curves(strip.action, strip.action_slot):
                if not curve.data_path.startswith('pose.bones['):
                    continue
                values = [point.co.y for point in curve.keyframe_points]
                if values and max(values) - min(values) > 0.0001:
                    moving_bone_curves += 1
    if moving_bone_curves:
        moving_body_rigs.append({'armature': armature.name, 'moving_bone_curves': moving_bone_curves})
camera_tracks = {track.name for camera in cameras if camera.animation_data
                 for track in camera.animation_data.nla_tracks}
armature_tracks = {track.name for obj in bpy.data.objects if obj.type == 'ARMATURE'
                   and obj.animation_data for track in obj.animation_data.nla_tracks}
facial_track_names = {track['track'] for track in morph_tracks}
shared_tracks = sorted(camera_tracks & armature_tracks & facial_track_names)
report = {
    'blender': bpy.app.version_string,
    'facial_tracks': morph_tracks,
    'camera_count': len(cameras),
    'animated_cameras': animated_cameras,
    'animated_armatures': animated_armatures,
    'moving_body_rigs': moving_body_rigs,
    'non_neutral_morph_meshes': non_neutral_morph_meshes,
    'moving_morph_meshes': moving_morph_meshes,
    'synchronized_tracks': shared_tracks,
}
report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('CINEMATIC_IMPORT_REPORT', json.dumps(report))

assert len(non_neutral_morph_meshes) >= expected_morph_meshes, \
    f'Expected at least {expected_morph_meshes} non-neutral facial meshes, found {non_neutral_morph_meshes}'
assert animated_cameras, 'No animated source camera imported into Blender'
assert len(moving_morph_meshes) >= expected_morph_meshes, \
    f'Expected {expected_morph_meshes} faces with changing weights, found {moving_morph_meshes}'
assert animated_armatures, 'No animated actor armatures imported into Blender'
assert shared_tracks, 'Camera, armatures and facial weights did not import into a shared cutscene track'
assert len(moving_body_rigs) >= expected_body_rigs, \
    f'Expected {expected_body_rigs} rigs with actual bone motion, found {moving_body_rigs}'
