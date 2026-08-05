#!/usr/bin/env python3
"""Replay Sneak Out's jump emote and the chair-release wall test.

The animation, skeleton, held-chair pose, collider and mesh are read directly
from the installed Unity assets.  The wall is intentionally parameterized:
the runtime logs currently identify it only as ``EnvironmentCollider`` and do
not record its transform.  The viewer therefore answers the useful geometric
question for every possible player-to-wall distance instead of pretending to
know the wall instance used in one network tick.
"""

from __future__ import annotations

import argparse
import bisect
import math
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

# Reuse the repository-local analysis environment when it already exists; a
# clean checkout can use the normal site-packages installation documented below.
local_packages = Path(__file__).resolve().parents[1] / ".tmp/python-packages"
if local_packages.is_dir():
    sys.path.insert(0, str(local_packages))

try:
    import matplotlib
    import numpy as np
    import UnityPy
    from UnityPy.helpers.MeshHelper import MeshHandler
except ImportError as exc:  # pragma: no cover - depends on the workstation
    raise SystemExit(
        "chair_jump_geometry.py requires UnityPy, numpy and matplotlib.\n"
        "Install them with: python3 -m pip install UnityPy numpy matplotlib"
    ) from exc

# Fedora toolboxes commonly provide Matplotlib but no Tk/Qt/GTK bindings. In
# that case Agg can only save images, so use Matplotlib's browser canvas for the
# interactive sliders. A real GUI backend, when present, remains untouched.
if "--save" not in sys.argv and str(matplotlib.get_backend()).lower() == "agg":
    try:
        import tornado  # noqa: F401 - WebAgg imports it lazily later
    except ImportError as exc:  # pragma: no cover - depends on the workstation
        raise SystemExit(
            "Matplotlib selected the non-interactive Agg backend because no Tk/Qt/GTK "
            "toolkit is installed. Install the browser backend with:\n"
            "python3 -m pip install tornado\n"
            "Then run the same command again."
        ) from exc
    matplotlib.use("WebAgg", force=True)
    # xdg-open/gio probes WebAgg with HEAD, while Matplotlib's handler accepts
    # GET only; gio then reports a misleading HTTP 405 and never opens the tab.
    # Printing the local URL is deterministic and leaves it clickable.
    matplotlib.rcParams["webagg.open_in_browser"] = False

import matplotlib.pyplot as plt
from matplotlib.widgets import Button, RadioButtons, Slider
from mpl_toolkits.mplot3d.art3d import Poly3DCollection


ANIMATION_NAME = "rig|emote_jumpup.kinguin"
AVATAR_NAME = "Kinguin_Base_fbxAvatar"
PLAYER_PREFAB_NAME = "_Kinguin_Base_prefab"
MODEL_ROOT_NAME = "Kinguin_Base_Merged_fbx"
HELD_ITEMS_NAME = "InHandThrowables"
ITEM_HOLDER_NAME = "ItemHolder"

CHAIRS = {
    "chair-a": "ChineseChairSet_a_chair_a_prefab",
    "chair-b": "ChineseChairSet_a_chair_b_prefab",
    "chair-c": "ChineseChairSet_a_chair_c_prefab",
    "chair-d": "ChineseChairSet_a_chair_d_prefab",
    "stool": "WarTable_Stool_a_prefab",
}

SWEEP_CLEARANCE = 0.03
SWEEP_SAMPLE_INSET = 0.8
BOX_FACES = (
    (0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4),
    (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5),
)


def vec3(value: object) -> np.ndarray:
    return np.array((value.x, value.y, value.z), dtype=float)


def quat(value: object) -> np.ndarray:
    return normalize_quat(np.array((value.x, value.y, value.z, value.w), dtype=float))


def normalize_quat(value: np.ndarray) -> np.ndarray:
    length = float(np.linalg.norm(value))
    return value / length if length > 1e-12 else np.array((0.0, 0.0, 0.0, 1.0))


def quat_mul(left: np.ndarray, right: np.ndarray) -> np.ndarray:
    lx, ly, lz, lw = left
    rx, ry, rz, rw = right
    return np.array(
        (
            lw * rx + lx * rw + ly * rz - lz * ry,
            lw * ry - lx * rz + ly * rw + lz * rx,
            lw * rz + lx * ry - ly * rx + lz * rw,
            lw * rw - lx * rx - ly * ry - lz * rz,
        ),
        dtype=float,
    )


def axis_quat(axis: int, degrees: float) -> np.ndarray:
    angle = math.radians(degrees) * 0.5
    result = np.zeros(4, dtype=float)
    result[axis] = math.sin(angle)
    result[3] = math.cos(angle)
    return result


