using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RuntimeChatController : MonoBehaviour
{
    [SerializeField] private RuntimeLlmClient _llmClient;
    [SerializeField] private TMP_InputField _input;
    [SerializeField] private TMP_Text _output;
    [SerializeField] private Button _sendButton;
    [SerializeField] private ScrollRect _scrollRect;

    private bool _busy;

    private void Awake()
    {
        if (_llmClient == null) throw new InvalidOperationException("RuntimeLlmClient is required.");
        if (_input == null) throw new InvalidOperationException("Input is required.");
        if (_output == null) throw new InvalidOperationException("Output is required.");
        if (_sendButton == null) throw new InvalidOperationException("Send button is required.");

        _sendButton.onClick.AddListener(Send);

        _output.enableWordWrapping = true;
        _output.overflowMode = TextOverflowModes.Overflow;
        _output.text = "ATLAS Runtime Assistant\n";

        RefreshOutputLayout();
    }

    private async void Send()
    {
        if (_busy) return;

        string message = _input.text.Trim();

        if (string.IsNullOrWhiteSpace(message)) return;

        _busy = true;
        _sendButton.interactable = false;
        _input.interactable = false;

        _input.text = "";

        Append($"\nYOU:\n{message}\n");
        Append("\nASSISTANT:\n");

        try
        {
            await _llmClient.SendAsync(message, Append);
            Append("\n");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Append($"\nERROR:\n{e.Message}\n");
        }
        finally
        {
            _busy = false;
            _sendButton.interactable = true;
            _input.interactable = true;

            _input.ActivateInputField();
            RefreshOutputLayout();
        }
    }

    private void Append(string text)
    {
        _output.text += text;
        RefreshOutputLayout();
    }

    private void RefreshOutputLayout()
    {
        Canvas.ForceUpdateCanvases();

        float minimumHeight = 100f;

        if (_scrollRect != null && _scrollRect.viewport != null)
        {
            minimumHeight = _scrollRect.viewport.rect.height;
        }

        float requiredHeight = Mathf.Max(minimumHeight, _output.preferredHeight + 20f);

        _output.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requiredHeight);

        if (_scrollRect != null && _scrollRect.content != null && _scrollRect.content != _output.rectTransform)
        {
            _scrollRect.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, requiredHeight);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollRect.content);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_output.rectTransform);

        Canvas.ForceUpdateCanvases();

        if (_scrollRect != null)
        {
            _scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}