import bpy,sys,json
from pathlib import Path
from mathutils import Vector
args=sys.argv[sys.argv.index('--')+1:]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(Path(args[0]).resolve()))
bpy.context.scene.frame_set(0)
mesh_points=[]
for obj in bpy.context.scene.objects:
    if obj.type=='MESH' and any(modifier.type=='ARMATURE' for modifier in obj.modifiers):
        evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh_points.extend(evaluated.matrix_world@vertex.co for vertex in evaluated.data.vertices)
minimum=Vector(tuple(min(point[index] for point in mesh_points) for index in range(3)))
maximum=Vector(tuple(max(point[index] for point in mesh_points) for index in range(3)))
bones=[]
root_bones=[]
for obj in bpy.context.scene.objects:
    if obj.type=='ARMATURE':
        for bone in obj.data.bones:
            head=obj.matrix_world@bone.head_local;tail=obj.matrix_world@bone.tail_local
            bones.append(dict(name=bone.name,length=(tail-head).length,head=list(head),tail=list(tail)))
            if bone.parent is None:
                root_bones.append(bones[-1])
report=dict(blender=bpy.app.version_string,mesh_bounds=[list(minimum),list(maximum)],mesh_extent=list(maximum-minimum),bone_count=len(bones),root_bones=root_bones,longest_bones=sorted(bones,key=lambda bone:bone['length'],reverse=True)[:12])
Path(args[1]).write_text(json.dumps(report,indent=2),encoding='utf-8')
print('IMPORT_REPORT',json.dumps(report))
if len(args) > 2:
    maximum_length = float(args[2])
    assert bones, 'No imported bones found'
    assert all(bone['length'] <= maximum_length for bone in bones), 'Imported rest bones exceed the supplied length limit'
if len(args) > 3:
    assert root_bones
    assert all(bone['length'] <= float(args[3]) for bone in root_bones), 'Root bone length depends on scene placement'
