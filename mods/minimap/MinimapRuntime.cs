using BepInEx.Logging;
using Gameplay.Enviro;
using Gameplay.Interactions;
using Gameplay.Player.Components;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Types;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SneakOut.Minimap;

internal static class MinimapRuntime
{
    private const float BuildDelaySeconds = 5f;
    private const int TextureSize = 512;
    private const float WorldPadding = 3f;
    private const float PanelBorder = 5f;
    private const float LocalMarkerSize = 19f;
    private const float FixedMapOverscan = 1.41421356f;
    private const float DefaultMapRotationDegrees = 135f;
    private const float MaximumZoomScale = 4f;
    private const float DoorMarkerLength = 2.1f;
    private const float DoorSlotMatchDistance = 0.8f;
    private const float RoomCornerRadiusPixels = 6f;
    private const int RoomCornerSegments = 4;
    private const int PointMarkerOutlineRadius = 6;
    private const int PointMarkerRadius = 4;

    private static readonly Color32 BackgroundColor = new(10, 15, 22, 242);
    private static readonly Color32 RoomColor = new(71, 92, 111, 255);
    private static readonly Color32 HallwayColor = new(54, 74, 94, 255);
    private static readonly Color32 SpawnRoomColor = new(47, 118, 119, 255);
    private static readonly Color32 TaskRoomColor = new(143, 105, 47, 255);
    private static readonly Color32 LabyrinthColor = new(100, 73, 126, 255);
    private static readonly Color32 OutlineColor = new(163, 190, 204, 255);
    private static readonly Color32 DoorColor = new(235, 202, 105, 255);
    private static readonly Color32 WardrobeColor = new(74, 232, 224, 255);
    private static readonly Color32 ItemGeneratorColor = new(255, 218, 69, 255);

    private static ManualLogSource? _logger;
    private static MinimapConfig? _configuration;
    private static Harmony? _harmony;
    private static InputAction? _toggleAction;
    private static SpookedNetworkPlayer? _localPlayer;
    private static Canvas? _canvas;
    private static GameObject? _panel;
    private static MinimapMaskGraphic? _panelGraphic;
    private static Mask? _panelMask;
    private static RawImage? _mapImage;
    private static RectTransform? _mapRect;
    private static RectTransform? _localMarker;
    private static Texture2D? _mapTexture;
    private static Sprite? _localMarkerSprite;
    private static MapProjection _projection;
    private static bool _haveProjection;
    private static bool _mapReady;
    private static bool _userVisible;
    private static int _sceneHandle = -1;
    private static float _buildAt;
    private static bool _buildAttempted;
    private static int _consecutiveFailures;
    private static float _mapRotationDegrees = DefaultMapRotationDegrees;

    public static void Initialize(ManualLogSource logger, MinimapConfig configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _userVisible = configuration.StartVisible.Value;

        EnsureToggleAction();
        _harmony ??= new Harmony(MinimapPlugin.PluginGuid);
        _harmony.PatchAll();

        ClassInjector.RegisterTypeInIl2Cpp<MinimapMaskGraphic>();
        ClassInjector.RegisterTypeInIl2Cpp<MinimapWatcher>();
        var watcherObject = new GameObject("MinimapWatcher");
        watcherObject.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(watcherObject);
        watcherObject.AddComponent<MinimapWatcher>();
    }

    public static void ObservePlayer(SpookedNetworkPlayer player)
    {
        try
        {
            if (player is not null
                && player.Pointer != IntPtr.Zero
                && player.HasInputAuthority
                && !player.IsBot)
            {
                _localPlayer = player;
            }
        }
        catch (Exception exception)
        {
            Log($"Ignored unavailable local-player candidate: {exception.Message}");
        }
    }

    public static void ForgetPlayer(SpookedNetworkPlayer player)
    {
        if (_localPlayer is not null && _localPlayer.Pointer == player.Pointer)
        {
            _localPlayer = null;
            SetPanelVisible(false);
        }
    }

    public static void ApplyConfiguration(
        bool toggleBindingChanged,
        bool visibilityChanged,
        bool inputModeChanged)
    {
        if ((visibilityChanged || inputModeChanged) && _configuration is not null)
        {
            _userVisible = _configuration.StartVisible.Value;
        }
        if (toggleBindingChanged)
        {
            _toggleAction?.Disable();
            _toggleAction?.Dispose();
            _toggleAction = null;
            EnsureToggleAction();
        }
        LayoutUi();
    }