def euler_quat(euler: Sequence[float]) -> np.ndarray:
    """Unity's ZXY Transform euler order as stored by this clip."""
    qx = axis_quat(0, float(euler[0]))
    qy = axis_quat(1, float(euler[1]))
    qz = axis_quat(2, float(euler[2]))
    return normalize_quat(quat_mul(quat_mul(qy, qx), qz))


def quat_matrix(rotation: np.ndarray) -> np.ndarray:
    x, y, z, w = normalize_quat(rotation)
    return np.array(
        (
            (1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)),
            (2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)),
            (2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)),
        ),
        dtype=float,
    )


def transform_matrix(position: np.ndarray, rotation: np.ndarray, scale: np.ndarray) -> np.ndarray:
    matrix = np.eye(4)
    matrix[:3, :3] = quat_matrix(rotation) @ np.diag(scale)
    matrix[:3, 3] = position
    return matrix


def transform_points(matrix: np.ndarray, points: np.ndarray) -> np.ndarray:
    homogeneous = np.column_stack((points, np.ones(len(points))))
    return (matrix @ homogeneous.T).T[:, :3]


def float_word(word: int) -> float:
    return struct.unpack("<f", struct.pack("<I", word))[0]


@dataclass(frozen=True)
class TransformNode:
    path: str
    parent: str | None
    position: np.ndarray
    rotation: np.ndarray
    scale: np.ndarray


@dataclass(frozen=True)
class ChairGeometry:
    key: str
    name: str
    held_position: np.ndarray
    held_rotation: np.ndarray
    collider_center: np.ndarray
    collider_size: np.ndarray
    vertices: np.ndarray
    triangles: np.ndarray


@dataclass(frozen=True)
class RayResult:
    origin: np.ndarray
    end: np.ndarray
    hit: bool
    distance: float | None


@dataclass(frozen=True)
class SweepState:
    name: str
    color: str
    rays: tuple[RayResult, ...]
    overlap: bool
    separated: bool
    desired_distance: float
    projected_radius: float
    safe_distance: float
    volume_hit_distance: float | None
    player_probe: RayResult | None


class StreamedCurves:
    def __init__(self, words: Sequence[int], curve_count: int):
        self.segments: list[list[tuple[float, tuple[float, float, float, float]]]] = [
            [] for _ in range(curve_count)
        ]
        cursor = 0
        while cursor < len(words):
            time = float_word(words[cursor])
            key_count = words[cursor + 1]
            cursor += 2
            for _ in range(key_count):
                index = words[cursor]
                coefficients = tuple(float_word(word) for word in words[cursor + 1 : cursor + 5])
                cursor += 5
                if index < curve_count:
                    self.segments[index].append((time, coefficients))

        self.times = [[entry[0] for entry in segment] for segment in self.segments]

    def value(self, index: int, time: float) -> float:
        segment = self.segments[index]
        if not segment:
            return 0.0
        position = max(0, bisect.bisect_right(self.times[index], time) - 1)
        key_time, coefficients = segment[position]
        delta = 0.0 if not math.isfinite(key_time) else time - key_time
        a, b, c, d = coefficients
        return ((a * delta + b) * delta + c) * delta + d


