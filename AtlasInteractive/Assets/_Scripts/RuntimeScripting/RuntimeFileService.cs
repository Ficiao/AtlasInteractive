using System;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class RuntimeFileService : MonoBehaviour
{
    [SerializeField] private string _rootDirectoryOverride;

    public string RootPath { get; private set; }

    private void Awake()
    {
        RootPath = string.IsNullOrWhiteSpace(_rootDirectoryOverride)
            ? Path.GetFullPath(Application.persistentDataPath)
            : Path.GetFullPath(_rootDirectoryOverride);

        Directory.CreateDirectory(RootPath);

        Debug.Log($"Runtime file root: {RootPath}");
    }

    public string ReadText(string relativePath)
    {
        return File.ReadAllText(Resolve(relativePath));
    }

    public void WriteText(string relativePath, string content)
    {
        string path = Resolve(relativePath);
        string directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(path, content);
    }

    public string[] Find(string searchPattern = "*")
    {
        return Directory.GetFiles(RootPath, searchPattern, SearchOption.AllDirectories)
            .Where(path => !IsPrivatePath(path))
            .Select(path => Path.GetRelativePath(RootPath, path))
            .ToArray();
    }

    public bool Exists(string relativePath)
    {
        return File.Exists(Resolve(relativePath));
    }

    private string Resolve(string relativePath)
    {
        string root = Path.GetFullPath(RootPath);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && path != root)
        {
            throw new UnauthorizedAccessException($"Path escapes runtime root: {relativePath}");
        }

        if (IsPrivatePath(path))
        {
            throw new UnauthorizedAccessException("Access to the Private directory is not allowed.");
        }

        return path;
    }

    private bool IsPrivatePath(string path)
    {
        string privateRoot = Path.GetFullPath(Path.Combine(RootPath, "Private"));

        return path.Equals(privateRoot, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(privateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}