    private static void Tick()
    {
        MinimapSettingsUi.Tick();
        if (_configuration?.EnableMod.Value != true)
        {
            SetPanelVisible(false);
            return;
        }

        var showWhileHolding = _configuration.ShowWhileHolding.Value;
        if (!showWhileHolding && _toggleAction?.WasPressedThisFrame() == true)
        {
            _userVisible = !_userVisible;
            Log($"Visibility toggled to {_userVisible}");
        }

        var scene = SceneManager.GetActiveScene();
        if (scene.handle != _sceneHandle)
        {
            ResetForScene(scene);
        }

        if (!IsPlayableMap(scene.name))
        {
            SetPanelVisible(false);
            return;
        }

        if (!_buildAttempted && Time.unscaledTime >= _buildAt)
        {
            _buildAttempted = true;
            BuildMap(scene);
        }

        if (!_mapReady || !TryGetLocalPlayer(out var localPlayer))
        {
            SetPanelVisible(false);
            return;
        }

        UpdateLocalMarker(localPlayer);
        SetPanelVisible(showWhileHolding
            ? _toggleAction?.IsPressed() == true
            : _userVisible);
    }

    private static void ResetForScene(Scene scene)
    {
        _sceneHandle = scene.handle;
        if (IsPlayableMap(scene.name) && _configuration is not null)
        {
            _userVisible = _configuration.StartVisible.Value;
            Log($"Map entry visibility reset to {_userVisible}");
        }
        _buildAt = Time.unscaledTime + BuildDelaySeconds;
        _buildAttempted = false;
        _mapReady = false;
        _haveProjection = false;
        SetPanelVisible(false);

        if (_mapImage is not null)
        {
            _mapImage.texture = null;
        }
        if (_mapTexture is not null)
        {
            UnityEngine.Object.Destroy(_mapTexture);
            _mapTexture = null;
        }
    }

    private static void BuildMap(Scene scene)
    {
        var shapes = CollectRoomShapes(scene);
        if (shapes.Count == 0)
        {
            _logger?.LogWarning($"Minimap found no room volumes in {scene.name}");
            return;
        }

        _projection = CreateProjection(shapes);
        _haveProjection = true;
        var doors = CollectDoorShapes(scene);
        var doorways = CollectOpenDoorwayShapes(scene, doors);
        var wardrobes = CollectInteractablePositions<MagicWardrobe>(scene, "teleport wardrobe");
        var itemGenerators = CollectInteractablePositions<ItemGenerator>(scene, "item roller");
        _mapRotationDegrees = Camera.main?.transform.eulerAngles.y ?? DefaultMapRotationDegrees;
        var pixels = new Color32[TextureSize * TextureSize];
        Array.Fill(pixels, BackgroundColor);
        foreach (var shape in shapes)
        {
            FillShape(pixels, shape, _projection);
        }
        foreach (var shape in shapes)
        {
            OutlineShape(pixels, shape, _projection);
        }
        foreach (var doorway in doorways)
        {
            DrawDoorway(pixels, doorway, _projection);
        }
        foreach (var door in doors)
        {
            DrawDoor(pixels, door, _projection);
        }
        foreach (var wardrobe in wardrobes)
        {
            DrawPointMarker(pixels, wardrobe, _projection, WardrobeColor);
        }
        foreach (var itemGenerator in itemGenerators)
        {
            DrawPointMarker(pixels, itemGenerator, _projection, ItemGeneratorColor);
        }

        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = $"RuntimeMinimap-{scene.name}",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        texture.SetPixels32(ToIl2CppArray(pixels));
        texture.Apply(false, true);

        EnsureUi();
        _mapTexture = texture;
        _mapImage!.texture = texture;
        _mapReady = true;
        Log(
            $"Built {scene.name} floor plan from {shapes.Count} room volumes, {doors.Count} doors, "
            + $"{doorways.Count} open doorways, {wardrobes.Count} teleport wardrobes, "
            + $"and {itemGenerators.Count} item rollers; rotation={_mapRotationDegrees:0.##}; "
            + $"worldBounds=({_projection.MinimumX:0.##},{_projection.MinimumZ:0.##}) "
            + $"to ({_projection.MaximumX:0.##},{_projection.MaximumZ:0.##})");
    }