class GeometryReplay:
    def __init__(self, resources_path: Path):
        environment = UnityPy.load(str(resources_path))
        self.asset = next(iter(environment.files.values()))
        self.animation = self._named_object("AnimationClip", ANIMATION_NAME).read()
        self.avatar = self._named_object("Avatar", AVATAR_NAME).read()
        self.player_prefab = self._named_object("GameObject", PLAYER_PREFAB_NAME).read()
        self.duration = float(self.animation.m_MuscleClip.m_StopTime)
        self.event_time = next(
            event.time for event in self.animation.m_Events if event.functionName == "OnEmoteJump"
        )
        streamed = self.animation.m_MuscleClip.m_Clip.data.m_StreamedClip
        self.curves = StreamedCurves(streamed.data, streamed.curveCount)
        self.bindings = self._build_scalar_bindings(streamed.curveCount)
        self.hash_paths = dict(self.avatar.m_TOS)
        self.nodes = self._load_skeleton()
        self.chairs = self._load_chairs()

    def _named_object(self, type_name: str, name: str):
        for object_reader in self.asset.objects.values():
            if object_reader.type.name != type_name:
                continue
            value = object_reader.read()
            if value.m_Name == name:
                return object_reader
        raise RuntimeError(f"{type_name} {name!r} was not found in resources.assets")

    def _game_object_with_component(self, name: str, component_type: str):
        for object_reader in self.asset.objects.values():
            if object_reader.type.name != "GameObject":
                continue
            value = object_reader.read()
            if value.m_Name != name:
                continue
            if any(
                component.component.deref().type.name == component_type
                for component in value.m_Component
            ):
                return object_reader
        raise RuntimeError(f"GameObject {name!r} with {component_type} was not found")

    def _find_child_transform(self, root_transform, name: str):
        for child_pointer in root_transform.m_Children:
            child = child_pointer.read()
            if child.m_GameObject.read().m_Name == name:
                return child
            match = self._find_child_transform(child, name)
            if match is not None:
                return match
        return None

    def _load_skeleton(self) -> dict[str, TransformNode]:
        prefab_transform = self.player_prefab.m_Component[0].component.read()
        model_transform = self._find_child_transform(prefab_transform, MODEL_ROOT_NAME)
        rig_transform = self._find_child_transform(model_transform, "rig")
        if rig_transform is None:
            raise RuntimeError("player rig was not found")

        nodes: dict[str, TransformNode] = {}

        def visit(transform, path: str, parent: str | None) -> None:
            name = transform.m_GameObject.read().m_Name
            current_path = f"{path}/{name}" if path else name
            nodes[current_path] = TransformNode(
                current_path,
                parent,
                vec3(transform.m_LocalPosition),
                quat(transform.m_LocalRotation),
                vec3(transform.m_LocalScale),
            )
            for child_pointer in transform.m_Children:
                visit(child_pointer.read(), current_path, current_path)

        visit(rig_transform, "", None)
        if not any(path.endswith("/" + ITEM_HOLDER_NAME) for path in nodes):
            raise RuntimeError("ItemHolder was not found in player rig")
        return nodes

    @staticmethod
    def _binding_dimensions(attribute: int) -> int:
        return {1: 3, 2: 4, 3: 3, 4: 3}.get(attribute, 1)

    def _build_scalar_bindings(self, curve_count: int) -> list[tuple[int, int, int]]:
        result: list[tuple[int, int, int]] = []
        for binding in self.animation.m_ClipBindingConstant.genericBindings:
            dimensions = self._binding_dimensions(binding.attribute)
            for component in range(dimensions):
                result.append((binding.path, binding.attribute, component))
                if len(result) == curve_count:
                    return result
        raise RuntimeError("animation bindings do not cover the streamed curves")

    def _load_chairs(self) -> dict[str, ChairGeometry]:
        prefab_transform = self.player_prefab.m_Component[0].component.read()
        held_items = self._find_child_transform(prefab_transform, HELD_ITEMS_NAME)
        if held_items is None:
            raise RuntimeError("InHandThrowables was not found")

        held_poses = {
            child.m_GameObject.read().m_Name: child
            for pointer in held_items.m_Children
            for child in (pointer.read(),)
        }
        result: dict[str, ChairGeometry] = {}
        for key, prefab_name in CHAIRS.items():
            prefab_reader = self._game_object_with_component(prefab_name, "BoxCollider")
            prefab = prefab_reader.read()
            root_transform = prefab.m_Component[0].component.read()
            box = next(
                component.component.read()
                for component in prefab.m_Component
                if component.component.deref().type.name == "BoxCollider"
            )
            pose = held_poses.get(prefab_name)
            if pose is None and key == "stool":
                pose = held_poses.get("WarTable_Stool_a")
            if pose is None:
                raise RuntimeError(f"held pose for {prefab_name} was not found")

            vertices = np.empty((0, 3))
            triangles = np.empty((0, 3), dtype=int)
            mesh_transform = self._find_mesh_transform(root_transform)
            if mesh_transform is not None:
                mesh_filter = next(
                    component.component.read()
                    for component in mesh_transform.m_GameObject.read().m_Component
                    if component.component.deref().type.name == "MeshFilter"
                )
                handler = MeshHandler(mesh_filter.m_Mesh.read())
                handler.process()
                if handler.m_Vertices:
                    vertices = np.asarray(handler.m_Vertices, dtype=float)
                    triangles = np.asarray(
                        [triangle for group in handler.get_triangles() for triangle in group],
                        dtype=int,
                    )

            result[key] = ChairGeometry(
                key,
                prefab_name,
                vec3(pose.m_LocalPosition),
                quat(pose.m_LocalRotation),
                vec3(box.m_Center),
                vec3(box.m_Size),
                vertices,
                triangles,
            )
        return result

    def _find_mesh_transform(self, transform):
        if any(
            component.component.deref().type.name == "MeshFilter"
            for component in transform.m_GameObject.read().m_Component
        ):
            return transform
        for pointer in transform.m_Children:
            result = self._find_mesh_transform(pointer.read())
            if result is not None:
                return result
        return None

    def pose(self, time: float, chair: ChairGeometry) -> tuple[dict[str, np.ndarray], np.ndarray]:
        local_positions = {path: node.position.copy() for path, node in self.nodes.items()}
        local_rotations = {path: node.rotation.copy() for path, node in self.nodes.items()}
        local_scales = {path: node.scale.copy() for path, node in self.nodes.items()}
        animated: dict[tuple[str, int], np.ndarray] = {}

        for curve_index, (path_hash, attribute, component) in enumerate(self.bindings):
            path = self.hash_paths.get(path_hash)
            if path not in self.nodes:
                continue
            dimensions = self._binding_dimensions(attribute)
            values = animated.setdefault((path, attribute), np.zeros(dimensions))
            values[component] = self.curves.value(curve_index, time)

        for (path, attribute), values in animated.items():
            if attribute == 1:
                local_positions[path] = values
            elif attribute == 2:
                local_rotations[path] = normalize_quat(values)
            elif attribute == 3:
                local_scales[path] = values
            elif attribute == 4:
                local_rotations[path] = euler_quat(values)

        item_path = next(path for path in self.nodes if path.endswith("/" + ITEM_HOLDER_NAME))
        local_positions[item_path] = chair.held_position
        local_rotations[item_path] = chair.held_rotation

        world: dict[str, np.ndarray] = {}
        for path, node in self.nodes.items():
            local = transform_matrix(local_positions[path], local_rotations[path], local_scales[path])
            world[path] = local if node.parent is None else world[node.parent] @ local
        return world, world[item_path]


