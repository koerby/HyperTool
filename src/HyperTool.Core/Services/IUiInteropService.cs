namespace HyperTool.Services;

public enum UnsavedConfigPromptResult
{
    Yes,
    No,
    Cancel
}

public interface IUiInteropService
{
    UnsavedConfigPromptResult ShowUnsavedConfigPrompt();

    void SetClipboardText(string text);

    string? PickFolderPath(string description);

    void ShutdownApplication();
}
