using System.IO;
using UnityEngine;

public static class RuntimeTextureCache
{
    private const int Version = 1;

    public static void Save(Texture2D texture, string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(Version);
        writer.Write(texture.width);
        writer.Write(texture.height);

        Color32[] pixels = texture.GetPixels32();

        writer.Write(pixels.Length);

        foreach (Color32 pixel in pixels)
        {
            writer.Write(pixel.r);
            writer.Write(pixel.g);
            writer.Write(pixel.b);
            writer.Write(pixel.a);
        }
    }

    public static Texture2D Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new BinaryReader(stream);

        int version = reader.ReadInt32();

        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported texture cache version: {version}");
        }

        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int count = reader.ReadInt32();

        Color32[] pixels = new Color32[count];

        for (int i = 0; i < count; i++)
        {
            pixels[i] = new Color32(
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte()
            );
        }

        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
        texture.SetPixels32(pixels);
        texture.Apply(true, false);

        return texture;
    }
}