def box_corners(center: np.ndarray, half: np.ndarray, rotation: np.ndarray) -> np.ndarray:
    signs = np.array(
        [(-1, -1, -1), (-1, -1, 1), (-1, 1, -1), (-1, 1, 1),
         (1, -1, -1), (1, -1, 1), (1, 1, -1), (1, 1, 1)],
        dtype=float,
    )
    return center + (quat_matrix(rotation) @ (signs * half).T).T


def ray_box_distance(origin: np.ndarray, direction: np.ndarray, minimum: np.ndarray, maximum: np.ndarray) -> float | None:
    near = -math.inf
    far = math.inf
    for axis in range(3):
        if abs(direction[axis]) < 1e-9:
            if origin[axis] < minimum[axis] or origin[axis] > maximum[axis]:
                return None
            continue
        first = (minimum[axis] - origin[axis]) / direction[axis]
        second = (maximum[axis] - origin[axis]) / direction[axis]
        near = max(near, min(first, second))
        far = min(far, max(first, second))
        if near > far:
            return None
    if far < 0:
        return None
    return max(0.0, near)


def aabb_obb_overlap(wall_min: np.ndarray, wall_max: np.ndarray, corners: np.ndarray) -> bool:
    # Exact for the viewer's axis-aligned wall when used as the conservative
    # fallback in the current patch: Unity receives Quaternion.identity there.
    chair_min = corners.min(axis=0)
    chair_max = corners.max(axis=0)
    return bool(np.all(chair_max >= wall_min) and np.all(chair_min <= wall_max))


def projected_radius(half: np.ndarray, rotation: np.ndarray, direction: np.ndarray) -> float:
    axes = quat_matrix(rotation)
    return float(sum(abs(float(np.dot(direction, axes[:, index]))) * half[index] for index in range(3)))


def swept_obb_aabb_distance(
    start: np.ndarray,
    direction: np.ndarray,
    distance: float,
    half: np.ndarray,
    rotation: np.ndarray,
    wall_min: np.ndarray,
    wall_max: np.ndarray,
) -> float | None:
    """Exact time-of-impact for a fixed-orientation OBB translated against an AABB."""
    wall_center = (wall_min + wall_max) * 0.5
    wall_half = (wall_max - wall_min) * 0.5
    chair_axes = quat_matrix(rotation)
    world_axes = np.eye(3)
    axes = [world_axes[:, index] for index in range(3)]
    axes.extend(chair_axes[:, index] for index in range(3))
    axes.extend(
        np.cross(world_axes[:, world_index], chair_axes[:, chair_index])
        for world_index in range(3)
        for chair_index in range(3)
    )

    entry = 0.0
    exit_distance = distance
    relative_start = start - wall_center
    for raw_axis in axes:
        length = float(np.linalg.norm(raw_axis))
        if length < 1e-9:
            continue
        axis = raw_axis / length
        wall_radius = float(np.dot(np.abs(axis), wall_half))
        chair_radius = sum(
            half[index] * abs(float(np.dot(axis, chair_axes[:, index])))
            for index in range(3)
        )
        combined_radius = wall_radius + chair_radius
        projected_start = float(np.dot(relative_start, axis))
        projected_speed = float(np.dot(direction, axis))
        if abs(projected_speed) < 1e-9:
            if abs(projected_start) > combined_radius:
                return None
            continue

        first = (-combined_radius - projected_start) / projected_speed
        second = (combined_radius - projected_start) / projected_speed
        entry = max(entry, min(first, second))
        exit_distance = min(exit_distance, max(first, second))
        if entry > exit_distance:
            return None

    return entry if entry <= distance and exit_distance >= 0.0 else None


