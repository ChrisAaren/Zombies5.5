"""Proof renders for Char_Human (Eevee): turnaround, face close-ups, curled hands, pose tests, gameplay camera, accessories.

Builds the character in memory (identical to the exported FBX) with existing textures, then renders to previews/Human_*.png.
Run: blender --background --factory-startup --python make_human_previews.py [-- only_substring]
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix, Quaternion

import make_human as MH
import make_human_accessories as MA
import hr_render as R
import bl_skeleton

P = os.path.join(MH.ROOT, "Tools", "Blender", "previews")


def pb_dir(rig, name):
    pb = rig.pose.bones[name]
    return ((rig.matrix_world @ pb.tail) - (rig.matrix_world @ pb.head)).normalized()


def rot_about_head(rig, name, axis, deg):
    pb = rig.pose.bones[name]
    bpy.context.view_layer.update()
    head = pb.head.copy()
    R_ = Matrix.Rotation(math.radians(deg), 4, Vector(axis).normalized())
    T = Matrix.Translation(head)
    pb.matrix = T @ R_ @ T.inverted() @ pb.matrix
    bpy.context.view_layer.update()


def aim(rig, name, target_dir):
    cur = pb_dir(rig, name)
    q = cur.rotation_difference(Vector(target_dir).normalized())
    pb = rig.pose.bones[name]
    bpy.context.view_layer.update()
    head = pb.head.copy()
    T = Matrix.Translation(head)
    pb.matrix = T @ q.to_matrix().to_4x4() @ T.inverted() @ pb.matrix
    bpy.context.view_layer.update()


def reset_pose(rig):
    for pb in rig.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def ik2(rig, upper, lower, hand_pos, pole):
    """Analytic two-bone IK: aims `upper` at the elbow and `lower` at the target."""
    bpy.context.view_layer.update()
    a = rig.pose.bones[upper].head.copy()
    L1 = (rig.pose.bones[upper].tail - rig.pose.bones[upper].head).length
    L2 = (rig.pose.bones[lower].tail - rig.pose.bones[lower].head).length
    t = Vector(hand_pos) - a
    d = min(t.length, (L1 + L2) * 0.999)
    td = t.normalized()
    cosA = (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d)
    ang = math.acos(max(-1, min(1, cosA)))
    pv = (Vector(pole) - td * Vector(pole).dot(td)).normalized()
    elbow = a + td * (L1 * math.cos(ang)) + pv * (L1 * math.sin(ang))
    aim(rig, upper, elbow - a)
    aim(rig, lower, Vector(hand_pos) - elbow)


def curl_fingers(rig, side, amount=1.0):
    sgn = 1 if side == "Left" else -1
    for f in ("Index", "Middle", "Ring", "Little"):
        for part, deg in (("Proximal", 70), ("Intermediate", 95), ("Distal", 60)):
            rot_about_head(rig, side + f + part, (0, 1, 0), sgn * deg * amount)
    # thumb: fold across the palm
    rot_about_head(rig, side + "ThumbProximal", (0, 1, 0), sgn * 10 * amount)
    rot_about_head(rig, side + "ThumbProximal", (0, 0, 1), sgn * -18 * amount)
    rot_about_head(rig, side + "ThumbIntermediate", (0, 0, 1), sgn * -28 * amount)
    rot_about_head(rig, side + "ThumbDistal", (0, 0, 1), sgn * -18 * amount)


# ------------------------------------------------------------------ poses
def pose_elbows(rig):
    for sd, sx in (("Left", 1), ("Right", -1)):
        aim(rig, sd + "UpperArm", (sx * 0.12, 0.02, -1))
        aim(rig, sd + "LowerArm", (sx * 0.05, -math.sin(math.radians(100)), -math.cos(math.radians(100))))
        aim(rig, sd + "Hand", (sx * 0.05, -math.sin(math.radians(100)), -math.cos(math.radians(100))))
        curl_fingers(rig, sd, 0.35)


def relax_arms(rig):
    for sd, sx in (("Left", 1), ("Right", -1)):
        aim(rig, sd + "UpperArm", (sx * 0.12, 0.0, -1))
        aim(rig, sd + "LowerArm", (sx * 0.10, -0.15, -1))
        aim(rig, sd + "Hand", (sx * 0.10, -0.15, -1))
        curl_fingers(rig, sd, 0.3)


def pose_crouch(rig):
    hip = rig.pose.bones["Hips"]
    phi, kap = 65, 90
    dz = -0.285
    dy = 0.20
    hip.location = rig.pose.bones["Hips"].bone.matrix_local.inverted().to_3x3() @ Vector((0, dy, dz))
    bpy.context.view_layer.update()
    for sd in ("Left", "Right"):
        aim(rig, sd + "UpperLeg", (0, -math.sin(math.radians(phi)), -math.cos(math.radians(phi))))
        sh = math.radians(phi - kap)
        aim(rig, sd + "LowerLeg", (0, -math.sin(sh), -math.cos(sh)))
        aim(rig, sd + "Foot", (0, -1, -0.12))
        aim(rig, sd + "Toes", (0, -1, -0.1))
    rot_about_head(rig, "Spine", (1, 0, 0), 14)
    rot_about_head(rig, "Chest", (1, 0, 0), 10)
    for sd, sx in (("Left", 1), ("Right", -1)):
        aim(rig, sd + "UpperArm", (sx * 0.15, -0.55, -0.8))
        aim(rig, sd + "LowerArm", (sx * 0.1, -0.85, -0.45))
        aim(rig, sd + "Hand", (sx * 0.1, -0.85, -0.45))
        curl_fingers(rig, sd, 0.45)


def pose_arms_fwd(rig):
    for sd, sx in (("Left", 1), ("Right", -1)):
        for b in ("UpperArm", "LowerArm", "Hand"):
            aim(rig, sd + b, (sx * 0.06, -1, 0.0))
        curl_fingers(rig, sd, 0.15)


def pose_arms_up(rig):
    for sd, sx in (("Left", 1), ("Right", -1)):
        v = (sx * math.sin(math.radians(120)), 0.0, -math.cos(math.radians(120)))
        for b in ("UpperArm", "LowerArm", "Hand"):
            aim(rig, sd + b, v)
        curl_fingers(rig, sd, 0.1)


def pose_head(rig):
    rot_about_head(rig, "Neck", (0, 0, 1), 18)
    rot_about_head(rig, "Head", (0, 0, 1), 42)
    for sd, sx in (("Left", 1), ("Right", -1)):
        aim(rig, sd + "UpperArm", (sx * 0.16, 0.0, -1))
        aim(rig, sd + "LowerArm", (sx * 0.12, -0.15, -1))
        aim(rig, sd + "Hand", (sx * 0.12, -0.15, -1))
        curl_fingers(rig, sd, 0.3)


def rifle_proxy():
    me = bpy.data.meshes.new("rifle")
    bm = bmesh.new()
    def box(c, s):
        bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(c) @ Matrix.Diagonal((s[0], s[1], s[2], 1)))
    box((-0.115, -0.46, 1.455), (0.05, 0.86, 0.075))       # receiver + barrel block
    box((-0.115, -0.93, 1.468), (0.022, 0.32, 0.022))      # barrel
    box((-0.115, -0.02, 1.435), (0.06, 0.22, 0.10))        # stock
    box((-0.115, -0.26, 1.375), (0.035, 0.05, 0.12))       # grip/mag
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("rifle", me)
    bpy.context.scene.collection.objects.link(o)
    mt = bpy.data.materials.new("rm"); mt.use_nodes = True
    mt.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.05, 0.055, 1)
    mt.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.45
    me.materials.append(mt)
    return o


def pose_rifle(rig):
    rot_about_head(rig, "Chest", (0, 0, 1), 16)
    rot_about_head(rig, "Head", (0, 0, 1), -14)
    rot_about_head(rig, "Neck", (0, 0, 1), -6)
    # right hand (character right = -X) on the grip, left hand under the handguard
    ik2(rig, "RightUpperArm", "RightLowerArm", (-0.115, -0.25, 1.395), (-0.3, 0.3, -1))
    ik2(rig, "LeftUpperArm", "LeftLowerArm", (-0.115, -0.58, 1.42), (0.4, 0.0, -1))
    aim(rig, "RightHand", (0, -1, -0.15))
    aim(rig, "LeftHand", (-0.3, -1, 0.0))
    curl_fingers(rig, "Right", 0.85)
    curl_fingers(rig, "Left", 0.7)


def look(path, az, el, dist, target, size=(900, 900), lens=70, floor=True, hide=(), light=1.0):
    R.shot(path, az, el, dist, target, lens=lens, size=size, floor=floor, light_scale=light, hide=hide)


def attach(ob, rig, bone):
    ob.parent = None
    ob.matrix_parent_inverse = Matrix.Identity(4)
    ob.location = MA.PIVOTS[ob.name]
    ob.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    basis = ob.matrix_basis.copy()
    ob.parent = rig
    ob.parent_type = 'BONE'
    ob.parent_bone = bone
    ob.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    P = ob.matrix_world @ basis.inverted()
    ob.matrix_parent_inverse = P.inverted()
    bpy.context.view_layer.update()


def compare(new_path, old_path, out):
    a = bpy.data.images.load(new_path); b = bpy.data.images.load(old_path)
    def arr(img):
        w, h = img.size
        return np.array(img.pixels[:], np.float32).reshape(h, w, 4)
    A, B = arr(a), arr(b)
    h = min(A.shape[0], B.shape[0])
    def crop(X):
        d = X.shape[0] - h
        return X[d // 2: d // 2 + h]
    A, B = crop(A), crop(B)
    gap = np.ones((h, 20, 4), np.float32) * 0.1
    C = np.concatenate([B, gap, A], axis=1)
    img = bpy.data.images.new("cmp", C.shape[1], C.shape[0])
    img.pixels.foreach_set(C.ravel())
    img.filepath_raw = out
    img.file_format = 'PNG'
    img.save()


def main():
    only = ""
    if "--" in sys.argv:
        rest = sys.argv[sys.argv.index("--") + 1:]
        only = rest[0] if rest else ""
    obj, rig = MH.build_character(bake_textures=False, export=False)
    MH.stats(obj)
    accs = {a.name: a for a in MA.build_all(obj)}
    for a in accs.values():
        a.hide_render = True
    base_objs = [obj]

    def ok(name):
        return only in name

    def show(*names):
        for n, a in accs.items():
            a.hide_render = n not in names

    # ------------------------------------------------ turnaround
    for az, nm in ((0, "Front"), (40, "3Q"), (90, "Side"), (180, "Back")):
        if ok("Turn"):
            look(os.path.join(P, "Human_Turn_%s.png" % nm), az, 5, 5.2, (0, 0, 0.92), size=(700, 1000), lens=75)
    # ------------------------------------------------ face
    if ok("Face"):
        relax_arms(rig)
        look(os.path.join(P, "Human_Face_Front.png"), 0, 2, 0.95, (0, -0.02, 1.685), size=(900, 900), lens=85, floor=False)
        look(os.path.join(P, "Human_Face_3Q.png"), 35, 3, 0.95, (0, -0.02, 1.685), size=(900, 900), lens=85, floor=False)
        look(os.path.join(P, "Human_Face_Profile.png"), 90, 2, 0.95, (0, -0.02, 1.685), size=(900, 900), lens=85, floor=False)
        look(os.path.join(P, "Human_Face_Eyes.png"), 18, 2, 0.34, (0, -0.075, 1.70), size=(1000, 600), lens=85, floor=False)
        reset_pose(rig)
    # ------------------------------------------------ hands
    if ok("Hands"):
        # open hand (rest) and curled
        look(os.path.join(P, "Human_Hand_Open.png"), 0, 62, 0.55, (0.80, -0.01, 1.44), size=(900, 700), lens=70, floor=False)
        reset_pose(rig)
        curl_fingers(rig, "Left", 1.0)
        look(os.path.join(P, "Human_Hands_Curled.png"), 15, 48, 0.5, (0.80, -0.04, 1.43), size=(900, 700), lens=70, floor=False)
        look(os.path.join(P, "Human_Hands_Curled_Palm.png"), 0, -40, 0.5, (0.80, -0.04, 1.43), size=(900, 700), lens=70, floor=False)
        reset_pose(rig)
    # ------------------------------------------------ topology
    if ok("Topology"):
        w = R.wire_copy(obj, 0.00018)
        obj.hide_render = True
        look(os.path.join(P, "Human_Topology_Face.png"), 25, 3, 0.8, (0, -0.02, 1.685), size=(1000, 900), lens=85, floor=False)
        look(os.path.join(P, "Human_Topology_Hand.png"), 10, 70, 0.5, (0.80, -0.02, 1.44), size=(1000, 800), lens=70, floor=False)
        look(os.path.join(P, "Human_Topology_Body.png"), 30, 8, 2.8, (0, 0, 1.2), size=(1000, 1000), lens=70, floor=False)
        obj.hide_render = False
        bpy.data.objects.remove(w, do_unlink=True)
    # ------------------------------------------------ pose tests
    poses = [("Elbows100", pose_elbows, 40, (0, -0.05, 1.12), 2.8), ("Crouch90", pose_crouch, 90, (0, 0.0, 0.72), 3.2),
             ("ArmsFwd90", pose_arms_fwd, 40, (0, -0.2, 1.1), 3.4), ("ArmsUp120", pose_arms_up, 0, (0, 0, 1.2), 3.8),
             ("Head60", pose_head, 20, (0, 0, 1.35), 2.2), ("RifleAim", pose_rifle, 50, (0, -0.3, 1.2), 3.2)]
    for nm, fn, az, tg, dist in poses:
        if not ok("Pose"):
            break
        reset_pose(rig)
        extra = rifle_proxy() if nm == "RifleAim" else None
        fn(rig)
        look(os.path.join(P, "Human_Pose_%s_A.png" % nm), az, 6, dist, tg, size=(900, 900), lens=70)
        az2 = {40: 125, 90: 0, 0: 50, 20: 70, 50: 130}.get(az, az + 80)
        look(os.path.join(P, "Human_Pose_%s_B.png" % nm), az2, 6, dist, tg, size=(900, 900), lens=70)
        if extra is not None:
            bpy.data.objects.remove(extra, do_unlink=True)
    reset_pose(rig)
    # shoulders close-up with arms raised (deformation check)
    if ok("Pose"):
        pose_arms_up(rig)
        look(os.path.join(P, "Human_Pose_ArmsUp120_Shoulders.png"), 20, 10, 1.4, (0, 0, 1.4), size=(1000, 800), lens=70)
        reset_pose(rig)
        pose_arms_fwd(rig)
        look(os.path.join(P, "Human_Pose_ArmsFwd90_Shoulders.png"), 60, 10, 1.3, (0, -0.1, 1.4), size=(1000, 800), lens=70)
        reset_pose(rig)
    # ------------------------------------------------ gameplay camera: ~4 m behind and above, 3/4
    if ok("Game"):
        for a in accs.values():
            a.hide_render = True
        reset_pose(rig); relax_arms(rig)
        for sd, sx in (("Left", 1), ("Right", -1)):      # idle stance: slight stride
            pass
        for nm_, accn in (("", ()), ("_Cap_Pack", ("Char2_Cap", "Char2_Backpack"))):
            show(*accn)
            for n in accn:
                attach(accs[n], rig, "Head" if n != "Char2_Backpack" else "Chest")
            look(os.path.join(P, "Human_Game_Cam%s.png" % nm_), 150, 25, 4.0, (0, 0, 0.95), size=(900, 1100), lens=45, light=1.0)
            look(os.path.join(P, "Human_Game_Cam_Front%s.png" % nm_), 205, 22, 4.0, (0, 0, 0.95), size=(900, 1100), lens=45)
            for n in accn:
                accs[n].parent = None
    # ------------------------------------------------ accessories
    if ok("Acc"):
        for n, a in accs.items():
            if a.parent is None:
                attach(a, rig, "Head" if n != "Char2_Backpack" else "Chest")
        for n in ("Char2_HairShort", "Char2_HairLong", "Char2_Cap", "Char2_Hood"):
            show(n)
            look(os.path.join(P, "Human_Acc_%s_3Q.png" % n[6:]), 35, 6, 0.95, (0, 0.0, 1.66), size=(900, 900), lens=75, floor=False)
            look(os.path.join(P, "Human_Acc_%s_Side.png" % n[6:]), 105, 6, 0.95, (0, 0.0, 1.66), size=(900, 900), lens=75, floor=False)
            if n == "Char2_Hood" or n == "Char2_HairLong":
                look(os.path.join(P, "Human_Acc_%s_Back.png" % n[6:]), 180, 8, 1.1, (0, 0.0, 1.55), size=(900, 900), lens=75, floor=False)
        show("Char2_Backpack")
        look(os.path.join(P, "Human_Acc_Backpack_Back.png"), 180, 8, 2.2, (0, 0.05, 1.25), size=(900, 900), lens=70, floor=False)
        look(os.path.join(P, "Human_Acc_Backpack_Front.png"), 0, 8, 2.2, (0, 0.0, 1.25), size=(900, 900), lens=70, floor=False)
        look(os.path.join(P, "Human_Acc_Backpack_Side.png"), 90, 8, 2.2, (0, 0.05, 1.25), size=(900, 900), lens=70, floor=False)
        show("Char2_Cap", "Char2_Backpack")
        look(os.path.join(P, "Human_Acc_Courier_3Q.png"), 40, 8, 4.0, (0, 0, 0.95), size=(800, 1000), lens=60)
        reset_pose(rig); relax_arms(rig)
        look(os.path.join(P, "Human_Acc_Courier_Relaxed.png"), 35, 6, 3.4, (0, 0, 0.95), size=(800, 1000), lens=60)
        show("Char2_HairShort")
        look(os.path.join(P, "Human_Acc_Courier_Relaxed_Hair.png"), 35, 6, 3.4, (0, 0, 0.95), size=(800, 1000), lens=60)
        reset_pose(rig)
    if ok("Turn"):
        old = os.path.join(P, "Char_Body_front.png")
        if os.path.exists(old):
            compare(os.path.join(P, "Human_Turn_Front.png"), old, os.path.join(P, "Human_vs_OldBody_Front.png"))
        old = os.path.join(P, "Char_Body_head.png")
        if os.path.exists(old) and os.path.exists(os.path.join(P, "Human_Face_3Q.png")):
            compare(os.path.join(P, "Human_Face_3Q.png"), old, os.path.join(P, "Human_vs_OldBody_Face.png"))


if __name__ == "__main__":
    main()