    private static List<RoomShape> CollectRoomShapes(Scene scene)
    {
        var result = new List<RoomShape>();
        foreach (var room in Resources.FindObjectsOfTypeAll<Room>())
        {
            if (room is null
                || room.Pointer == IntPtr.Zero
                || room.gameObject.scene.handle != scene.handle)
            {
                continue;
            }

            foreach (var collider in room.GetComponents<Collider>())
            {
                if (collider is null || collider.Pointer == IntPtr.Zero || !collider.isTrigger)
                {
                    continue;
                }

                result.Add(new RoomShape(GetHorizontalCorners(collider), room.RoomType));
            }
        }
        return result;
    }

    private static List<DoorShape> CollectDoorShapes(Scene scene)
    {
        var result = new List<DoorShape>();
        foreach (var door in Resources.FindObjectsOfTypeAll<Door>())
        {
            if (door is null
                || door.Pointer == IntPtr.Zero
                || door.gameObject.scene.handle != scene.handle)
            {
                continue;
            }

            try
            {
                var collider = door._doorInteractableCollider ?? door.Collider;
                if (collider is null || collider.Pointer == IntPtr.Zero)
                {
                    continue;
                }

                var corners = GetHorizontalCorners(collider);
                var firstEdgeLength = Vector2.Distance(corners[0], corners[1]);
                var secondEdgeLength = Vector2.Distance(corners[1], corners[2]);
                var start = firstEdgeLength >= secondEdgeLength
                    ? (corners[0] + corners[3]) * 0.5f
                    : (corners[0] + corners[1]) * 0.5f;
                var end = firstEdgeLength >= secondEdgeLength
                    ? (corners[1] + corners[2]) * 0.5f
                    : (corners[3] + corners[2]) * 0.5f;
                result.Add(new DoorShape(start, end));
            }
            catch (Exception exception)
            {
                Log($"Ignored unavailable door geometry for {door.name}: {exception.Message}");
            }
        }
        return result;
    }

