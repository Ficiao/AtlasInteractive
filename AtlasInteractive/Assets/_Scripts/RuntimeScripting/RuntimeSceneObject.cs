using System;
using UnityEngine;

public enum RuntimeObjectKind
{
    Primitive,
    Model
}

public sealed class RuntimeSceneObject : MonoBehaviour
{
    [field: SerializeField] public string Id { get; private set; }
    [field: SerializeField] public RuntimeObjectKind Kind { get; private set; }
    [field: SerializeField] public string Source { get; private set; }
    [field: SerializeField] public string TextureSource { get; private set; }
    [field: SerializeField] public string AudioSourcePath { get; private set; }

    private RuntimeSceneRegistry _registry;

    public void Initialize(string id, RuntimeObjectKind kind, string source, RuntimeSceneRegistry registry)
    {
        if (!string.IsNullOrEmpty(Id))
        {
            throw new InvalidOperationException("RuntimeSceneObject is already initialized.");
        }

        Id = id;
        Kind = kind;
        Source = source;
        _registry = registry;
    }

    public void SetTextureSource(string source)
    {
        TextureSource = source;
    }

    public void SetAudioSourcePath(string source)
    {
        AudioSourcePath = source;
    }

    private void OnDestroy()
    {
        _registry?.Unregister(this);
    }
}