def evaluate_sweep(
    player_anchor: np.ndarray,
    chair_center: np.ndarray,
    chair_half: np.ndarray,
    chair_rotation: np.ndarray,
    wall_min: np.ndarray,
    wall_max: np.ndarray,
) -> SweepState:
    anchor = player_anchor.copy()
    anchor[1] = chair_center[1]
    delta = chair_center - anchor
    delta[1] = 0.0
    distance = float(np.linalg.norm(delta))
    if distance < 0.001:
        return SweepState("DEGENERATE", "#999999", (), False, False, distance, 0.0, distance, None, None)
    direction = delta / distance
    radius = projected_radius(chair_half, chair_rotation, direction)
    lateral = np.array((-direction[2], 0.0, direction[0]))
    lateral_radius = projected_radius(chair_half, chair_rotation, lateral) * SWEEP_SAMPLE_INSET
    vertical_radius = chair_half[1] * SWEEP_SAMPLE_INSET
    origins = (
        anchor,
        anchor + lateral * lateral_radius,
        anchor - lateral * lateral_radius,
        anchor + np.array((0.0, vertical_radius, 0.0)),
        anchor - np.array((0.0, vertical_radius, 0.0)),
    )
    cast_distance = distance + radius + SWEEP_CLEARANCE
    safe_distance = distance
    volume_hit_distance = swept_obb_aabb_distance(
        anchor,
        direction,
        distance + SWEEP_CLEARANCE,
        chair_half * 0.98,
        chair_rotation,
        wall_min,
        wall_max,
    )
    if volume_hit_distance is not None and volume_hit_distance > SWEEP_CLEARANCE:
        safe_distance = min(safe_distance, max(0.0, volume_hit_distance - SWEEP_CLEARANCE))

    player_probe_origin = player_anchor.copy()
    player_probe_origin[1] = min(chair_center[1], player_anchor[1] + 0.5)
    player_probe_origin -= direction * SWEEP_CLEARANCE
    player_probe_hit_distance = ray_box_distance(
        player_probe_origin,
        direction,
        wall_min,
        wall_max,
    )
    player_probe_hit = (
        player_probe_hit_distance is not None
        and player_probe_hit_distance <= cast_distance
    )
    player_probe = RayResult(
        player_probe_origin,
        player_probe_origin + direction * (
            player_probe_hit_distance if player_probe_hit else cast_distance
        ),
        player_probe_hit,
        player_probe_hit_distance,
    )
    if player_probe_hit_distance is not None:
        safe_distance = min(
            safe_distance,
            player_probe_hit_distance - radius - SWEEP_CLEARANCE,
        )
    rays: list[RayResult] = []
    for origin in origins:
        hit_distance = ray_box_distance(origin, direction, wall_min, wall_max)
        hit = hit_distance is not None and hit_distance <= cast_distance
        end_distance = hit_distance if hit else cast_distance
        rays.append(RayResult(origin, origin + direction * end_distance, hit, hit_distance))
        if hit_distance is not None:
            safe_distance = min(safe_distance, max(0.0, hit_distance - radius - SWEEP_CLEARANCE))

    corners = box_corners(chair_center, chair_half, chair_rotation)
    overlap = aabb_obb_overlap(wall_min, wall_max, corners)
    root_side = np.sign(anchor[2] - (wall_min[2] + wall_max[2]) * 0.5)
    corner_sides = np.sign(corners[:, 2] - (wall_min[2] + wall_max[2]) * 0.5)
    separated = bool(root_side != 0 and np.all(corner_sides == -root_side))
    any_hit = any(ray.hit for ray in rays)
    clamped = any_hit and safe_distance < distance
    player_side_clamped = player_probe_hit and safe_distance < distance
    volume_clamped = (
        volume_hit_distance is not None
        and volume_hit_distance > SWEEP_CLEARANCE
        and safe_distance < distance
    )
    if player_side_clamped:
        name, color = "PLAYER-SIDE CLAMP", "#e76f51"
    elif volume_clamped:
        name, color = "VOLUME CLAMP", "#e76f51"
    elif clamped:
        name, color = "RAY CLAMP", "#e76f51"
    elif separated:
        name, color = "FALSE CLEAR: chair is beyond wall", "#d00000"
    elif overlap:
        name, color = "FALLBACK OVERLAP", "#f4a261"
    else:
        name, color = "CLEAR", "#2a9d8f"
    return SweepState(
        name,
        color,
        tuple(rays),
        overlap,
        separated,
        distance,
        radius,
        safe_distance,
        volume_hit_distance,
        player_probe,
    )


