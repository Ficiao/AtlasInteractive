using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class RuntimeSceneRegistry : MonoBehaviour
{
    [SerializeField] private Material _defaultMaterial;

    private readonly Dictionary<string, RuntimeSceneObject> _objects = new();

    public IEnumerable<RuntimeSceneObject> Objects => _objects.Values;

    private void Awake()
    {
        if (_defaultMaterial == null)
        {
            throw new InvalidOperationException("Runtime default material is required.");
        }
    }

    public string SpawnCube(string name, Vector3 position)
    {
        return SpawnPrimitive(name, PrimitiveType.Cube, position).Id;
    }

    public RuntimeSceneObject SpawnPrimitive(string name, PrimitiveType primitiveType, Vector3 position)
    {
        return SpawnPrimitive(Guid.NewGuid().ToString("N"), name, primitiveType, position);
    }

    public RuntimeSceneObject SpawnPrimitive(string id, string name, PrimitiveType primitiveType, Vector3 position)
    {
        GameObject obj = GameObject.CreatePrimitive(primitiveType);

        obj.name = name;
        obj.transform.position = position;

        Renderer renderer = obj.GetComponent<Renderer>();

        if (renderer != null)
        {
            renderer.sharedMaterial = _defaultMaterial;
        }

        RuntimeSceneObject runtimeObject = obj.AddComponent<RuntimeSceneObject>();
        runtimeObject.Initialize(id, RuntimeObjectKind.Primitive, primitiveType.ToString(), this);

        Register(runtimeObject);

        return runtimeObject;
    }

    public RuntimeSceneObject Get(string id)
    {
        return _objects.TryGetValue(id, out RuntimeSceneObject obj) ? obj : null;
    }

    public bool Exists(string id)
    {
        return _objects.ContainsKey(id);
    }

    public void SetPosition(string id, Vector3 position)
    {
        RuntimeSceneObject obj = Get(id);
        if (obj == null) return;

        obj.transform.position = position;
    }

    public void SetRotation(string id, Vector3 rotation)
    {
        RuntimeSceneObject obj = Get(id);
        if (obj == null) return;

        obj.transform.eulerAngles = rotation;
    }

    public void SetScale(string id, Vector3 scale)
    {
        RuntimeSceneObject obj = Get(id);
        if (obj == null) return;

        obj.transform.localScale = scale;
    }

    public void Rename(string id, string name)
    {
        RuntimeSceneObject obj = Get(id);
        if (obj == null) return;

        obj.name = name;
    }

    public void SetParent(string childId, string parentId)
    {
        RuntimeSceneObject child = Get(childId);

        if (child == null)
        {
            throw new InvalidOperationException($"Runtime child object not found: {childId}");
        }

        if (string.IsNullOrWhiteSpace(parentId))
        {
            child.transform.SetParent(null, true);
            return;
        }

        RuntimeSceneObject parent = Get(parentId);

        if (parent == null)
        {
            throw new InvalidOperationException($"Runtime parent object not found: {parentId}");
        }

        if (child == parent)
        {
            throw new InvalidOperationException("An object cannot be parented to itself.");
        }

        if (parent.transform.IsChildOf(child.transform))
        {
            throw new InvalidOperationException("Parenting would create a transform cycle.");
        }

        child.transform.SetParent(parent.transform, true);
    }

    public void DestroyObject(string id)
    {
        RuntimeSceneObject obj = Get(id);
        if (obj == null) return;

        UnityEngine.Object.Destroy(obj.gameObject);
    }

    public void Clear()
    {
        RuntimeSceneObject[] objects = _objects.Values.ToArray();

        _objects.Clear();

        foreach (RuntimeSceneObject obj in objects)
        {
            if (obj != null)
            {
                UnityEngine.Object.Destroy(obj.gameObject);
            }
        }
    }

    public void Register(RuntimeSceneObject obj)
    {
        if (!_objects.TryAdd(obj.Id, obj))
        {
            throw new InvalidOperationException($"Duplicate runtime object ID: {obj.Id}");
        }
    }

    public void Unregister(RuntimeSceneObject obj)
    {
        if (_objects.TryGetValue(obj.Id, out RuntimeSceneObject registered) && registered == obj)
        {
            _objects.Remove(obj.Id);
        }
    }
}