    private static List<DoorShape> CollectOpenDoorwayShapes(
        Scene scene,
        IReadOnlyList<DoorShape> interactiveDoors)
    {
        var authoredSlots = new List<DoorShape>();
        foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (renderer is null
                || renderer.Pointer == IntPtr.Zero
                || renderer.gameObject.scene.handle != scene.handle)
            {
                continue;
            }

            // Each standard wall doorway contains one thin lintel mesh ending in _02c.
            // Unlike the Door interaction component, that authored mesh also exists for
            // pass-through frames with no door leaf.
            var rendererName = renderer.name;
            var parent = renderer.transform.parent;
            if (parent is null
                || !string.Equals(parent.name, "Renderers", StringComparison.Ordinal)
                || !rendererName.Contains("_Door_", StringComparison.OrdinalIgnoreCase)
                || !rendererName.EndsWith("_02c", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bounds = renderer.bounds;
            var horizontalLength = Mathf.Max(bounds.size.x, bounds.size.z);
            if (horizontalLength < 2.5f || horizontalLength > 3.5f)
            {
                continue;
            }

            var center = new Vector2(bounds.center.x, bounds.center.z);
            if (authoredSlots.Any(slot => Vector2.Distance(slot.Center, center) < 0.25f))
            {
                continue;
            }

            var halfLength = DoorMarkerLength * 0.5f;
            var start = bounds.size.x >= bounds.size.z
                ? new Vector2(center.x - halfLength, center.y)
                : new Vector2(center.x, center.y - halfLength);
            var end = bounds.size.x >= bounds.size.z
                ? new Vector2(center.x + halfLength, center.y)
                : new Vector2(center.x, center.y + halfLength);
            authoredSlots.Add(new DoorShape(start, end));
        }

        return authoredSlots
            .Where(slot => !interactiveDoors.Any(door =>
                Vector2.Distance(door.Center, slot.Center) <= DoorSlotMatchDistance))
            .ToList();
    }

    private static List<Vector2> CollectInteractablePositions<T>(Scene scene, string markerName)
        where T : Interactable
    {
        var result = new List<Vector2>();
        foreach (var interactable in Resources.FindObjectsOfTypeAll<T>())
        {
            if (interactable is null
                || interactable.Pointer == IntPtr.Zero
                || interactable.gameObject.scene.handle != scene.handle)
            {
                continue;
            }

            try
            {
                result.Add(ToHorizontal(interactable.Position));
            }
            catch (Exception exception)
            {
                Log($"Ignored unavailable {markerName} position for {interactable.name}: {exception.Message}");
            }
        }
        return result;
    }

    private static Vector2[] GetHorizontalCorners(Collider collider)
    {
        if (collider.TryCast<BoxCollider>() is { } boxCollider)
        {
            var center = boxCollider.center;
            var half = boxCollider.size * 0.5f;
            var transform = boxCollider.transform;
            return new[]
            {
                ToHorizontal(transform.TransformPoint(center + new Vector3(-half.x, 0f, -half.z))),
                ToHorizontal(transform.TransformPoint(center + new Vector3(half.x, 0f, -half.z))),
                ToHorizontal(transform.TransformPoint(center + new Vector3(half.x, 0f, half.z))),
                ToHorizontal(transform.TransformPoint(center + new Vector3(-half.x, 0f, half.z))),
            };
        }

        var bounds = collider.bounds;
        return new[]
        {
            new Vector2(bounds.min.x, bounds.min.z),
            new Vector2(bounds.max.x, bounds.min.z),
            new Vector2(bounds.max.x, bounds.max.z),
            new Vector2(bounds.min.x, bounds.max.z),
        };
    }

    private static Vector2 ToHorizontal(Vector3 point)
    {
        return new Vector2(point.x, point.z);
    }

    private static MapProjection CreateProjection(IReadOnlyList<RoomShape> shapes)
    {
        var minimumX = float.PositiveInfinity;
        var maximumX = float.NegativeInfinity;
        var minimumZ = float.PositiveInfinity;
        var maximumZ = float.NegativeInfinity;
        foreach (var shape in shapes)
        {
            foreach (var corner in shape.Corners)
            {
                minimumX = Mathf.Min(minimumX, corner.x);
                maximumX = Mathf.Max(maximumX, corner.x);
                minimumZ = Mathf.Min(minimumZ, corner.y);
                maximumZ = Mathf.Max(maximumZ, corner.y);
            }
        }

        var centerX = (minimumX + maximumX) * 0.5f;
        var centerZ = (minimumZ + maximumZ) * 0.5f;
        var side = Mathf.Max(maximumX - minimumX, maximumZ - minimumZ) + WorldPadding * 2f;
        return new MapProjection(centerX - side * 0.5f, centerZ - side * 0.5f, side);
    }

    private static void FillShape(Color32[] pixels, RoomShape shape, MapProjection projection)
    {
        var polygon = CreateRoundedPolygon(shape.Corners.Select(projection.WorldToPixel).ToArray());
        var minimumX = Mathf.Clamp(Mathf.FloorToInt(polygon.Min(point => point.x)), 0, TextureSize - 1);
        var maximumX = Mathf.Clamp(Mathf.CeilToInt(polygon.Max(point => point.x)), 0, TextureSize - 1);
        var minimumY = Mathf.Clamp(Mathf.FloorToInt(polygon.Min(point => point.y)), 0, TextureSize - 1);
        var maximumY = Mathf.Clamp(Mathf.CeilToInt(polygon.Max(point => point.y)), 0, TextureSize - 1);
        var color = GetRoomColor(shape.RoomType);

        for (var y = minimumY; y <= maximumY; y++)
        {
            for (var x = minimumX; x <= maximumX; x++)
            {
                if (ContainsPoint(polygon, new Vector2(x + 0.5f, y + 0.5f)))
                {
                    pixels[y * TextureSize + x] = color;
                }
            }
        }
    }

    private static bool ContainsPoint(IReadOnlyList<Vector2> polygon, Vector2 point)
    {
        var positive = false;
        var negative = false;
        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            var cross = (end.x - start.x) * (point.y - start.y)
                - (end.y - start.y) * (point.x - start.x);
            positive |= cross > 0.001f;
            negative |= cross < -0.001f;
            if (positive && negative)
            {
                return false;
            }
        }
        return true;
    }

    private static void OutlineShape(Color32[] pixels, RoomShape shape, MapProjection projection)
    {
        var polygon = CreateRoundedPolygon(shape.Corners.Select(projection.WorldToPixel).ToArray());
        for (var index = 0; index < polygon.Length; index++)
        {
            DrawLine(pixels, polygon[index], polygon[(index + 1) % polygon.Length]);
        }
    }