def wall_geometry(distance: float, height: float, thickness: float, width: float = 3.0):
    center = np.array((0.0, height * 0.5, distance))
    half = np.array((width * 0.5, height * 0.5, thickness * 0.5))
    return center - half, center + half, box_corners(center, half, np.array((0.0, 0.0, 0.0, 1.0)))


def add_box(axis, corners: np.ndarray, color: str, alpha: float, edgecolor: str | None = None) -> None:
    axis.add_collection3d(
        Poly3DCollection(
            [[corners[index] for index in face] for face in BOX_FACES],
            facecolors=color,
            edgecolors=edgecolor or color,
            linewidths=0.8,
            alpha=alpha,
        )
    )


class Viewer:
    def __init__(
        self,
        replay: GeometryReplay,
        chair_key: str,
        save_path: Path | None,
        initial_time: float | None = None,
        initial_wall: float = -0.42,
        initial_wall_height: float = 1.10,
    ):
        self.replay = replay
        self.chair_key = chair_key
        self.save_path = save_path
        self.figure = plt.figure(figsize=(15, 9))
        self.axis = self.figure.add_axes((0.05, 0.23, 0.68, 0.72), projection="3d")
        self.info = self.figure.text(0.755, 0.94, "", va="top", family="monospace", fontsize=8.5)
        self.time_slider = Slider(self.figure.add_axes((0.10, 0.12, 0.56, 0.025)), "emote", 0.0, replay.duration, valinit=initial_time if initial_time is not None else replay.event_time)
        self.wall_slider = Slider(self.figure.add_axes((0.10, 0.075, 0.56, 0.025)), "wall z", -1.50, 1.50, valinit=initial_wall)
        self.height_slider = Slider(self.figure.add_axes((0.10, 0.03, 0.56, 0.025)), "wall h", 0.20, 2.20, valinit=initial_wall_height)
        self.chair_buttons = RadioButtons(
            self.figure.add_axes((0.77, 0.12, 0.18, 0.25)),
            tuple(replay.chairs),
            active=tuple(replay.chairs).index(chair_key),
        )
        self.find_button = Button(self.figure.add_axes((0.77, 0.045, 0.18, 0.05)), "Find false clear")
        for widget in (self.time_slider, self.wall_slider, self.height_slider):
            widget.on_changed(self.draw)
        self.chair_buttons.on_clicked(self.select_chair)
        self.find_button.on_clicked(self.find_false_clear)
        self.draw()

    def select_chair(self, label: str) -> None:
        self.chair_key = label
        self.draw()

    def frame_geometry(self, time: float):
        chair = self.replay.chairs[self.chair_key]
        world, chair_matrix = self.replay.pose(time, chair)
        center = transform_points(chair_matrix, chair.collider_center[None, :])[0]
        rotation_matrix = chair_matrix[:3, :3]
        scale = np.linalg.norm(rotation_matrix, axis=0)
        normalized_rotation = rotation_matrix / np.where(scale > 1e-9, scale, 1.0)
        rotation = matrix_quat(normalized_rotation)
        half = chair.collider_size * scale * 0.5
        return chair, world, chair_matrix, center, half, rotation

    def find_false_clear(self, _event=None) -> None:
        best = None
        for time in np.linspace(0.0, self.replay.duration, 301):
            _chair, _world, _matrix, center, half, rotation = self.frame_geometry(float(time))
            direction = -1.0 if center[2] < 0 else 1.0
            for wall_z in direction * np.linspace(0.08, 1.25, 235):
                wall_min, wall_max, _ = wall_geometry(float(wall_z), self.height_slider.val, 0.16)
                state = evaluate_sweep(np.zeros(3), center, half, rotation, wall_min, wall_max)
                if state.separated and not any(ray.hit for ray in state.rays):
                    best = (time, wall_z)
                    break
            if best:
                break
        if best:
            self.time_slider.set_val(float(best[0]))
            self.wall_slider.set_val(float(best[1]))
            _chair, _world, _matrix, center, half, rotation = self.frame_geometry(float(best[0]))
            wall_min, wall_max, _ = wall_geometry(float(best[1]), self.height_slider.val, 0.16)
            state = evaluate_sweep(np.zeros(3), center, half, rotation, wall_min, wall_max)
            lower_ray_height = min(ray.origin[1] for ray in state.rays)
            volume_hit = (
                f"{state.volume_hit_distance:.6f}m"
                if state.volume_hit_distance is not None
                else "none"
            )
            player_probe_hit = (
                f"{state.player_probe.distance:.6f}m"
                if state.player_probe is not None and state.player_probe.distance is not None
                else "none"
            )
            print(
                "False clear: "
                f"time={best[0]:.6f}s frame={best[0] * 60:.2f}, "
                f"wall_z={best[1]:.3f}m wall_top={wall_max[1]:.3f}m, "
                f"chair_center={center.round(6).tolist()}, "
                f"lowest_ray_y={lower_ray_height:.6f}m, hits=0/5, "
                f"volume_hit={volume_hit}, player_probe={player_probe_hit}, "
                f"safe_center={state.safe_distance:.6f}m."
            )
        else:
            self.info.set_text("No false-clear frame found for the current wall height.")
            self.figure.canvas.draw_idle()

    def draw(self, _value=None) -> None:
        time = float(self.time_slider.val)
        chair, world, chair_matrix, center, half, rotation = self.frame_geometry(time)
        wall_min, wall_max, wall_corners = wall_geometry(float(self.wall_slider.val), float(self.height_slider.val), 0.16)
        state = evaluate_sweep(np.zeros(3), center, half, rotation, wall_min, wall_max)

        self.axis.clear()
        self.axis.set_title("Exact jump-emote pose + current Chair Wall Throw Fix geometry")
        self.axis.set_xlabel("X (right)")
        self.axis.set_ylabel("Z (forward)")
        self.axis.set_zlabel("Y (up)")
        self.axis.set_xlim(-1.5, 1.5)
        self.axis.set_ylim(-0.8, 1.7)
        self.axis.set_zlim(0.0, 3.8)
        self.axis.view_init(elev=19, azim=-58)
        self.axis.set_box_aspect((3.0, 2.5, 3.8))

        # Matplotlib's displayed axes are X, Z, Y; convert only at rendering.
        display = lambda point: (point[0], point[2], point[1])
        for path, node in self.replay.nodes.items():
            if node.parent is None:
                continue
            start = world[node.parent][:3, 3]
            end = world[path][:3, 3]
            xs, ys, zs = zip(display(start), display(end))
            self.axis.plot(xs, ys, zs, color="#264653", linewidth=1.2, alpha=0.82)

        converted_wall = np.array([display(point) for point in wall_corners])
        add_box(self.axis, converted_wall, "#457b9d", 0.22, "#1d3557")
        chair_corners = box_corners(center, half, rotation)
        add_box(self.axis, np.array([display(point) for point in chair_corners]), state.color, 0.18, state.color)

        if len(chair.vertices) and len(chair.triangles):
            mesh_points = transform_points(chair_matrix, chair.vertices)
            mesh_display = np.array([display(point) for point in mesh_points])
            faces = mesh_display[chair.triangles]
            # Drawing every face is still cheap for the ~250 vertex chair and
            # makes the collider/visual mismatch directly inspectable.
            self.axis.add_collection3d(
                Poly3DCollection(faces, facecolor="#e9c46a", edgecolor="#6d4c2f", linewidth=0.15, alpha=0.55)
            )

        for ray in state.rays:
            start, end = display(ray.origin), display(ray.end)
            color = "#d00000" if ray.hit else "#2a9d8f"
            self.axis.plot(
                (start[0], end[0]), (start[1], end[1]), (start[2], end[2]),
                color=color, linewidth=2.0 if ray.hit else 1.0, alpha=0.95,
            )
        if state.player_probe is not None:
            start, end = display(state.player_probe.origin), display(state.player_probe.end)
            self.axis.plot(
                (start[0], end[0]), (start[1], end[1]), (start[2], end[2]),
                color="#ff006e" if state.player_probe.hit else "#8338ec",
                linewidth=2.4,
                alpha=0.95,
            )
        holder = chair_matrix[:3, 3]
        hx, hy, hz = display(holder)
        self.axis.scatter((hx,), (hy,), (hz,), color="#ff006e", s=40, label="ItemHolder")
        self.axis.scatter((0,), (0,), (0,), color="black", s=35, label="player root")
        self.axis.legend(loc="upper left", fontsize=8)

        jump_marker = " < OnEmoteJump" if abs(time - self.replay.event_time) < 1 / 60 else ""
        self.info.set_text(
            f"STATE: {state.name}\n"
            f"asset: {ANIMATION_NAME}\n"
            f"time: {time:0.4f}s  frame: {time * 60:0.2f}{jump_marker}\n"
            f"chair: {chair.name}\n"
            f"ItemHolder: {holder.round(4)}\n"
            f"collider center: {center.round(4)}\n"
            f"collider half: {half.round(4)}\n"
            f"player->chair: {state.desired_distance:0.4f} m\n"
            f"projected radius: {state.projected_radius:0.4f} m\n"
            f"safe distance: {state.safe_distance:0.4f} m\n"
            f"volume hit: {state.volume_hit_distance if state.volume_hit_distance is not None else 'none'}\n"
            f"player probe: {state.player_probe.distance if state.player_probe is not None else 'none'}\n"
            f"ray hits: {sum(ray.hit for ray in state.rays)}/5\n"
            f"fallback overlap: {state.overlap}\n"
            f"fully beyond wall: {state.separated}\n\n"
            "Exact from assets:\n"
            "  102 animation frames @ 60 Hz\n"
            "  complete bone transforms\n"
            "  held-item pose + chair mesh/box\n\n"
            "Parameterized (missing from old log):\n"
            "  player-relative wall transform"
        )
        self.info.set_color(state.color)
        self.figure.canvas.draw_idle()

    def run(self) -> None:
        if self.save_path:
            self.figure.savefig(self.save_path, dpi=160)
            print(f"Saved {self.save_path}")
        else:
            plt.show()


