using System;
using MoonSharp.Interpreter;
using UnityEngine;

public sealed class RuntimeScriptAPI
{
    private readonly GameObject _gameObject;
    private readonly RuntimeSceneRegistry _sceneRegistry;
    private readonly RuntimeSceneSerializer _sceneSerializer;

    public RuntimeScriptAPI(GameObject gameObject, RuntimeSceneRegistry sceneRegistry, RuntimeSceneSerializer sceneSerializer)
    {
        _gameObject = gameObject;
        _sceneRegistry = sceneRegistry;
        _sceneSerializer = sceneSerializer;
    }

    public void Bind(Script script)
    {
        script.Globals["move"] = (Action<double, double, double>)Move;
        script.Globals["setPosition"] = (Action<double, double, double>)SetPosition;
        script.Globals["rotate"] = (Action<double, double, double>)Rotate;
        script.Globals["log"] = (Action<string>)Log;

        script.Globals["spawnCube"] = (Func<string, double, double, double, string>)SpawnCube;
        script.Globals["exists"] = (Func<string, bool>)Exists;
        script.Globals["destroy"] = (Action<string>)Destroy;

        script.Globals["setObjectPosition"] = (Action<string, double, double, double>)SetObjectPosition;
        script.Globals["setObjectRotation"] = (Action<string, double, double, double>)SetObjectRotation;
        script.Globals["setObjectScale"] = (Action<string, double, double, double>)SetObjectScale;
        script.Globals["renameObject"] = (Action<string, string>)RenameObject;

        script.Globals["attachScript"] = (Action<string, string>)AttachScript;

        script.Globals["saveScene"] = (Action<string>)SaveScene;
        script.Globals["loadScene"] = (Action<string>)LoadScene;
    }

    private void Move(double x, double y, double z)
    {
        _gameObject.transform.position += ToVector3(x, y, z);
    }

    private void SetPosition(double x, double y, double z)
    {
        _gameObject.transform.position = ToVector3(x, y, z);
    }

    private void Rotate(double x, double y, double z)
    {
        _gameObject.transform.Rotate(ToVector3(x, y, z));
    }

    private void Log(string message)
    {
        Debug.Log($"[Lua:{_gameObject.name}] {message}");
    }

    private string SpawnCube(string name, double x, double y, double z)
    {
        return _sceneRegistry.SpawnCube(name, ToVector3(x, y, z));
    }

    private bool Exists(string id)
    {
        return _sceneRegistry.Exists(id);
    }

    private void Destroy(string id)
    {
        _sceneRegistry.DestroyObject(id);
    }

    private void SetObjectPosition(string id, double x, double y, double z)
    {
        _sceneRegistry.SetPosition(id, ToVector3(x, y, z));
    }

    private void SetObjectRotation(string id, double x, double y, double z)
    {
        _sceneRegistry.SetRotation(id, ToVector3(x, y, z));
    }

    private void SetObjectScale(string id, double x, double y, double z)
    {
        _sceneRegistry.SetScale(id, ToVector3(x, y, z));
    }

    private void RenameObject(string id, string name)
    {
        _sceneRegistry.Rename(id, name);
    }

    private void AttachScript(string id, string scriptName)
    {
        RuntimeSceneObject runtimeObject = _sceneRegistry.Get(id);
        if (runtimeObject == null) return;

        foreach (LuaBehaviour existing in runtimeObject.GetComponents<LuaBehaviour>())
        {
            if (existing.ScriptName == scriptName) return;
        }

        LuaBehaviour behaviour = runtimeObject.gameObject.AddComponent<LuaBehaviour>();
        behaviour.Configure(scriptName, _sceneRegistry, _sceneSerializer);
    }

    private void SaveScene(string fileName)
    {
        _sceneSerializer.Save(fileName);
    }

    private async void LoadScene(string fileName)
    {
        try
        {
            await _sceneSerializer.LoadAsync(fileName);
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    private static Vector3 ToVector3(double x, double y, double z)
    {
        return new Vector3((float)x, (float)y, (float)z);
    }
}