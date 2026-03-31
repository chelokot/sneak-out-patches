using System.Reflection;
using System.Text.Json;
using BepInEx.Logging;
using Gameplay.Skills;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SneakOut.MummyUnlock;

internal static class MummySarcophagusVisualRuntime
{
    private const string MeshResourceName = "SneakOut.MummyUnlock.Assets.sarcophagus_mesh.json";
    private const string TextureResourceName = "SneakOut.MummyUnlock.Assets.sarcophagus_texture.png";
    private static readonly Vector3 ReplacementPosition = new(0f, 1f, 0.5f);
    private static readonly Vector3 ReplacementScale = new(0.568422f, 0.568422f, 0.568422f);
    private static readonly Quaternion ReplacementRotation = Quaternion.identity;
    private static readonly HashSet<int> AppliedCubeIds = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static ManualLogSource? _logger;
    private static Mesh? _mesh;
    private static Texture2D? _texture;
    private static Material? _material;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
    }

    public static void ApplyToSarcophagi(Sarcophagus[] sarcophagi)
    {
        if (sarcophagi is null || sarcophagi.Length == 0)
        {
            return;
        }

        var appliedCount = 0;
        foreach (var sarcophagus in sarcophagi)
        {
            if (sarcophagus is null)
            {
                continue;
            }

            if (ApplyToSarcophagus(sarcophagus))
            {
                appliedCount++;
            }
        }

        if (appliedCount > 0)
        {
            _logger?.LogInfo($"Applied mummy sarcophagus replacement to {appliedCount} object(s)");
        }
    }

    private static bool ApplyToSarcophagus(Sarcophagus sarcophagus)
    {
        var cube = sarcophagus.transform.Find("Cube");
        if (cube is null)
        {
            _logger?.LogWarning($"Mummy sarcophagus visual child not found on '{sarcophagus.gameObject.name}'");
            return false;
        }

        var cubeId = cube.gameObject.GetInstanceID();
        if (!AppliedCubeIds.Add(cubeId))
        {
            return false;
        }

        var meshFilter = cube.GetComponent<MeshFilter>();
        var meshRenderer = cube.GetComponent<MeshRenderer>();
        if (meshFilter is null || meshRenderer is null)
        {
            _logger?.LogWarning($"Mummy sarcophagus visual components missing on '{cube.gameObject.name}'");
            return false;
        }

        meshFilter.sharedMesh = GetOrCreateMesh();
        meshRenderer.sharedMaterial = GetOrCreateMaterial(meshRenderer);
        cube.localPosition = ReplacementPosition;
        cube.localRotation = ReplacementRotation;
        cube.localScale = ReplacementScale;
        return true;
    }

    private static Mesh GetOrCreateMesh()
    {
        if (_mesh is not null)
        {
            return _mesh;
        }

        var payload = JsonSerializer.Deserialize<SarcophagusMeshResource>(LoadRequiredText(MeshResourceName), JsonOptions)
                      ?? throw new InvalidOperationException("Failed to deserialize mummy sarcophagus mesh payload");

        var vertices = new Vector3[payload.VertexCount];
        var normals = new Vector3[payload.VertexCount];
        var uvs = new Vector2[payload.VertexCount];
        for (var vertexIndex = 0; vertexIndex < payload.VertexCount; vertexIndex++)
        {
            var positionOffset = vertexIndex * 3;
            vertices[vertexIndex] = new Vector3(
                payload.Positions[positionOffset],
                payload.Positions[positionOffset + 1],
                payload.Positions[positionOffset + 2]);
            normals[vertexIndex] = new Vector3(
                payload.Normals[positionOffset],
                payload.Normals[positionOffset + 1],
                payload.Normals[positionOffset + 2]);
            var uvOffset = vertexIndex * 2;
            uvs[vertexIndex] = new Vector2(
                payload.Uvs[uvOffset],
                payload.Uvs[uvOffset + 1]);
        }

        var mesh = new Mesh
        {
            name = "MummySarcophagusReplacement"
        };
        mesh.vertices = ToIl2CppArray(vertices);
        mesh.normals = ToIl2CppArray(normals);
        mesh.uv = ToIl2CppArray(uvs);
        mesh.triangles = ToIl2CppArray(payload.Indices);
        mesh.RecalculateBounds();
        _mesh = mesh;
        return mesh;
    }

    private static Material GetOrCreateMaterial(MeshRenderer meshRenderer)
    {
        if (_material is not null)
        {
            return _material;
        }

        var template = meshRenderer.sharedMaterial;
        var material = template is not null
            ? new Material(template)
            : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var texture = GetOrCreateTexture();
        material.mainTexture = texture;
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        material.color = Color.white;
        _material = material;
        return material;
    }

    private static Texture2D GetOrCreateTexture()
    {
        if (_texture is not null)
        {
            return _texture;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "MummySarcophagusReplacement"
        };
        if (!ImageConversion.LoadImage(texture, ToIl2CppArray(LoadRequiredBytes(TextureResourceName))))
        {
            throw new InvalidOperationException("Failed to decode mummy sarcophagus texture");
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        _texture = texture;
        return texture;
    }

    private static string LoadRequiredText(string resourceName)
    {
        using var stream = OpenRequiredResource(resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] LoadRequiredBytes(string resourceName)
    {
        using var stream = OpenRequiredResource(resourceName);
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static Stream OpenRequiredResource(string resourceName)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
               ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found");
    }

    private static Il2CppStructArray<T> ToIl2CppArray<T>(IReadOnlyList<T> values) where T : unmanaged
    {
        var result = new Il2CppStructArray<T>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        return result;
    }

    private sealed class SarcophagusMeshResource
    {
        public int VertexCount { get; set; }
        public float[] Positions { get; set; } = Array.Empty<float>();
        public float[] Normals { get; set; } = Array.Empty<float>();
        public float[] Uvs { get; set; } = Array.Empty<float>();
        public int[] Indices { get; set; } = Array.Empty<int>();
    }
}