def matrix_quat(matrix: np.ndarray) -> np.ndarray:
    trace = float(np.trace(matrix))
    if trace > 0:
        scale = math.sqrt(trace + 1.0) * 2
        return normalize_quat(np.array(((matrix[2, 1] - matrix[1, 2]) / scale,
                                        (matrix[0, 2] - matrix[2, 0]) / scale,
                                        (matrix[1, 0] - matrix[0, 1]) / scale,
                                        0.25 * scale)))
    axis = int(np.argmax(np.diag(matrix)))
    if axis == 0:
        scale = math.sqrt(1.0 + matrix[0, 0] - matrix[1, 1] - matrix[2, 2]) * 2
        value = (0.25 * scale, (matrix[0, 1] + matrix[1, 0]) / scale,
                 (matrix[0, 2] + matrix[2, 0]) / scale, (matrix[2, 1] - matrix[1, 2]) / scale)
    elif axis == 1:
        scale = math.sqrt(1.0 + matrix[1, 1] - matrix[0, 0] - matrix[2, 2]) * 2
        value = ((matrix[0, 1] + matrix[1, 0]) / scale, 0.25 * scale,
                 (matrix[1, 2] + matrix[2, 1]) / scale, (matrix[0, 2] - matrix[2, 0]) / scale)
    else:
        scale = math.sqrt(1.0 + matrix[2, 2] - matrix[0, 0] - matrix[1, 1]) * 2
        value = ((matrix[0, 2] + matrix[2, 0]) / scale, (matrix[1, 2] + matrix[2, 1]) / scale,
                 0.25 * scale, (matrix[1, 0] - matrix[0, 1]) / scale)
    return normalize_quat(np.array(value))


