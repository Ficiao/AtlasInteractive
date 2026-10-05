using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

public static class RuntimeModelCache
{
    private const int Version = 2;

    public static void Save(GameObject root, string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(Version);

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        writer.Write(transforms.Length);

        Dictionary<Transform, int> indices = new Dictionary<Transform, int>();

        for (int i = 0; i < transforms.Length; i++)
        {
            indices[transforms[i]] = i;
        }

        foreach (Transform transform in transforms)
        {
            writer.Write(transform.name);

            int parentIndex = transform.parent != null && indices.TryGetValue(transform.parent, out int index)
                ? index
                : -1;

            writer.Write(parentIndex);

            WriteVector3(writer, transform.localPosition);
            WriteQuaternion(writer, transform.localRotation);
            WriteVector3(writer, transform.localScale);

            MeshFilter meshFilter = transform.GetComponent<MeshFilter>();
            MeshRenderer renderer = transform.GetComponent<MeshRenderer>();

            bool hasMesh = meshFilter != null && meshFilter.sharedMesh != null;
            writer.Write(hasMesh);

            if (!hasMesh) continue;

            WriteMesh(writer, meshFilter.sharedMesh);

            Material[] materials = renderer != null
                ? renderer.sharedMaterials
                : Array.Empty<Material>();

            writer.Write(materials.Length);

            foreach (Material material in materials)
            {
                WriteMaterial(writer, material);
            }
        }
    }

    public static GameObject Load(string path, string rootName)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new BinaryReader(stream);

