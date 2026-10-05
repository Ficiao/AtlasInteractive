using UnityEngine;

public sealed class RuntimeToolTest : MonoBehaviour
{
    [SerializeField] private RuntimeToolService _tools;

    private async void Start()
    {
        string cubeId = _tools.CreateCube("ToolCube", -3, 1, 0);

        _tools.SetScale(cubeId, 2, 0.5f, 2);
        _tools.SetRotation(cubeId, 0, 45, 0);

        string modelId = await _tools.LoadModel("Models/Test.glb", "ToolModel", 3, 0, 0);

        _tools.CreateScript("Spin.lua",
@"function start()
    log(""Spin script started"")
end

function update(dt)
    rotate(0, 50 * dt, 0)
end");

        _tools.AttachScript(modelId, "Spin.lua");

        Debug.Log(_tools.GetSceneState());
    }
}