    private static Vector2[] CreateRoundedPolygon(IReadOnlyList<Vector2> corners)
    {
        var rounded = new List<Vector2>(corners.Count * (RoomCornerSegments + 1));
        for (var index = 0; index < corners.Count; index++)
        {
            var previous = corners[(index + corners.Count - 1) % corners.Count];
            var corner = corners[index];
            var next = corners[(index + 1) % corners.Count];
            var incomingLength = Vector2.Distance(corner, previous);
            var outgoingLength = Vector2.Distance(corner, next);
            var distance = Mathf.Min(
                RoomCornerRadiusPixels,
                Mathf.Min(incomingLength, outgoingLength) * 0.35f);
            var start = corner + (previous - corner).normalized * distance;
            var end = corner + (next - corner).normalized * distance;
            rounded.Add(start);
            for (var segment = 1; segment <= RoomCornerSegments; segment++)
            {
                var amount = segment / (float)RoomCornerSegments;
                var inverse = 1f - amount;
                rounded.Add(
                    inverse * inverse * start
                    + 2f * inverse * amount * corner
                    + amount * amount * end);
            }
        }
        return rounded.ToArray();
    }

    private static void DrawDoor(Color32[] pixels, DoorShape door, MapProjection projection)
    {
        DrawLine(
            pixels,
            projection.WorldToPixel(door.Start),
            projection.WorldToPixel(door.End),
            DoorColor,
            radius: 2);
    }

    private static void DrawDoorway(Color32[] pixels, DoorShape doorway, MapProjection projection)
    {
        DrawDoor(pixels, doorway, projection);
    }

    private static void DrawPointMarker(
        Color32[] pixels,
        Vector2 worldPosition,
        MapProjection projection,
        Color32 color)
    {
        var point = projection.WorldToPixel(worldPosition);
        var x = Mathf.RoundToInt(point.x);
        var y = Mathf.RoundToInt(point.y);
        SetPixelDisc(pixels, x, y, BackgroundColor, PointMarkerOutlineRadius);
        SetPixelDisc(pixels, x, y, color, PointMarkerRadius);
    }

    private static void DrawLine(Color32[] pixels, Vector2 start, Vector2 end)
    {
        DrawLine(pixels, start, end, OutlineColor, radius: 1);
    }