        int version = reader.ReadInt32();

        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported model cache version: {version}. Expected: {Version}");
        }

        int count = reader.ReadInt32();

        GameObject[] objects = new GameObject[count];
        int[] parents = new int[count];

        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();

            parents[i] = reader.ReadInt32();

            Vector3 position = ReadVector3(reader);
            Quaternion rotation = ReadQuaternion(reader);
            Vector3 scale = ReadVector3(reader);

            GameObject obj = new GameObject(name);

            obj.transform.localPosition = position;
            obj.transform.localRotation = rotation;
            obj.transform.localScale = scale;

            bool hasMesh = reader.ReadBoolean();

            if (hasMesh)
            {
                Mesh mesh = ReadMesh(reader);

                MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;

                int materialCount = reader.ReadInt32();
                Material[] materials = new Material[materialCount];

                for (int m = 0; m < materialCount; m++)
                {
                    materials[m] = ReadMaterial(reader);
                }

                MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
            }

            objects[i] = obj;
        }

        for (int i = 0; i < count; i++)
        {
            int parent = parents[i];

            if (parent >= 0)
            {
                objects[i].transform.SetParent(objects[parent].transform, false);
            }
        }

        GameObject importedRoot = objects[0];
        importedRoot.name = rootName;

        return importedRoot;
    }

    private static void WriteMesh(BinaryWriter writer, Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        Vector2[] uv = mesh.uv;
        Color[] colors = mesh.colors;

        writer.Write(vertices.Length);
        foreach (Vector3 value in vertices) WriteVector3(writer, value);

        writer.Write(normals.Length);
        foreach (Vector3 value in normals) WriteVector3(writer, value);

        writer.Write(tangents.Length);
        foreach (Vector4 value in tangents) WriteVector4(writer, value);

        writer.Write(uv.Length);
        foreach (Vector2 value in uv) WriteVector2(writer, value);

        writer.Write(colors.Length);
        foreach (Color value in colors) WriteColor(writer, value);

        writer.Write(mesh.subMeshCount);

        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            int[] indices = mesh.GetIndices(i);

            writer.Write((int)mesh.GetTopology(i));
            writer.Write(indices.Length);

            foreach (int index in indices) writer.Write(index);
        }

        WriteVector3(writer, mesh.bounds.center);
        WriteVector3(writer, mesh.bounds.size);
    }

    private static Mesh ReadMesh(BinaryReader reader)
    {
        Mesh mesh = new Mesh();

        int vertexCount = reader.ReadInt32();

        if (vertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;

        Vector3[] vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertices.Length; i++) vertices[i] = ReadVector3(reader);

        int normalCount = reader.ReadInt32();
        Vector3[] normals = new Vector3[normalCount];
        for (int i = 0; i < normals.Length; i++) normals[i] = ReadVector3(reader);

        int tangentCount = reader.ReadInt32();
        Vector4[] tangents = new Vector4[tangentCount];
        for (int i = 0; i < tangents.Length; i++) tangents[i] = ReadVector4(reader);

        int uvCount = reader.ReadInt32();
        Vector2[] uv = new Vector2[uvCount];
        for (int i = 0; i < uv.Length; i++) uv[i] = ReadVector2(reader);

        int colorCount = reader.ReadInt32();
        Color[] colors = new Color[colorCount];
        for (int i = 0; i < colors.Length; i++) colors[i] = ReadColor(reader);

        mesh.vertices = vertices;

        if (normals.Length == vertices.Length) mesh.normals = normals;
        if (tangents.Length == vertices.Length) mesh.tangents = tangents;
        if (uv.Length == vertices.Length) mesh.uv = uv;
        if (colors.Length == vertices.Length) mesh.colors = colors;

        int subMeshCount = reader.ReadInt32();
        mesh.subMeshCount = subMeshCount;

        for (int i = 0; i < subMeshCount; i++)
        {
            MeshTopology topology = (MeshTopology)reader.ReadInt32();

            int indexCount = reader.ReadInt32();
            int[] indices = new int[indexCount];

            for (int j = 0; j < indices.Length; j++)
            {
                indices[j] = reader.ReadInt32();
            }

            mesh.SetIndices(indices, topology, i);
        }

        Vector3 boundsCenter = ReadVector3(reader);
        Vector3 boundsSize = ReadVector3(reader);

        mesh.bounds = new Bounds(boundsCenter, boundsSize);
        mesh.name = "RuntimeCachedMesh";

        return mesh;
    }

    private static void WriteMaterial(BinaryWriter writer, Material material)
    {
        if (material == null)
        {
            writer.Write(false);
            return;
        }

        writer.Write(true);
        writer.Write(material.name);

        Color color = FindBaseColor(material);
        WriteColor(writer, color);

        TexturePropertyResult textureResult = FindBaseColorTexture(material);

        bool hasTexture = textureResult.Texture != null;
        writer.Write(hasTexture);

        if (!hasTexture)
        {
            Debug.LogWarning(
                $"No base-color texture found for material '{material.name}', shader '{material.shader.name}'."
            );

            return;
        }

        Debug.Log(
            $"Caching material '{material.name}' using texture property " +
            $"'{textureResult.PropertyName}' from shader '{material.shader.name}'."
        );

        Texture2D readable = MakeReadable(textureResult.Texture);

        byte[] png = readable.EncodeToPNG();

        writer.Write(png.Length);
        writer.Write(png);

        Vector2 scale = material.GetTextureScale(textureResult.PropertyName);
        Vector2 offset = material.GetTextureOffset(textureResult.PropertyName);

        WriteVector2(writer, scale);
        WriteVector2(writer, offset);

        UnityEngine.Object.Destroy(readable);
    }

    private static Material ReadMaterial(BinaryReader reader)
    {
        if (!reader.ReadBoolean()) return null;

        string name = reader.ReadString();
        Color color = ReadColor(reader);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            throw new InvalidOperationException(
                "Universal Render Pipeline/Lit shader is not available in the runtime player."
            );
        }

        Material material = new Material(shader)
        {
            name = name
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        bool hasTexture = reader.ReadBoolean();

        if (hasTexture)
        {
            int length = reader.ReadInt32();
            byte[] png = reader.ReadBytes(length);

            Vector2 scale = ReadVector2(reader);
            Vector2 offset = ReadVector2(reader);

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);

            if (!texture.LoadImage(png))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException($"Failed to load cached texture for material '{name}'.");
            }

            texture.name = $"{name}_BaseColor";

            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
        }

        return material;
    }

    private static Color FindBaseColor(Material material)
    {
        string[] preferred =
        {
            "_BaseColorFactor",
            "_BaseColor",
            "_Color",
            "baseColorFactor"
        };

        foreach (string property in preferred)
        {
            if (material.HasProperty(property))
            {
                return material.GetColor(property);
            }
        }

        Shader shader = material.shader;

        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            if (shader.GetPropertyType(i) != ShaderPropertyType.Color) continue;

            string property = shader.GetPropertyName(i);
            string lower = property.ToLowerInvariant();

            if (lower.Contains("base") && lower.Contains("color"))
            {
                return material.GetColor(property);
            }
        }

        return Color.white;
    }

    private static TexturePropertyResult FindBaseColorTexture(Material material)
    {
        string[] preferred =
        {
            "_BaseColorTexture",
            "_BaseColorMap",
            "_BaseMap",
            "_MainTex",
            "baseColorTexture"
        };

        foreach (string property in preferred)
        {
            if (!material.HasProperty(property)) continue;

            Texture texture = material.GetTexture(property);

            if (texture != null)
            {
                return new TexturePropertyResult
                {
                    PropertyName = property,
                    Texture = texture
                };
            }
        }

        Shader shader = material.shader;

        TexturePropertyResult best = default;
        int bestScore = int.MinValue;

        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;

            string propertyName = shader.GetPropertyName(i);
            Texture texture = material.GetTexture(propertyName);

            if (texture == null) continue;

            int score = ScoreTextureProperty(propertyName);

            Debug.Log(
                $"Material '{material.name}' texture property '{propertyName}' " +
                $"contains '{texture.name}', score {score}."
            );

            if (score <= bestScore) continue;

            bestScore = score;

            best = new TexturePropertyResult
            {
                PropertyName = propertyName,
                Texture = texture
            };
        }

        return bestScore > 0 ? best : default;
    }

    private static int ScoreTextureProperty(string propertyName)
    {
        string name = propertyName.ToLowerInvariant();

        if (name.Contains("normal")) return -1000;
        if (name.Contains("metal")) return -900;
        if (name.Contains("rough")) return -900;
        if (name.Contains("occlusion")) return -900;
        if (name.Contains("emission") || name.Contains("emissive")) return -900;

        int score = 0;

        if (name.Contains("base")) score += 100;
        if (name.Contains("color") || name.Contains("colour")) score += 100;
        if (name.Contains("albedo")) score += 180;
        if (name.Contains("diffuse")) score += 160;
        if (name.Contains("main")) score += 80;

        return score;
    }

    private static Texture2D MakeReadable(Texture source)
    {
        int width = source.width;
        int height = source.height;

        RenderTexture temporary = RenderTexture.GetTemporary(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32
        );

        RenderTexture previous = RenderTexture.active;

        Graphics.Blit(source, temporary);
        RenderTexture.active = temporary;

        Texture2D readable = new Texture2D(
            width,
            height,
            TextureFormat.RGBA32,
            false
        );

        readable.ReadPixels(
            new Rect(0, 0, width, height),
            0,
            0
        );

        readable.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temporary);

        return readable;
    }

    private static void WriteVector2(BinaryWriter writer, Vector2 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
    }

    private static Vector2 ReadVector2(BinaryReader reader)
    {
        return new Vector2(
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
    }

    private static Vector3 ReadVector3(BinaryReader reader)
    {
        return new Vector3(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    private static void WriteVector4(BinaryWriter writer, Vector4 value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }

    private static Vector4 ReadVector4(BinaryReader reader)
    {
        return new Vector4(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
    {
        writer.Write(value.x);
        writer.Write(value.y);
        writer.Write(value.z);
        writer.Write(value.w);
    }

    private static Quaternion ReadQuaternion(BinaryReader reader)
    {
        return new Quaternion(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    private static void WriteColor(BinaryWriter writer, Color value)
    {
        writer.Write(value.r);
        writer.Write(value.g);
        writer.Write(value.b);
        writer.Write(value.a);
    }

    private static Color ReadColor(BinaryReader reader)
    {
        return new Color(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    private struct TexturePropertyResult
    {
        public string PropertyName;
        public Texture Texture;
    }
}