def detect_game_dir(explicit: str | None) -> Path:
    if explicit:
        return Path(explicit).expanduser().resolve()
    candidates = (
        Path.home() / ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Sneak Out",
        Path.home() / ".steam/steam/steamapps/common/Sneak Out",
        Path.home() / ".local/share/Steam/steamapps/common/Sneak Out",
    )
    for candidate in candidates:
        if (candidate / "Sneak Out_Data/resources.assets").is_file():
            return candidate
    raise SystemExit("Sneak Out was not detected; pass --game-dir /path/to/Sneak Out")


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", help="Sneak Out installation directory")
    parser.add_argument("--chair", choices=tuple(CHAIRS), default="chair-a")
    parser.add_argument("--save", type=Path, help="render one frame instead of opening the GUI")
    parser.add_argument("--time", type=float, help="initial emote time in seconds")
    parser.add_argument("--wall-z", type=float, default=-0.42, help="initial player-relative wall Z")
    parser.add_argument("--wall-height", type=float, default=1.10)
    parser.add_argument("--find-false-clear", action="store_true", help="start at the first counterexample")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    arguments = parse_args(argv or sys.argv[1:])
    game_dir = detect_game_dir(arguments.game_dir)
    resources = game_dir / "Sneak Out_Data/resources.assets"
    if not resources.is_file():
        raise SystemExit(f"resources.assets not found below {game_dir}")
    replay = GeometryReplay(resources)
    print(
        f"Loaded {ANIMATION_NAME}: 102 frames, {replay.duration:.6f}s, "
        f"OnEmoteJump={replay.event_time:.6f}s, {len(replay.nodes)} rig transforms."
    )
    viewer = Viewer(
        replay,
        arguments.chair,
        arguments.save,
        arguments.time,
        arguments.wall_z,
        arguments.wall_height,
    )
    if arguments.find_false_clear:
        viewer.find_false_clear()
    viewer.run()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
