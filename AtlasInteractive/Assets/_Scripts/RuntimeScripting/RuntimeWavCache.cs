using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class RuntimeWavCache
{
    private const int Version = 1;

    public static AudioClip ImportSource(string sourcePath)
    {
        RuntimeAudioData data = ReadWav(sourcePath);

        string clipName = Path.GetFileNameWithoutExtension(sourcePath);

        AudioClip clip = AudioClip.Create(
            clipName,
            data.Samples.Length / data.Channels,
            data.Channels,
            data.SampleRate,
            false
        );

        if (!clip.SetData(data.Samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException($"Failed to set WAV data: {sourcePath}");
        }

        return clip;
    }

    public static void Save(AudioClip clip, string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        float[] samples = new float[clip.samples * clip.channels];

        if (!clip.GetData(samples, 0))
        {
            throw new InvalidOperationException($"Failed to read AudioClip '{clip.name}'.");
        }

        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(Version);
        writer.Write(clip.channels);
        writer.Write(clip.frequency);
        writer.Write(samples.Length);

        foreach (float sample in samples)
        {
            writer.Write(sample);
        }
    }

    public static AudioClip Load(string path, string clipName)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new BinaryReader(stream);

        int version = reader.ReadInt32();

        if (version != Version)
        {
            throw new InvalidDataException($"Unsupported audio cache version: {version}");
        }

        int channels = reader.ReadInt32();
        int sampleRate = reader.ReadInt32();
        int sampleCount = reader.ReadInt32();

        float[] samples = new float[sampleCount];

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = reader.ReadSingle();
        }

        AudioClip clip = AudioClip.Create(
            clipName,
            sampleCount / channels,
            channels,
            sampleRate,
            false
        );

        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException($"Failed to restore cached audio '{clipName}'.");
        }

        return clip;
    }

    private static RuntimeAudioData ReadWav(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new BinaryReader(stream);

        if (ReadFourCC(reader) != "RIFF") throw new InvalidDataException("Invalid WAV: missing RIFF.");
        reader.ReadUInt32();

        if (ReadFourCC(reader) != "WAVE") throw new InvalidDataException("Invalid WAV: missing WAVE.");

        ushort audioFormat = 0;
        ushort channels = 0;
        uint sampleRate = 0;
        ushort bitsPerSample = 0;
        byte[] audioBytes = null;

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = ReadFourCC(reader);
            uint chunkSize = reader.ReadUInt32();

            long chunkStart = stream.Position;

            if (chunkId == "fmt ")
            {
                audioFormat = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();

                reader.ReadUInt32();
                reader.ReadUInt16();

                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                audioBytes = reader.ReadBytes((int)chunkSize);
            }

            stream.Position = chunkStart + chunkSize;

            if ((chunkSize & 1) != 0 && stream.Position < stream.Length)
            {
                stream.Position++;
            }
        }

        if (audioFormat != 1)
        {
            throw new NotSupportedException($"Only PCM WAV is supported. WAV format was {audioFormat}.");
        }

        if (bitsPerSample != 16)
        {
            throw new NotSupportedException($"Only 16-bit WAV is supported. WAV was {bitsPerSample}-bit.");
        }

        if (channels == 0 || sampleRate == 0 || audioBytes == null)
        {
            throw new InvalidDataException("WAV is missing required audio data.");
        }

        int sampleCount = audioBytes.Length / 2;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(audioBytes, i * 2);
            samples[i] = sample / 32768f;
        }

        return new RuntimeAudioData
        {
            Channels = channels,
            SampleRate = (int)sampleRate,
            Samples = samples
        };
    }

    private static string ReadFourCC(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }

    private sealed class RuntimeAudioData
    {
        public int Channels;
        public int SampleRate;
        public float[] Samples;
    }
}