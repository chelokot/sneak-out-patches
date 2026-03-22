#!/usr/bin/env python3

from __future__ import annotations

import sys
from pathlib import Path

import UnityPy


def main() -> int:
    path = Path(sys.argv[1])
    env = UnityPy.load(str(path))
    asset = next(iter(env.files.values()))
    if len(sys.argv) > 2:
        root_path_id = int(sys.argv[2])

        def walk(transform_path_id: int, depth: int) -> None:
            transform_tree = asset.objects[transform_path_id].read_typetree()
            game_object_path_id = transform_tree["m_GameObject"]["m_PathID"]
            game_object_name = asset.objects[game_object_path_id].read_typetree().get("m_Name")
            anchored_position = transform_tree.get("m_AnchoredPosition")
            anchored_text = ""
            if isinstance(anchored_position, dict):
                anchored_text = f" pos=({anchored_position.get('x')},{anchored_position.get('y')})"
            print("  " * depth + f"T {transform_path_id} GO {game_object_path_id} {game_object_name}{anchored_text}")
            game_object_tree = asset.objects[game_object_path_id].read_typetree()
            for component in game_object_tree["m_Component"]:
                component_path_id = component["component"]["m_PathID"]
                component_object = asset.objects[component_path_id]
                print(
                    "  " * (depth + 1)
                    + f"C {component_path_id} class {component_object.class_id} {component_object.type.name} size {component_object.byte_size}"
                )
            for child in transform_tree["m_Children"]:
                walk(child["m_PathID"], depth + 1)

        walk(root_path_id, 0)
        return 0
    for path_id, obj in sorted(asset.objects.items()):
        if path_id < 28033 or obj.class_id != 114:
            continue
        try:
            tree = obj.read_typetree()
        except Exception as exc:
            print("ERR", path_id, obj.byte_size, exc)
            continue
        game_object = tree.get("m_GameObject", {})
        game_object_path_id = game_object.get("m_PathID") if isinstance(game_object, dict) else None
        game_object_name = None
        if game_object_path_id in asset.objects:
            try:
                game_object_name = asset.objects[game_object_path_id].read_typetree().get("m_Name")
            except Exception:
                game_object_name = None
        script_path_id = tree.get("m_Script", {}).get("m_PathID") if isinstance(tree, dict) else None
        print(path_id, obj.byte_size, game_object_name, script_path_id)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
