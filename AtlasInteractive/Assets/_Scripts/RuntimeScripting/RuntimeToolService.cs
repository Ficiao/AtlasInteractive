using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public sealed class RuntimeToolService : MonoBehaviour
{
    [SerializeField] private RuntimeSceneRegistry _sceneRegistry;
    [SerializeField] private RuntimeSceneSerializer _sceneSerializer;
    [SerializeField] private RuntimeAssetService _assetService;
    [SerializeField] private RuntimeFileService _fileService;

    private void Awake()
    {
        if (_sceneRegistry == null) throw new InvalidOperationException("RuntimeSceneRegistry is required.");
        if (_sceneSerializer == null) throw new InvalidOperationException("RuntimeSceneSerializer is required.");
        if (_assetService == null) throw new InvalidOperationException("RuntimeAssetService is required.");
        if (_fileService == null) throw new InvalidOperationException("RuntimeFileService is required.");
    }

    public string CreateCube(string name, float x, float y, float z)
    {
        return _sceneRegistry.SpawnCube(name, new Vector3(x, y, z));
    }

    public string CreatePrimitive(string primitiveType, string name, float x, float y, float z)
    {
        PrimitiveType type = Enum.Parse<PrimitiveType>(primitiveType, true);
        return _sceneRegistry.SpawnPrimitive(name, type, new Vector3(x, y, z)).Id;
    }

    public async Task<string> LoadModel(string path, string name, float x, float y, float z)
    {
        path = NormalizeAssetPath(path);
        return await _assetService.LoadModel(path, name, new Vector3(x, y, z));
    }

    public string[] ListModels()
    {
        return ListAssetFiles("Models", "*.glb");
    }

    public string[] ListTextures()
    {
        return ListAssetFiles("Textures", "*.png");
    }

    public string[] ListAudio()
    {
        return ListAssetFiles("Audio", "*.wav");
    }

    public string ResolveObjectId(string reference)
    {
        return RequireObject(reference).Id;
    }

    public void SetPosition(string reference, float x, float y, float z)
    {
        RequireObject(reference).transform.position = new Vector3(x, y, z);
    }

    public void SetRotation(string reference, float x, float y, float z)
    {
        RequireObject(reference).transform.eulerAngles = new Vector3(x, y, z);
    }

    public void SetScale(string reference, float x, float y, float z)
    {
        RequireObject(reference).transform.localScale = new Vector3(x, y, z);
    }

    public void RenameObject(string reference, string name)
    {
        RequireObject(reference).name = name;
    }

    public void SetParent(string childReference, string parentReference)
    {
        RuntimeSceneObject child = RequireObject(childReference);

        if (string.IsNullOrWhiteSpace(parentReference))
        {
            _sceneRegistry.SetParent(child.Id, "");
            return;
        }

        RuntimeSceneObject parent = RequireObject(parentReference);
        _sceneRegistry.SetParent(child.Id, parent.Id);
    }

    public void DestroyObject(string reference)
    {
        RuntimeSceneObject runtimeObject = RequireObject(reference);
        UnityEngine.Object.Destroy(runtimeObject.gameObject);
    }

    public void SetTexture(string reference, string path)
    {
        RuntimeSceneObject runtimeObject = RequireObject(reference);
        path = NormalizeAssetPath(path);
        _assetService.ApplyTexture(runtimeObject.Id, path);
    }

    public void PlayAudio(string reference, string path, bool loop, float volume)
    {
        RuntimeSceneObject runtimeObject = RequireObject(reference);
        path = NormalizeAssetPath(path);
        _assetService.ApplyAudio(runtimeObject.Id, path, true, loop, volume);
    }

    public RuntimeRigidbodyState ConfigureRigidbody(string reference, float mass, bool useGravity, bool isKinematic)
    {
        RuntimeSceneObject runtimeObject = RequireObject(reference);

        Rigidbody body = runtimeObject.GetComponent<Rigidbody>();

        if (body == null)
        {
            body = runtimeObject.gameObject.AddComponent<Rigidbody>();
        }

        body.mass = mass;
        body.useGravity = useGravity;
        body.isKinematic = isKinematic;

        return new RuntimeRigidbodyState
        {
            objectId = runtimeObject.Id,
            objectName = runtimeObject.name,
            mass = body.mass,
            useGravity = body.useGravity,
            isKinematic = body.isKinematic
        };
    }

    public RuntimeRotationScriptState AttachRotationScript(string reference, float degreesPerSecond)
    {
        RuntimeSceneObject runtimeObject = RequireObject(reference);

        string speed = degreesPerSecond.ToString(CultureInfo.InvariantCulture);
        string scriptName = $"Rotate_{runtimeObject.Id}.lua";

        string source =
            "function start() end\n" +
            $"function update(dt) rotate(0, {speed} * dt, 0) end";

        CreateScript(scriptName, source);
        AttachScript(runtimeObject.Id, scriptName);

        return new RuntimeRotationScriptState
        {
            objectId = runtimeObject.Id,
            objectName = runtimeObject.name,
            scriptName = scriptName,
            degreesPerSecond = degreesPerSecond
        };
    }

    public string SaveScene(string fileName)
    {
        return _sceneSerializer.Save(fileName);
    }

    public async Task LoadScene(string fileName)
    {
        await _sceneSerializer.LoadAsync(fileName);
    }

    public string ReadFile(string path)
    {
        return _fileService.ReadText(path);
    }

    public void WriteFile(string path, string content)
    {
        _fileService.WriteText(path, content);
    }

    public string[] FindFiles(string pattern)
    {
        return _fileService.Find(pattern);
    }

    public void CreateScript(string scriptName, string source)
    {
        scriptName = NormalizeScriptName(scriptName);

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Lua source cannot be empty.");
        }

        _fileService.WriteText($"Scripts/{scriptName}", source);
    }

    public void AttachScript(string objectReference, string scriptName)
    {
        RuntimeSceneObject runtimeObject = RequireObject(objectReference);
        scriptName = NormalizeScriptName(scriptName);

        string scriptPath = $"Scripts/{scriptName}";

        if (!_fileService.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Lua script does not exist: {scriptPath}");
        }

        foreach (LuaBehaviour existing in runtimeObject.GetComponents<LuaBehaviour>())
        {
            if (existing.ScriptName != scriptName) continue;

            existing.ReloadNow();
            return;
        }

        LuaBehaviour behaviour = runtimeObject.gameObject.AddComponent<LuaBehaviour>();

        try
        {
            behaviour.Configure(scriptName, _sceneRegistry, _sceneSerializer);
        }
        catch
        {
            UnityEngine.Object.Destroy(behaviour);
            throw;
        }
    }

    public void CreateAndAttachScript(string objectReference, string scriptName, string source)
    {
        RequireObject(objectReference);

        scriptName = NormalizeScriptName(scriptName);

        CreateScript(scriptName, source);
        AttachScript(objectReference, scriptName);
    }

    public string GetSceneState()
    {
        RuntimeSceneState state = new RuntimeSceneState();

        foreach (RuntimeSceneObject obj in _sceneRegistry.Objects)
        {
            RuntimeObjectState objectState = new RuntimeObjectState
            {
                id = obj.Id,
                name = obj.name,
                kind = obj.Kind.ToString(),
                source = obj.Source,
                textureSource = obj.TextureSource,
                audioSource = obj.AudioSourcePath,
                position = obj.transform.position,
                rotation = obj.transform.eulerAngles,
                scale = obj.transform.localScale
            };

            RuntimeSceneObject parent = obj.transform.parent != null
                ? obj.transform.parent.GetComponent<RuntimeSceneObject>()
                : null;

            objectState.parentId = parent != null ? parent.Id : "";

            foreach (LuaBehaviour script in obj.GetComponents<LuaBehaviour>())
            {
                objectState.scripts.Add(script.ScriptName);
            }

            Rigidbody rigidbody = obj.GetComponent<Rigidbody>();

            if (rigidbody != null)
            {
                objectState.components.Add("Rigidbody");
                objectState.hasRigidbody = true;
                objectState.rigidbodyMass = rigidbody.mass;
                objectState.rigidbodyUseGravity = rigidbody.useGravity;
                objectState.rigidbodyIsKinematic = rigidbody.isKinematic;
            }

            if (obj.GetComponent<Light>() != null) objectState.components.Add("Light");
            if (obj.GetComponent<AudioSource>() != null) objectState.components.Add("AudioSource");

            state.objects.Add(objectState);
        }

        return JsonUtility.ToJson(state, true);
    }

    private string[] ListAssetFiles(string directoryName, string searchPattern)
    {
        string directory = Path.Combine(_assetService.SourceRoot, directoryName);

        if (!Directory.Exists(directory)) return Array.Empty<string>();

        return Directory.GetFiles(directory, searchPattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_assetService.SourceRoot, path).Replace('\\', '/'))
            .OrderBy(path => path)
            .ToArray();
    }

    private RuntimeSceneObject RequireObject(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("Runtime object reference cannot be empty.");
        }

        RuntimeSceneObject byId = _sceneRegistry.Get(reference);

        if (byId != null) return byId;

        List<RuntimeSceneObject> matches = _sceneRegistry.Objects
            .Where(obj => obj != null && string.Equals(obj.name, reference, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 1) return matches[0];

        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"Object name '{reference}' is ambiguous. Use its runtime ID.");
        }

        throw new InvalidOperationException($"Runtime object not found by ID or exact name: {reference}");
    }

    private static string NormalizeScriptName(string scriptName)
    {
        if (string.IsNullOrWhiteSpace(scriptName))
        {
            throw new ArgumentException("Script name cannot be empty.");
        }

        return scriptName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? scriptName
            : scriptName + ".lua";
    }

    private static string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Asset path cannot be empty.");
        }

        path = path.Trim().Replace('\\', '/');

        while (path.StartsWith("./", StringComparison.Ordinal)) path = path.Substring(2);

        path = path.TrimStart('/');

        if (path.StartsWith("Assets/src/", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring("Assets/src/".Length);
        }
        else if (path.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring("src/".Length);
        }

        return path;
    }
}

[Serializable]
public sealed class RuntimeRigidbodyState
{
    public string objectId;
    public string objectName;
    public float mass;
    public bool useGravity;
    public bool isKinematic;
}

[Serializable]
public sealed class RuntimeRotationScriptState
{
    public string objectId;
    public string objectName;
    public string scriptName;
    public float degreesPerSecond;
}

[Serializable]
public sealed class RuntimeSceneState
{
    public List<RuntimeObjectState> objects = new();
}

[Serializable]
public sealed class RuntimeObjectState
{
    public string id;
    public string name;
    public string kind;
    public string source;
    public string textureSource;
    public string audioSource;
    public string parentId;

    public Vector3 position;
    public Vector3 rotation;
    public Vector3 scale;

    public List<string> scripts = new();
    public List<string> components = new();

    public bool hasRigidbody;
    public float rigidbodyMass;
    public bool rigidbodyUseGravity;
    public bool rigidbodyIsKinematic;
}