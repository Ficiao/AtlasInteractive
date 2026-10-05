using System;
using System.IO;
using MoonSharp.Interpreter;
using UnityEngine;

public sealed class LuaBehaviour : MonoBehaviour
{
    [SerializeField] private string _scriptName = "Test.lua";
    [SerializeField] private float _reloadCheckInterval = 0.25f;
    [SerializeField] private RuntimeSceneRegistry _sceneRegistry;
    [SerializeField] private RuntimeSceneSerializer _sceneSerializer;

    private Script _script;
    private DynValue _update;
    private RuntimeScriptAPI _api;

    private string _scriptPath;
    private DateTime _lastObservedWriteTime;
    private float _nextReloadCheck;
    private bool _initialized;

    public string ScriptName => _scriptName;

    public void Configure(string scriptName, RuntimeSceneRegistry sceneRegistry, RuntimeSceneSerializer sceneSerializer)
    {
        _scriptName = scriptName;
        _sceneRegistry = sceneRegistry;
        _sceneSerializer = sceneSerializer;

        Initialize();
    }

    public void ReloadNow()
    {
        if (!_initialized)
        {
            Initialize();
            return;
        }

        LoadScriptOrThrow();
    }

    private void Start()
    {
        if (!_initialized) Initialize();
    }

    private void Update()
    {
        if (!_initialized) return;

        if (_update != null)
        {
            try
            {
                _script.Call(_update, Time.deltaTime);
            }
            catch (Exception e)
            {
                Debug.LogError($"Lua update failed [{_scriptName}]:\n{FormatLuaException(e)}");
                _update = null;
            }
        }

        if (Time.unscaledTime < _nextReloadCheck) return;

        _nextReloadCheck = Time.unscaledTime + _reloadCheckInterval;

        DateTime writeTime = File.GetLastWriteTimeUtc(_scriptPath);

        if (writeTime == _lastObservedWriteTime) return;

        _lastObservedWriteTime = writeTime;

        try
        {
            LoadScriptOrThrow();
        }
        catch (Exception e)
        {
            Debug.LogError($"Lua reload failed [{_scriptName}]:\n{FormatLuaException(e)}");
        }
    }

    private void Initialize()
    {
        if (_initialized) return;

        if (_sceneRegistry == null) throw new InvalidOperationException("RuntimeSceneRegistry is required.");
        if (_sceneSerializer == null) throw new InvalidOperationException("RuntimeSceneSerializer is required.");
        if (string.IsNullOrWhiteSpace(_scriptName)) throw new InvalidOperationException("Lua script name is required.");

        string scriptsDirectory = Path.Combine(Application.persistentDataPath, "Scripts");
        Directory.CreateDirectory(scriptsDirectory);

        _scriptPath = Path.Combine(scriptsDirectory, _scriptName);

        if (!File.Exists(_scriptPath))
        {
            throw new FileNotFoundException($"Lua script does not exist: {_scriptPath}");
        }

        _api = new RuntimeScriptAPI(gameObject, _sceneRegistry, _sceneSerializer);

        LoadScriptOrThrow();

        _initialized = true;

        Debug.Log($"Lua script initialized: {_scriptPath}");
    }

    private void LoadScriptOrThrow()
    {
        string source = File.ReadAllText(_scriptPath);

        Script candidate = new Script();

        _api.Bind(candidate);

        candidate.DoString(source);

        DynValue start = candidate.Globals.Get("start");
        DynValue update = candidate.Globals.Get("update");

        if (start.Type != DataType.Nil && start.Type != DataType.Function)
        {
            throw new InvalidOperationException("Lua global 'start' must be a function.");
        }

        if (update.Type != DataType.Nil && update.Type != DataType.Function)
        {
            throw new InvalidOperationException("Lua global 'update' must be a function.");
        }

        if (start.Type == DataType.Function)
        {
            candidate.Call(start);
        }

        _script = candidate;
        _update = update.Type == DataType.Function ? update : null;
        _lastObservedWriteTime = File.GetLastWriteTimeUtc(_scriptPath);

        Debug.Log($"Loaded Lua: {_scriptPath}");
    }

    private static string FormatLuaException(Exception exception)
    {
        if (exception is ScriptRuntimeException runtimeException && !string.IsNullOrWhiteSpace(runtimeException.DecoratedMessage))
        {
            return runtimeException.DecoratedMessage;
        }

        return exception.ToString();
    }
}