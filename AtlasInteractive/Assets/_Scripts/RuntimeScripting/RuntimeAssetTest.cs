using UnityEngine;

public sealed class RuntimeAssetTest : MonoBehaviour
{
    [SerializeField] private RuntimeAssetService _assetService;

    private async void Start()
    {
        string id = await _assetService.LoadModel("Models/Test.glb", "RuntimeModel", new Vector3(0, 0, 3));
        Debug.Log($"Loaded model: {id}");
    }
}