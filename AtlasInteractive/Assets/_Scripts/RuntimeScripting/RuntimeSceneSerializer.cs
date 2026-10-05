using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

public sealed class RuntimeSceneSerializer : MonoBehaviour
{
    [SerializeField] private RuntimeSceneRegistry _sceneRegistry;
    [SerializeField] private RuntimeAssetService _assetService;

    private string _sceneDirectory;

    private void Awake()
    {
        if (_sceneRegistry == null) throw new InvalidOperationException("RuntimeSceneRegistry is required.");
        if (_assetService == null) throw new InvalidOperationException("RuntimeAssetService is required.");

        _sceneDirectory = Path.Combine(Application.persistentDataPath, "Scenes");

        Directory.CreateDirectory(_sceneDirectory);
    }

    public string Save(string fileName)
    {
        RuntimeSceneData sceneData = new RuntimeSceneData();

        foreach (RuntimeSceneObject obj in _sceneRegistry.Objects)
        {
            RuntimeObjectData objectData = new RuntimeObjectData
            {
                id = obj.Id,
                name = obj.name,
                kind = obj.Kind.ToString(),
                source = obj.Source,
                textureSource = obj.TextureSource,
                audioSource = obj.AudioSourcePath,
                position = obj.transform.localPosition,
                rotation = obj.transform.localRotation,
                scale = obj.transform.localScale
            };

            RuntimeSceneObject parent =
                obj.transform.parent != null
                    ? obj.transform.parent.GetComponent<RuntimeSceneObject>()
                    : null;

            objectData.parentId = parent != null ? parent.Id : "";

            foreach (LuaBehaviour script in obj.GetComponents<LuaBehaviour>())
            {
                objectData.scripts.Add(script.ScriptName);
            }

            Rigidbody rigidbody = obj.GetComponent<Rigidbody>();

            if (rigidbody != null)
            {
                objectData.components.Add(new RuntimeComponentData
                {
                    type = "Rigidbody",
                    mass = rigidbody.mass,
                    useGravity = rigidbody.useGravity,
                    isKinematic = rigidbody.isKinematic
                });
            }

            Light light = obj.GetComponent<Light>();

            if (light != null)
            {
                objectData.components.Add(new RuntimeComponentData
                {
                    type = "Light",
                    intensity = light.intensity,
                    range = light.range,
                    color = light.color
                });
            }

            AudioSource audioSource = obj.GetComponent<AudioSource>();

            if (audioSource != null)
            {
                objectData.components.Add(new RuntimeComponentData
                {
                    type = "AudioSource",
                    volume = audioSource.volume,
                    loop = audioSource.loop,
                    spatialBlend = audioSource.spatialBlend,
                    wasPlaying = audioSource.isPlaying
                });
            }

            sceneData.objects.Add(objectData);
        }

        string path = GetScenePath(fileName);

        File.WriteAllText(
            path,
            JsonUtility.ToJson(sceneData, true)
        );

        Debug.Log($"Scene saved: {path}");

        return path;
    }

    public async Task LoadAsync(string fileName)
    {
        string path = GetScenePath(fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Scene not found: {path}");
        }

        RuntimeSceneData sceneData =
            JsonUtility.FromJson<RuntimeSceneData>(
                File.ReadAllText(path)
            );

        if (sceneData == null)
        {
            throw new InvalidDataException($"Invalid scene file: {path}");
        }

        if (sceneData.objects == null)
        {
            sceneData.objects = new List<RuntimeObjectData>();
        }

        _sceneRegistry.Clear();

        // Destroy() is deferred until end of frame.
        // We must not start rebuilding the runtime scene while old objects still exist.
        await Awaitable.NextFrameAsync();

        // PASS 1:
        // Create every object first.
        foreach (RuntimeObjectData objectData in sceneData.objects)
        {
            await CreateObject(objectData);
        }

        // PASS 2:
        // Restore complete hierarchy only after every referenced object exists.
        foreach (RuntimeObjectData objectData in sceneData.objects)
        {
            RuntimeSceneObject runtimeObject = RequireLoadedObject(objectData.id);

            if (!string.IsNullOrWhiteSpace(objectData.parentId))
            {
                _sceneRegistry.SetParent(
                    objectData.id,
                    objectData.parentId
                );
            }

            runtimeObject.transform.localPosition = objectData.position;
            runtimeObject.transform.localRotation = objectData.rotation;
            runtimeObject.transform.localScale = objectData.scale;
        }

        // PASS 3:
        // Restore components and external assets.
        foreach (RuntimeObjectData objectData in sceneData.objects)
        {
            RuntimeSceneObject runtimeObject = RequireLoadedObject(objectData.id);

            if (objectData.components != null)
            {
                foreach (RuntimeComponentData component in objectData.components)
                {
                    RestoreComponent(runtimeObject, component);
                }
            }

            if (!string.IsNullOrWhiteSpace(objectData.textureSource))
            {
                _assetService.ApplyTexture(
                    objectData.id,
                    objectData.textureSource
                );
            }

            if (!string.IsNullOrWhiteSpace(objectData.audioSource))
            {
                RestoreAudio(
                    runtimeObject,
                    objectData
                );
            }
        }

        // PASS 4:
        // Scripts are restored LAST so start()/update() sees the final scene state.
        foreach (RuntimeObjectData objectData in sceneData.objects)
        {
            RuntimeSceneObject runtimeObject = RequireLoadedObject(objectData.id);

            if (objectData.scripts == null) continue;

            foreach (string scriptName in objectData.scripts)
            {
                LuaBehaviour existing = FindLuaBehaviour(runtimeObject, scriptName);

                if (existing != null)
                {
                    existing.ReloadNow();
                    continue;
                }

                LuaBehaviour behaviour =
                    runtimeObject.gameObject.AddComponent<LuaBehaviour>();

                behaviour.Configure(
                    scriptName,
                    _sceneRegistry,
                    this
                );
            }
        }

        Debug.Log($"Scene loaded successfully: {path}");
    }

