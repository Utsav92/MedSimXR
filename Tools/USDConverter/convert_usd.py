import bpy, sys, json, mathutils
argv = sys.argv[sys.argv.index("--")+1:]
usd, out_glb, report, target_tris, tex_px = argv[0], argv[1], argv[2], int(argv[3]), int(argv[4])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.usd_import(filepath=usd, import_materials=True, import_usd_preview=True, scale=1.0)
def stats():
    objs=[o for o in bpy.context.scene.objects if o.type=='MESH']
    tris=0; mn=mathutils.Vector((1e9,)*3); mx=mathutils.Vector((-1e9,)*3)
    for o in objs:
        o.data.calc_loop_triangles(); tris+=len(o.data.loop_triangles)
        for c in o.bound_box:
            w=o.matrix_world@mathutils.Vector(c)
            mn=mathutils.Vector(map(min,mn,w)); mx=mathutils.Vector(map(max,mx,w))
    return objs,tris,mn,mx
objs,tris0,mn,mx=stats()
mats=set(); imgs={}
for o in objs:
    for s in o.material_slots:
        if s.material:
            mats.add(s.material.name)
            if s.material.node_tree:
                for n in s.material.node_tree.nodes:
                    if n.type=='TEX_IMAGE' and n.image: imgs[n.image.name]=tuple(n.image.size)
ratio=min(1.0,target_tris/max(tris0,1))
if ratio<1.0:
    for o in objs:
        bpy.context.view_layer.objects.active=o
        m=o.modifiers.new("dec","DECIMATE"); m.ratio=ratio
        bpy.ops.object.modifier_apply(modifier="dec")
for im in bpy.data.images:
    if im.source=='TILED':
        im.source='FILE'; im.filepath=im.filepath.replace('<UDIM>','1001'); im.reload()
    if im.size[0]>tex_px or im.size[1]>tex_px:
        im.scale(min(im.size[0],tex_px),min(im.size[1],tex_px))
objs,tris1,mn1,mx1=stats()
info=dict(meshes=len(objs),triangles_original=tris0,triangles_exported=tris1,decimate_ratio=ratio,
  bbox_min=list(mn),bbox_max=list(mx),size_m=list(mx-mn),materials=sorted(mats),
  textures_original_px=imgs,texture_cap_px=tex_px)
json.dump(info,open(report,'w'),indent=1)
print("INFO tris",tris0,"->",tris1,"size",list(mx-mn))
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', export_image_format='JPEG', export_jpeg_quality=80, export_apply=True)
print("EXPORTED",out_glb)

