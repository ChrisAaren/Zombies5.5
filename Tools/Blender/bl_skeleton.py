"""Shared humanoid skeleton for Zombies 5.5 characters.

The single source of truth for bone names, rest positions, parents and rolls. Both the body
(make_human.py) and the animation clips (make_animations.py) build their armature from here, so
clips authored on one armature play on the other. Bone names follow Unity's Humanoid auto-mapper.

Coordinates are Blender world space in meters: the character stands on z = 0, faces -Y
(Unity +Z after FBX export), and its LEFT side is +X. Rest pose is a T-pose: arms horizontal,
palms down, fingers straight, legs straight, feet forward. Total height ~1.80 m (no hair).

Usage inside Blender:
    import sys; sys.path.append(r"<this folder>")
    import bl_skeleton
    arm_obj = bl_skeleton.build_armature("CharacterRig")
"""

import bpy
from mathutils import Vector

# (name, head, tail, parent, roll_axis)
# roll_axis: the world direction the bone's local Z should face ("up" for horizontal bones,
# "fwd" for vertical ones), so every bone has a consistent, predictable orientation.

FWD = (0.0, -1.0, 0.0)   # character forward in Blender
UP = (0.0, 0.0, 1.0)


def _side(name_l, x_sign):
    return name_l.replace("Left", "Right") if x_sign < 0 else name_l


def _mirror(p, x_sign):
    return (p[0] * x_sign, p[1], p[2])


# Torso and head (center line).
_CENTER = [
    ("Hips",  (0.0, 0.00, 0.97), (0.0, 0.01, 1.07), None,    FWD),
    ("Spine", (0.0, 0.01, 1.07), (0.0, 0.01, 1.25), "Hips",  FWD),
    ("Chest", (0.0, 0.01, 1.25), (0.0, 0.02, 1.50), "Spine", FWD),
    ("Neck",  (0.0, 0.02, 1.50), (0.0, 0.00, 1.59), "Chest", FWD),
    ("Head",  (0.0, 0.00, 1.59), (0.0, 0.00, 1.80), "Neck",  FWD),
]

# Left side (+X); the right side is mirrored automatically.
_LEFT = [
    ("LeftShoulder", (0.03, 0.01, 1.44), (0.17, 0.02, 1.44), "Chest",        UP),
    ("LeftUpperArm", (0.17, 0.02, 1.44), (0.45, 0.02, 1.44), "LeftShoulder", UP),
    ("LeftLowerArm", (0.45, 0.02, 1.44), (0.70, 0.01, 1.44), "LeftUpperArm", UP),
    ("LeftHand",     (0.70, 0.01, 1.44), (0.79, 0.00, 1.44), "LeftLowerArm", UP),

    ("LeftThumbProximal",     (0.725, -0.030, 1.425), (0.755, -0.058, 1.420), "LeftHand",              UP),
    ("LeftThumbIntermediate", (0.755, -0.058, 1.420), (0.780, -0.078, 1.418), "LeftThumbProximal",     UP),
    ("LeftThumbDistal",       (0.780, -0.078, 1.418), (0.802, -0.094, 1.417), "LeftThumbIntermediate", UP),

    ("LeftIndexProximal",     (0.790, -0.030, 1.442), (0.835, -0.032, 1.442), "LeftHand",              UP),
    ("LeftIndexIntermediate", (0.835, -0.032, 1.442), (0.863, -0.033, 1.442), "LeftIndexProximal",     UP),
    ("LeftIndexDistal",       (0.863, -0.033, 1.442), (0.885, -0.033, 1.442), "LeftIndexIntermediate", UP),

    ("LeftMiddleProximal",     (0.792, -0.010, 1.443), (0.842, -0.010, 1.443), "LeftHand",               UP),
    ("LeftMiddleIntermediate", (0.842, -0.010, 1.443), (0.874, -0.010, 1.443), "LeftMiddleProximal",     UP),
    ("LeftMiddleDistal",       (0.874, -0.010, 1.443), (0.898, -0.010, 1.443), "LeftMiddleIntermediate", UP),

    ("LeftRingProximal",     (0.789, 0.010, 1.442), (0.835, 0.011, 1.442), "LeftHand",             UP),
    ("LeftRingIntermediate", (0.835, 0.011, 1.442), (0.865, 0.012, 1.442), "LeftRingProximal",     UP),
    ("LeftRingDistal",       (0.865, 0.012, 1.442), (0.887, 0.012, 1.442), "LeftRingIntermediate", UP),

    ("LeftLittleProximal",     (0.783, 0.029, 1.440), (0.819, 0.031, 1.440), "LeftHand",               UP),
    ("LeftLittleIntermediate", (0.819, 0.031, 1.440), (0.841, 0.032, 1.440), "LeftLittleProximal",     UP),
    ("LeftLittleDistal",       (0.841, 0.032, 1.440), (0.860, 0.033, 1.440), "LeftLittleIntermediate", UP),

    ("LeftUpperLeg", (0.095, 0.00, 0.93),  (0.095, -0.01, 0.51), "Hips",         FWD),
    ("LeftLowerLeg", (0.095, -0.01, 0.51), (0.095, 0.02, 0.085), "LeftUpperLeg", FWD),
    ("LeftFoot",     (0.095, 0.02, 0.085), (0.095, -0.11, 0.025), "LeftLowerLeg", UP),
    ("LeftToes",     (0.095, -0.11, 0.025), (0.095, -0.17, 0.020), "LeftFoot",    UP),
]


def bone_table():
    """Every bone as (name, head, tail, parent, roll_axis), parents before children."""
    table = list(_CENTER)
    for x_sign in (1.0, -1.0):
        for name, head, tail, parent, roll in _LEFT:
            table.append((
                _side(name, x_sign),
                _mirror(head, x_sign),
                _mirror(tail, x_sign),
                _side(parent, x_sign) if parent else None,
                roll,
            ))
    return table


BONE_NAMES = [b[0] for b in bone_table()]


def build_armature(name="CharacterRig"):
    """Create the armature object in the current scene, in its T-pose rest pose."""
    data = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit = data.edit_bones
    for bname, head, tail, parent, roll_axis in bone_table():
        eb = edit.new(bname)
        eb.head = Vector(head)
        eb.tail = Vector(tail)
        eb.align_roll(Vector(roll_axis))
        if parent:
            eb.parent = edit[parent]
            # Connect when the child starts where the parent ends (a clean chain).
            eb.use_connect = (Vector(edit[parent].tail) - Vector(head)).length < 1e-4
        eb.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj
