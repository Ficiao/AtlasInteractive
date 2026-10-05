using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

public sealed class RuntimeAssetService : MonoBehaviour
{
    private const int ModelCacheVersion = 2;
    private const int TextureCacheVersion = 1;
    private const int AudioCacheVersion = 1;

    [SerializeField] private RuntimeSceneRegistry _sceneRegistry;

    private readonly Dictionary<string, AudioClip> _loadedAudio = new();

    public string SourceRoot { get; private set; }
    public string BinaryRoot { get; private set; }

    private void Awake()
    {
        if (_sceneRegistry == null)
        {
            throw new InvalidOperationException("RuntimeSceneRegistry is required.");
        }

        string root = Path.Combine(Application.persistentDataPath, "Assets");

        SourceRoot = Path.Combine(root, "src");
        BinaryRoot = Path.Combine(root, "bin");

        Directory.CreateDirectory(Path.Combine(SourceRoot, "Models"));
        Directory.CreateDirectory(Path.Combine(SourceRoot, "Textures"));
        Directory.CreateDirectory(Path.Combine(SourceRoot, "Audio"));

        Directory.CreateDirectory(Path.Combine(BinaryRoot, "Models"));
        Directory.CreateDirectory(Path.Combine(BinaryRoot, "Textures"));
        Directory.CreateDirectory(Path.Combine(BinaryRoot, "Audio"));

        Debug.Log($"Runtime asset source root: {SourceRoot}");
        Debug.Log($"Runtime asset binary root: {BinaryRoot}");
    }

    public async Task<string> LoadModel(string relativePath, string objectName, Vector3 position, string id = null)
    {
        string sourcePath = ResolveSourcePath(relativePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Model not found: {sourcePath}");
        }

        string cachePath = GetCachePath(relativePath);
        string metadataPath = cachePath + ".meta.json";
        string sourceHash = CalculateSHA256(sourcePath);

        GameObject root;

        if (IsCacheValid(cachePath, metadataPath, sourceHash, ModelCacheVersion))
        {
            Debug.Log($"Loading cached model: {cachePath}");

            root = RuntimeModelCache.Load(cachePath, objectName);
        }
        else
        {
            Debug.Log($"Importing source model: {sourcePath}");

            root = await ImportGLB(sourcePath, objectName);

            Debug.Log($"Writing model cache: {cachePath}");

            RuntimeModelCache.Save(root, cachePath);
            WriteMetadata(metadataPath, sourceHash, ModelCacheVersion);
        }

        root.transform.position = position;

        RuntimeSceneObject runtimeObject = root.AddComponent<RuntimeSceneObject>();

        id ??= Guid.NewGuid().ToString("N");

        runtimeObject.Initialize(id, RuntimeObjectKind.Model, relativePath, _sceneRegistry);
        _sceneRegistry.Register(runtimeObject);

        return id;
    }

    public Texture2D LoadTexture(string relativePath)
    {
        string sourcePath = ResolveSourcePath(relativePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Texture not found: {sourcePath}");
        }

        string cachePath = GetCachePath(relativePath);
        string metadataPath = cachePath + ".meta.json";
        string sourceHash = CalculateSHA256(sourcePath);

        Texture2D texture;

        if (IsCacheValid(cachePath, metadataPath, sourceHash, TextureCacheVersion))
        {
            Debug.Log($"Loading cached texture: {cachePath}");

            texture = RuntimeTextureCache.Load(cachePath);
        }
        else
        {
            Debug.Log($"Importing source texture: {sourcePath}");

            byte[] data = File.ReadAllBytes(sourcePath);

            texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);

            if (!texture.LoadImage(data, false))
            {
                Destroy(texture);
                throw new InvalidOperationException($"Failed to load texture: {sourcePath}");
            }

            Debug.Log($"Writing texture cache: {cachePath}");

            RuntimeTextureCache.Save(texture, cachePath);
            WriteMetadata(metadataPath, sourceHash, TextureCacheVersion);
        }

        texture.name = Path.GetFileNameWithoutExtension(relativePath);

