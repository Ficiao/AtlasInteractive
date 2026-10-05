using System;
using System.Globalization;
using UnityEngine;

public static class RuntimeComponentUtility
{
    public static Component Add(GameObject gameObject, string componentType)
    {
        switch (Normalize(componentType))
        {
            case "rigidbody":
                return gameObject.GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();

            case "light":
                return gameObject.GetComponent<Light>() ?? gameObject.AddComponent<Light>();

            case "audiosource":
                return gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

            default:
                throw new NotSupportedException(
                    $"Unsupported component '{componentType}'. Supported: Rigidbody, Light, AudioSource."
                );
        }
    }

    public static void SetProperty(GameObject gameObject, string componentType, string property, string value)
    {
        switch (Normalize(componentType))
        {
            case "rigidbody":
                SetRigidbodyProperty((Rigidbody)Add(gameObject, componentType), property, value);
                return;

            case "light":
                SetLightProperty((Light)Add(gameObject, componentType), property, value);
                return;

            case "audiosource":
                SetAudioSourceProperty((AudioSource)Add(gameObject, componentType), property, value);
                return;

            default:
                throw new NotSupportedException(
                    $"Unsupported component '{componentType}'. Supported: Rigidbody, Light, AudioSource."
                );
        }
    }

    private static void SetRigidbodyProperty(Rigidbody body, string property, string value)
    {
        switch (property.ToLowerInvariant())
        {
            case "mass":
                body.mass = ParseFloat(value);
                return;

            case "usegravity":
                body.useGravity = ParseBool(value);
                return;

            case "iskinematic":
                body.isKinematic = ParseBool(value);
                return;

            default:
                throw new NotSupportedException(
                    $"Unsupported Rigidbody property '{property}'. Supported: mass, useGravity, isKinematic."
                );
        }
    }

    private static void SetLightProperty(Light light, string property, string value)
    {
        switch (property.ToLowerInvariant())
        {
            case "intensity":
                light.intensity = ParseFloat(value);
                return;

            case "range":
                light.range = ParseFloat(value);
                return;

            case "color":
                if (!ColorUtility.TryParseHtmlString(value, out Color color))
                {
                    throw new ArgumentException($"Invalid HTML color '{value}'. Example: #FF0000");
                }

                light.color = color;
                return;

            default:
                throw new NotSupportedException(
                    $"Unsupported Light property '{property}'. Supported: intensity, range, color."
                );
        }
    }

    private static void SetAudioSourceProperty(AudioSource source, string property, string value)
    {
        switch (property.ToLowerInvariant())
        {
            case "volume":
                source.volume = Mathf.Clamp01(ParseFloat(value));
                return;

            case "loop":
                source.loop = ParseBool(value);
                return;

            case "spatialblend":
                source.spatialBlend = Mathf.Clamp01(ParseFloat(value));
                return;

            default:
                throw new NotSupportedException(
                    $"Unsupported AudioSource property '{property}'. Supported: volume, loop, spatialBlend."
                );
        }
    }

    private static string Normalize(string value)
    {
        return value.Replace(" ", "").ToLowerInvariant();
    }

    private static float ParseFloat(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
        {
            throw new ArgumentException($"Invalid number '{value}'.");
        }

        return result;
    }

    private static bool ParseBool(string value)
    {
        if (!bool.TryParse(value, out bool result))
        {
            throw new ArgumentException($"Invalid boolean '{value}'. Use true or false.");
        }

        return result;
    }
}