    private static void DrawLine(
        Color32[] pixels,
        Vector2 start,
        Vector2 end,
        Color32 color,
        int radius)
    {
        var x0 = Mathf.RoundToInt(start.x);
        var y0 = Mathf.RoundToInt(start.y);
        var x1 = Mathf.RoundToInt(end.x);
        var y1 = Mathf.RoundToInt(end.y);
        var deltaX = Mathf.Abs(x1 - x0);
        var stepX = x0 < x1 ? 1 : -1;
        var deltaY = -Mathf.Abs(y1 - y0);
        var stepY = y0 < y1 ? 1 : -1;
        var error = deltaX + deltaY;

        while (true)
        {
            SetPixelBlock(pixels, x0, y0, color, radius);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }
            var twiceError = error * 2;
            if (twiceError >= deltaY)
            {
                error += deltaY;
                x0 += stepX;
            }
            if (twiceError <= deltaX)
            {
                error += deltaX;
                y0 += stepY;
            }
        }
    }

    private static void SetPixelBlock(Color32[] pixels, int x, int y, Color32 color, int radius)
    {
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                var targetX = x + offsetX;
                var targetY = y + offsetY;
                if (targetX >= 0 && targetX < TextureSize && targetY >= 0 && targetY < TextureSize)
                {
                    pixels[targetY * TextureSize + targetX] = color;
                }
            }
        }
    }

    private static void SetPixelDisc(Color32[] pixels, int x, int y, Color32 color, int radius)
    {
        var radiusSquared = radius * radius;
        for (var offsetY = -radius; offsetY <= radius; offsetY++)
        {
            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            {
                if (offsetX * offsetX + offsetY * offsetY > radiusSquared)
                {
                    continue;
                }

                var targetX = x + offsetX;
                var targetY = y + offsetY;
                if (targetX >= 0 && targetX < TextureSize && targetY >= 0 && targetY < TextureSize)
                {
                    pixels[targetY * TextureSize + targetX] = color;
                }
            }
        }
    }

    private static Color32 GetRoomColor(RoomType roomType)
    {
        return roomType switch
        {
            RoomType.Hallway => HallwayColor,
            RoomType.SpawnRoom => SpawnRoomColor,
            RoomType.LabyrinthRoom => LabyrinthColor,
            RoomType.CookingTaskRoom or RoomType.AlchemyTaskRoom or RoomType.TelescopeTaskRoom => TaskRoomColor,
            _ => RoomColor,
        };
    }

    private static void EnsureUi()
    {
        if (_canvas is not null && _canvas.Pointer != IntPtr.Zero && _canvas)
        {
            LayoutUi();
            return;
        }

        var canvasObject = new GameObject("RuntimeMinimapOverlay");
        canvasObject.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(canvasObject);
        _canvas = canvasObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = 25000;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _panel = new GameObject("MinimapPanel");
        _panel.hideFlags = HideFlags.HideAndDontSave;
        _panel.transform.SetParent(canvasObject.transform, false);
        var panelRect = _panel.AddComponent<RectTransform>();
        _panelGraphic = _panel.AddComponent<MinimapMaskGraphic>();
        _panelGraphic.color = new Color(0.025f, 0.04f, 0.055f, 0.96f);
        _panelGraphic.raycastTarget = false;
        _panelMask = _panel.AddComponent<Mask>();
        _panelMask.showMaskGraphic = true;

        var mapObject = new GameObject("MinimapFloorPlan");
        mapObject.hideFlags = HideFlags.HideAndDontSave;
        mapObject.transform.SetParent(_panel.transform, false);
        _mapRect = mapObject.AddComponent<RectTransform>();
        _mapImage = mapObject.AddComponent<RawImage>();
        _mapImage.color = Color.white;
        _mapImage.raycastTarget = false;

        var markerObject = new GameObject("MinimapLocalPlayer");
        markerObject.hideFlags = HideFlags.HideAndDontSave;
        markerObject.transform.SetParent(mapObject.transform, false);
        _localMarker = markerObject.AddComponent<RectTransform>();
        var markerImage = markerObject.AddComponent<Image>();
        markerImage.sprite = GetOrCreateLocalMarkerSprite();
        markerImage.color = Color.white;
        markerImage.raycastTarget = false;

        LayoutUi();
        _panel.SetActive(false);
    }

    private static void LayoutUi()
    {
        if (_panel is null
            || _panelGraphic is null
            || _panelMask is null
            || _mapRect is null
            || _localMarker is null
            || _configuration is null)
        {
            return;
        }

        var size = Mathf.Clamp(_configuration.MapSize.Value, 140, 500);
        var topMargin = Mathf.Clamp(_configuration.TopMargin.Value, 0, 300);
        var rightMargin = Mathf.Clamp(_configuration.RightMargin.Value, 0, 300);
        var panelRect = _panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.one;
        panelRect.anchorMax = Vector2.one;
        panelRect.pivot = Vector2.one;
        panelRect.anchoredPosition = new Vector2(-rightMargin, -topMargin);
        panelRect.sizeDelta = new Vector2(size, size);
        panelRect.localScale = Vector3.one;
        panelRect.localRotation = Quaternion.identity;

        var circle = _configuration.MapShape.Value == MinimapShape.Circle;
        _panelGraphic.SetCircle(circle);
        _panelMask.enabled = true;

        var innerSize = size - PanelBorder * 2f;
        _mapRect.anchorMin = new Vector2(0.5f, 0.5f);
        _mapRect.anchorMax = new Vector2(0.5f, 0.5f);
        _mapRect.pivot = new Vector2(0.5f, 0.5f);
        _mapRect.sizeDelta = new Vector2(
            innerSize * FixedMapOverscan,
            innerSize * FixedMapOverscan);
        _mapRect.anchoredPosition = Vector2.zero;

        _localMarker.anchorMin = new Vector2(0.5f, 0.5f);
        _localMarker.anchorMax = new Vector2(0.5f, 0.5f);
        _localMarker.pivot = new Vector2(0.5f, 0.5f);
        _localMarker.sizeDelta = new Vector2(LocalMarkerSize, LocalMarkerSize);
        _localMarker.localScale = Vector3.one;
    }

    private static void UpdateLocalMarker(SpookedNetworkPlayer localPlayer)
    {
        if (!_haveProjection
            || _mapImage is null
            || _mapRect is null
            || _localMarker is null
            || _configuration is null)
        {
            return;
        }

        var position = localPlayer.transform.position;
        var normalized = _projection.WorldToNormalized(new Vector2(position.x, position.z));
        normalized.x = Mathf.Clamp01(normalized.x);
        normalized.y = Mathf.Clamp01(normalized.y);
        var zoomAmount = Mathf.Clamp01(_configuration.Zoom.Value / 100f);
        var zoomScale = Mathf.Lerp(1f, MaximumZoomScale, zoomAmount);
        var baseViewSize = 1f / zoomScale;
        var halfView = baseViewSize * 0.5f;
        var viewCenter = Vector2.Lerp(new Vector2(0.5f, 0.5f), normalized, zoomAmount);
        viewCenter.x = Mathf.Clamp(viewCenter.x, halfView, 1f - halfView);
        viewCenter.y = Mathf.Clamp(viewCenter.y, halfView, 1f - halfView);

        // The authored camera is fixed at an isometric angle. A sqrt(2)-overscanned
        // rectangle plus a sqrt(2) UV fit keeps all four floor-plan corners visible
        // after applying that angle once.
        var sampledViewSize = baseViewSize * FixedMapOverscan * FixedMapOverscan;
        _mapImage.uvRect = new Rect(
            viewCenter.x - sampledViewSize * 0.5f,
            viewCenter.y - sampledViewSize * 0.5f,
            sampledViewSize,
            sampledViewSize);
        _mapRect.localEulerAngles = new Vector3(0f, 0f, _mapRotationDegrees);

        var availableMarkerSpace = _mapRect.rect.width - LocalMarkerSize;
        _localMarker.anchoredPosition = new Vector2(
            (normalized.x - viewCenter.x) / sampledViewSize * availableMarkerSpace,
            (normalized.y - viewCenter.y) / sampledViewSize * availableMarkerSpace);
        _localMarker.localEulerAngles = new Vector3(0f, 0f, -localPlayer.transform.eulerAngles.y);
    }

    private static Sprite GetOrCreateLocalMarkerSprite()
    {
        if (_localMarkerSprite is not null)
        {
            return _localMarkerSprite;
        }

        const int size = 32;
        var pixels = new Color32[size * size];
        for (var y = 3; y < 29; y++)
        {
            var halfWidth = Mathf.Lerp(10.5f, 0.8f, (y - 3f) / 26f);
            var minimumX = Mathf.CeilToInt(15.5f - halfWidth);
            var maximumX = Mathf.FloorToInt(15.5f + halfWidth);
            for (var x = minimumX; x <= maximumX; x++)
            {
                var edge = x == minimumX || x == maximumX || y == 3 || y == 28;
                pixels[y * size + x] = edge
                    ? new Color32(20, 53, 55, 255)
                    : new Color32(94, 240, 222, 255);
            }
        }

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "RuntimeMinimapLocalMarker",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels32(ToIl2CppArray(pixels));
        texture.Apply(false, true);
        _localMarkerSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f);
        _localMarkerSprite.name = "RuntimeMinimapLocalMarker";
        return _localMarkerSprite;
    }

    private static Il2CppStructArray<Color32> ToIl2CppArray(IReadOnlyList<Color32> values)
    {
        var result = new Il2CppStructArray<Color32>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }
        return result;
    }

    private static bool TryGetLocalPlayer(out SpookedNetworkPlayer localPlayer)
    {
        localPlayer = null!;
        try
        {
            if (_localPlayer is null
                || _localPlayer.Pointer == IntPtr.Zero
                || !_localPlayer.HasInputAuthority
                || _localPlayer.IsBot
                || _localPlayer.GamePlayerState != GamePlayerState.Alive)
            {
                return false;
            }
            localPlayer = _localPlayer;
            return true;
        }
        catch
        {
            _localPlayer = null;
            return false;
        }
    }

    private static void SetPanelVisible(bool visible)
    {
        if (_panel is not null && _panel.activeSelf != visible)
        {
            _panel.SetActive(visible);
        }
    }

    private static void EnsureToggleAction()
    {
        if (_toggleAction is not null || _configuration is null)
        {
            return;
        }

        try
        {
            _toggleAction = new InputAction(
                "ToggleMinimap",
                binding: _configuration.ToggleBinding.Value);
            _toggleAction.Enable();
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                $"Invalid minimap key binding '{_configuration.ToggleBinding.Value}': {exception.Message}; using Tab");
            _toggleAction?.Dispose();
            _toggleAction = new InputAction("ToggleMinimap", binding: "<Keyboard>/tab");
            _toggleAction.Enable();
        }
    }

    private static bool IsPlayableMap(string sceneName)
    {
        return sceneName.StartsWith("Map", StringComparison.Ordinal);
    }

    private static void Log(string message)
    {
        if (_configuration?.EnableLogging.Value == true)
        {
            _logger?.LogInfo(message);
        }
    }

    private readonly record struct RoomShape(Vector2[] Corners, RoomType RoomType);

    private readonly record struct DoorShape(Vector2 Start, Vector2 End)
    {
        public Vector2 Center => (Start + End) * 0.5f;
    }

    private readonly record struct MapProjection(float MinimumX, float MinimumZ, float Side)
    {
        public float MaximumX => MinimumX + Side;

        public float MaximumZ => MinimumZ + Side;

        public Vector2 WorldToNormalized(Vector2 world)
        {
            return new Vector2(
                (world.x - MinimumX) / Side,
                (world.y - MinimumZ) / Side);
        }

        public Vector2 WorldToPixel(Vector2 world)
        {
            var normalized = WorldToNormalized(world);
            return normalized * (TextureSize - 1f);
        }
    }

    private sealed class MinimapMaskGraphic : MaskableGraphic
    {
        private const int CircleSegments = 64;
        private bool _circle = true;

        public MinimapMaskGraphic(IntPtr pointer) : base(pointer)
        {
        }

        public MinimapMaskGraphic()
            : base(ClassInjector.DerivedConstructorPointer<MinimapMaskGraphic>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        public void SetCircle(bool circle)
        {
            if (_circle == circle)
            {
                return;
            }
            _circle = circle;
            SetVerticesDirty();
        }

        public override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var bounds = rectTransform.rect;
            var vertexColor = (Color32)color;
            if (!_circle)
            {
                vertexHelper.AddVert(
                    new Vector3(bounds.xMin, bounds.yMin, 0f), vertexColor, Vector2.zero);
                vertexHelper.AddVert(
                    new Vector3(bounds.xMin, bounds.yMax, 0f), vertexColor, Vector2.zero);
                vertexHelper.AddVert(
                    new Vector3(bounds.xMax, bounds.yMax, 0f), vertexColor, Vector2.zero);
                vertexHelper.AddVert(
                    new Vector3(bounds.xMax, bounds.yMin, 0f), vertexColor, Vector2.zero);
                vertexHelper.AddTriangle(0, 1, 2);
                vertexHelper.AddTriangle(0, 2, 3);
                return;
            }

            var center = bounds.center;
            var radius = Mathf.Min(bounds.width, bounds.height) * 0.5f;
            vertexHelper.AddVert(new Vector3(center.x, center.y, 0f), vertexColor, Vector2.zero);
            for (var segment = 0; segment < CircleSegments; segment++)
            {
                var angle = segment / (float)CircleSegments * Mathf.PI * 2f;
                vertexHelper.AddVert(
                    new Vector3(
                        center.x + Mathf.Cos(angle) * radius,
                        center.y + Mathf.Sin(angle) * radius,
                        0f),
                    vertexColor,
                    Vector2.zero);
            }
            for (var segment = 0; segment < CircleSegments; segment++)
            {
                vertexHelper.AddTriangle(
                    0,
                    segment + 1,
                    (segment + 1) % CircleSegments + 1);
            }
        }
    }

    private sealed class MinimapWatcher : MonoBehaviour
    {
        public MinimapWatcher(IntPtr pointer) : base(pointer)
        {
        }

        public MinimapWatcher() : base(ClassInjector.DerivedConstructorPointer<MinimapWatcher>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private void Update()
        {
            try
            {
                Tick();
                _consecutiveFailures = 0;
            }
            catch (Exception exception)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures == 1)
                {
                    _logger?.LogError($"Minimap update failed: {exception}");
                }
                if (_consecutiveFailures >= 3)
                {
                    _logger?.LogError("Minimap disabled itself after three consecutive update failures");
                    _configuration!.EnableMod.Value = false;
                    SetPanelVisible(false);
                }
            }
        }

        private void OnApplicationQuit()
        {
            _toggleAction?.Dispose();
            _toggleAction = null;
        }
    }
}