        return texture;
    }

    public AudioClip LoadAudio(string relativePath)
    {
        string sourcePath = ResolveSourcePath(relativePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Audio file not found: {sourcePath}");
        }

        string sourceHash = CalculateSHA256(sourcePath);
        string memoryKey = $"{relativePath}|{sourceHash}";

        if (_loadedAudio.TryGetValue(memoryKey, out AudioClip existing) && existing != null)
        {
            return existing;
        }

        string cachePath = GetCachePath(relativePath);
        string metadataPath = cachePath + ".meta.json";

        AudioClip clip;

        if (IsCacheValid(cachePath, metadataPath, sourceHash, AudioCacheVersion))
        {
            Debug.Log($"Loading cached audio: {cachePath}");

            clip = RuntimeWavCache.Load(
                cachePath,
                Path.GetFileNameWithoutExtension(relativePath)
            );
        }
        else
        {
            Debug.Log($"Importing source audio: {sourcePath}");

            clip = RuntimeWavCache.ImportSource(sourcePath);

            Debug.Log($"Writing audio cache: {cachePath}");

            RuntimeWavCache.Save(clip, cachePath);
            WriteMetadata(metadataPath, sourceHash, AudioCacheVersion);
        }

        _loadedAudio[memoryKey] = clip;

        return clip;
    }

    public void ApplyTexture(string objectId, string relativePath)
    {
        RuntimeSceneObject runtimeObject = _sceneRegistry.Get(objectId);

        if (runtimeObject == null)
        {
            throw new InvalidOperationException($"Runtime object not found: {objectId}");
        }

        Texture2D texture = LoadTexture(relativePath);

        Renderer[] renderers = runtimeObject.GetComponentsInChildren<Renderer>(true);

        if (renderers.Length == 0)
        {
            throw new InvalidOperationException($"Object '{objectId}' has no renderers.");
        }

        foreach (Renderer renderer in renderers)
        {
            Material[] materials = renderer.materials;

            foreach (Material material in materials)
            {
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", texture);
                }
                else if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", texture);
                }
            }
        }

        runtimeObject.SetTextureSource(relativePath);
    }

    public void ApplyAudio(string objectId, string relativePath, bool play, bool loop, float volume)
    {
        RuntimeSceneObject runtimeObject = _sceneRegistry.Get(objectId);

        if (runtimeObject == null)
        {
            throw new InvalidOperationException($"Runtime object not found: {objectId}");
        }

        AudioClip clip = LoadAudio(relativePath);

        AudioSource source = runtimeObject.GetComponent<AudioSource>();

        if (source == null)
        {
            source = runtimeObject.gameObject.AddComponent<AudioSource>();
        }

        source.clip = clip;
        source.loop = loop;
        source.volume = Mathf.Clamp01(volume);

        runtimeObject.SetAudioSourcePath(relativePath);

        if (play)
        {
            source.Play();
        }
    }

    private async Task<GameObject> ImportGLB(string sourcePath, string objectName)
    {
        byte[] data = await File.ReadAllBytesAsync(sourcePath);

        GltfImport gltf = new GltfImport();

        bool loaded = await gltf.Load(data);

        if (!loaded)
        {
            gltf.Dispose();
            throw new InvalidOperationException($"Failed to load GLB: {sourcePath}");
        }

        GameObject root = new GameObject(objectName);

        bool instantiated = await gltf.InstantiateMainSceneAsync(root.transform);

        if (!instantiated)
        {
            Destroy(root);
            gltf.Dispose();
            throw new InvalidOperationException($"Failed to instantiate GLB: {sourcePath}");
        }

        return root;
    }

    private bool IsCacheValid(string cachePath, string metadataPath, string sourceHash, int expectedVersion)
    {
        if (!File.Exists(cachePath)) return false;
        if (!File.Exists(metadataPath)) return false;

        RuntimeAssetCacheMetadata metadata =
            JsonUtility.FromJson<RuntimeAssetCacheMetadata>(File.ReadAllText(metadataPath));

        if (metadata == null) return false;
        if (metadata.cacheVersion != expectedVersion) return false;

        return metadata.sourceHash == sourceHash;
    }

    private static void WriteMetadata(string metadataPath, string sourceHash, int cacheVersion)
    {
        RuntimeAssetCacheMetadata metadata = new RuntimeAssetCacheMetadata
        {
            sourceHash = sourceHash,
            cacheVersion = cacheVersion
        };

        File.WriteAllText(
            metadataPath,
            JsonUtility.ToJson(metadata, true)
        );
    }

    private string GetCachePath(string relativePath)
    {
        string withoutExtension = Path.ChangeExtension(relativePath, ".bin");
        string path = Path.Combine(BinaryRoot, withoutExtension);

        string directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return path;
    }

    private string ResolveSourcePath(string relativePath)
    {
        string root = Path.GetFullPath(SourceRoot);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            path != root)
        {
            throw new UnauthorizedAccessException($"Path escapes asset root: {relativePath}");
        }

        return path;
    }

    private static string CalculateSHA256(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);

        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
}