    private async Task CreateObject(RuntimeObjectData objectData)
    {
        RuntimeObjectKind kind =
            Enum.Parse<RuntimeObjectKind>(
                objectData.kind,
                true
            );

        RuntimeSceneObject runtimeObject;

        switch (kind)
        {
            case RuntimeObjectKind.Primitive:
                {
                    PrimitiveType primitiveType =
                        Enum.Parse<PrimitiveType>(
                            objectData.source,
                            true
                        );

                    runtimeObject =
                        _sceneRegistry.SpawnPrimitive(
                            objectData.id,
                            objectData.name,
                            primitiveType,
                            Vector3.zero
                        );

                    break;
                }

            case RuntimeObjectKind.Model:
                {
                    await _assetService.LoadModel(
                        objectData.source,
                        objectData.name,
                        Vector3.zero,
                        objectData.id
                    );

                    runtimeObject =
                        RequireLoadedObject(
                            objectData.id
                        );

                    break;
                }

            default:
                throw new NotSupportedException(
                    $"Unsupported runtime object kind '{kind}'."
                );
        }

        runtimeObject.transform.localPosition = objectData.position;
        runtimeObject.transform.localRotation = objectData.rotation;
        runtimeObject.transform.localScale = objectData.scale;
    }

    private void RestoreAudio(RuntimeSceneObject runtimeObject, RuntimeObjectData objectData)
    {
        RuntimeComponentData audioData =
            objectData.components?.Find(
                component =>
                    string.Equals(
                        component.type,
                        "AudioSource",
                        StringComparison.OrdinalIgnoreCase
                    )
            );

        AudioSource source =
            runtimeObject.gameObject.GetComponent<AudioSource>();

        if (source == null)
        {
            source =
                runtimeObject.gameObject.AddComponent<AudioSource>();
        }

        AudioClip clip =
            _assetService.LoadAudio(
                objectData.audioSource
            );

        source.clip = clip;

        if (audioData != null)
        {
            source.volume = audioData.volume;
            source.loop = audioData.loop;
            source.spatialBlend = audioData.spatialBlend;
        }
        else
        {
            source.volume = 1f;
            source.loop = false;
            source.spatialBlend = 0f;
        }

        runtimeObject.SetAudioSourcePath(
            objectData.audioSource
        );

        if (audioData != null && audioData.wasPlaying)
        {
            source.Play();
        }
    }

    private static void RestoreComponent(RuntimeSceneObject runtimeObject, RuntimeComponentData data)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.type))
        {
            return;
        }

        switch (data.type.ToLowerInvariant())
        {
            case "rigidbody":
                {
                    Rigidbody rigidbody =
                        runtimeObject.gameObject.GetComponent<Rigidbody>();

                    if (rigidbody == null)
                    {
                        rigidbody =
                            runtimeObject.gameObject.AddComponent<Rigidbody>();
                    }

                    rigidbody.mass = data.mass;
                    rigidbody.useGravity = data.useGravity;
                    rigidbody.isKinematic = data.isKinematic;

                    return;
                }

            case "light":
                {
                    Light light =
                        runtimeObject.gameObject.GetComponent<Light>();

                    if (light == null)
                    {
                        light =
                            runtimeObject.gameObject.AddComponent<Light>();
                    }

                    light.intensity = data.intensity;
                    light.range = data.range;
                    light.color = data.color;

                    return;
                }

            case "audiosource":
                {
                    AudioSource source =
                        runtimeObject.gameObject.GetComponent<AudioSource>();

                    if (source == null)
                    {
                        source =
                            runtimeObject.gameObject.AddComponent<AudioSource>();
                    }

                    source.volume = data.volume;
                    source.loop = data.loop;
                    source.spatialBlend = data.spatialBlend;

                    return;
                }

            default:
                throw new NotSupportedException(
                    $"Unsupported saved component type '{data.type}'."
                );
        }
    }

    private RuntimeSceneObject RequireLoadedObject(string id)
    {
        RuntimeSceneObject runtimeObject =
            _sceneRegistry.Get(id);

        if (runtimeObject == null)
        {
            throw new InvalidOperationException(
                $"Scene load failed. Runtime object '{id}' was not created."
            );
        }

        return runtimeObject;
    }

    private static LuaBehaviour FindLuaBehaviour(RuntimeSceneObject runtimeObject, string scriptName)
    {
        foreach (LuaBehaviour behaviour in runtimeObject.GetComponents<LuaBehaviour>())
        {
            if (string.Equals(
                    behaviour.ScriptName,
                    scriptName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return behaviour;
            }
        }

        return null;
    }

    private string GetScenePath(string fileName)
    {
        if (Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException(
                "Scene file name cannot contain a directory path."
            );
        }

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".json";
        }

        return Path.Combine(
            _sceneDirectory,
            fileName
        );
    }
}

[Serializable]
public sealed class RuntimeSceneData
{
    public List<RuntimeObjectData> objects = new();
}

[Serializable]
public sealed class RuntimeObjectData
{
    public string id;
    public string name;
    public string kind;
    public string source;
    public string textureSource;
    public string audioSource;
    public string parentId;

    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;

    public List<string> scripts = new();
    public List<RuntimeComponentData> components = new();
}

[Serializable]
public sealed class RuntimeComponentData
{
    public string type;

    public float mass;
    public bool useGravity;
    public bool isKinematic;

    public float intensity;
    public float range;
    public Color color;

    public float volume;
    public bool loop;
    public float spatialBlend;
    public bool wasPlaying;
}