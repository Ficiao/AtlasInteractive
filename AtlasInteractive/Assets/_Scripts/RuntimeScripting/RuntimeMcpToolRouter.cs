using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class RuntimeMcpToolRouter : MonoBehaviour
{
    [SerializeField] private RuntimeToolService _tools;

    private void Awake()
    {
        if (_tools == null) throw new InvalidOperationException("RuntimeToolService is required.");
    }

    public JArray GetToolDefinitions()
    {
        return new JArray
        {
            Tool(
                "scene.get_state",
                "Returns runtime objects, IDs, names, transforms, parents, scripts, components and Rigidbody state. Takes no arguments.",
                new JObject()
            ),

            Tool(
                "scene.create_primitive",
                "Creates a Unity primitive and returns its objectId and name.",
                new JObject
                {
                    ["primitiveType"] = StringProperty("Cube, Sphere, Capsule, Cylinder, Plane or Quad."),
                    ["name"] = StringProperty("Exact unique object name."),
                    ["x"] = NumberProperty("World X."),
                    ["y"] = NumberProperty("World Y."),
                    ["z"] = NumberProperty("World Z.")
                },
                "primitiveType", "name", "x", "y", "z"
            ),

            Tool(
                "scene.set_transform",
                "Changes position, rotation and/or scale. id may be objectId or exact unique object name.",
                new JObject
                {
                    ["id"] = StringProperty("Object ID or exact unique name."),
                    ["position"] = Vector3Property("World position."),
                    ["rotation"] = Vector3Property("Euler rotation."),
                    ["scale"] = Vector3Property("Local scale.")
                },
                "id"
            ),

            Tool(
                "scene.set_parent",
                "Parents a runtime object under another runtime object.",
                new JObject
                {
                    ["childId"] = StringProperty("Child ID or exact unique name."),
                    ["parentId"] = StringProperty("Parent ID or exact unique name.")
                },
                "childId", "parentId"
            ),

            Tool(
                "scene.delete_object",
                "Deletes a runtime object.",
                new JObject
                {
                    ["id"] = StringProperty("Object ID or exact unique name.")
                },
                "id"
            ),

            Tool(
                "scene.save",
                "Saves runtime objects, hierarchy, supported components, assets and Lua scripts to JSON.",
                new JObject
                {
                    ["fileName"] = StringProperty("Scene filename.")
                },
                "fileName"
            ),

            Tool(
                "scene.load",
                "Loads a saved runtime scene including hierarchy, components, assets and scripts.",
                new JObject
                {
                    ["fileName"] = StringProperty("Scene filename.")
                },
                "fileName"
            ),

            Tool("asset.list_models", "Lists exact GLB paths. Takes no arguments.", new JObject()),
            Tool("asset.list_textures", "Lists exact PNG paths. Takes no arguments.", new JObject()),
            Tool("asset.list_audio", "Lists exact WAV paths. Takes no arguments.", new JObject()),

            Tool(
                "asset.load_model",
                "Loads a GLB model and returns objectId.",
                new JObject
                {
                    ["path"] = StringProperty("Exact path returned by asset.list_models."),
                    ["name"] = StringProperty("Unique object name."),
                    ["x"] = NumberProperty("World X."),
                    ["y"] = NumberProperty("World Y."),
                    ["z"] = NumberProperty("World Z.")
                },
                "path", "name", "x", "y", "z"
            ),

            Tool(
                "asset.set_texture",
                "Applies a PNG texture to an object.",
                new JObject
                {
                    ["id"] = StringProperty("Object ID or exact unique name."),
                    ["path"] = StringProperty("Exact path returned by asset.list_textures.")
                },
                "id", "path"
            ),

            Tool(
                "asset.play_audio",
                "Loads and plays a PCM 16-bit WAV. AudioSource is created automatically.",
                new JObject
                {
                    ["id"] = StringProperty("Object ID or exact unique name."),
                    ["path"] = StringProperty("Exact path returned by asset.list_audio."),
                    ["loop"] = BoolProperty("Whether audio loops."),
                    ["volume"] = NumberProperty("Volume from 0 to 1.")
                },
                "id", "path", "loop", "volume"
            ),

            Tool(
                "component.configure_rigidbody",
                "Creates a Rigidbody if missing and configures it in one atomic call.",
                new JObject
                {
                    ["id"] = StringProperty("Object ID or exact unique name."),
                    ["mass"] = NumberProperty("Mass."),
                    ["useGravity"] = BoolProperty("Whether gravity is enabled."),
                    ["isKinematic"] = BoolProperty("Whether the Rigidbody is kinematic.")
                },
                "id", "mass", "useGravity", "isKinematic"
            ),

            Tool(
                "script.attach_rotation",
                "Creates and attaches a valid runtime Lua script that continuously rotates the target around its Y axis. ALWAYS use this tool for continuous rotation requests. No Rigidbody is required.",
                new JObject
                {
                    ["id"] = StringProperty("Object ID or exact unique name."),
                    ["degreesPerSecond"] = NumberProperty("Y-axis rotation speed in degrees per second.")
                },
                "id", "degreesPerSecond"
            ),

            Tool(
                "script.create_and_attach",
                "Creates arbitrary Lua source and attaches it in one call. Use for custom behaviours, but DO NOT use this tool for simple continuous Y rotation; use script.attach_rotation instead.",
                new JObject
                {
                    ["objectId"] = StringProperty("Object ID or exact unique name."),
                    ["scriptName"] = StringProperty("Lua filename."),
                    ["source"] = StringProperty("Complete Lua source.")
                },
                "objectId", "scriptName", "source"
            ),

            Tool(
                "file.read",
                "Reads a text file inside the runtime sandbox.",
                new JObject
                {
                    ["path"] = StringProperty("Relative file path.")
                },
                "path"
            ),

            Tool(
                "file.write",
                "Writes a text file inside the runtime sandbox.",
                new JObject
                {
                    ["path"] = StringProperty("Relative file path."),
                    ["content"] = StringProperty("Complete file contents.")
                },
                "path", "content"
            ),

            Tool(
                "file.find",
                "Finds files inside the runtime sandbox.",
                new JObject
                {
                    ["pattern"] = StringProperty("Search pattern.")
                }
            )
        };
    }

    public async Task<JObject> CallAsync(string name, JObject arguments)
    {
        try
        {
            switch (name)
            {
                case "scene.get_state":
                    return Success(_tools.GetSceneState());

                case "scene.create_primitive":
                    {
                        string objectName = RequireString(arguments, "name");

                        string objectId = _tools.CreatePrimitive(
                            RequireString(arguments, "primitiveType"),
                            objectName,
                            RequireFloat(arguments, "x"),
                            RequireFloat(arguments, "y"),
                            RequireFloat(arguments, "z")
                        );

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["objectId"] = objectId,
                            ["name"] = objectName
                        });
                    }

                case "scene.set_transform":
                    {
                        string reference = RequireString(arguments, "id");
                        string objectId = _tools.ResolveObjectId(reference);

                        if (arguments["position"] is JObject position)
                        {
                            _tools.SetPosition(reference, RequireFloat(position, "x"), RequireFloat(position, "y"), RequireFloat(position, "z"));
                        }

                        if (arguments["rotation"] is JObject rotation)
                        {
                            _tools.SetRotation(reference, RequireFloat(rotation, "x"), RequireFloat(rotation, "y"), RequireFloat(rotation, "z"));
                        }

                        if (arguments["scale"] is JObject scale)
                        {
                            _tools.SetScale(reference, RequireFloat(scale, "x"), RequireFloat(scale, "y"), RequireFloat(scale, "z"));
                        }

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["objectId"] = objectId
                        });
                    }

                case "scene.set_parent":
                    {
                        string child = RequireString(arguments, "childId");
                        string parent = RequireString(arguments, "parentId");

                        _tools.SetParent(child, parent);

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["childId"] = _tools.ResolveObjectId(child),
                            ["parentId"] = _tools.ResolveObjectId(parent)
                        });
                    }

                case "scene.delete_object":
                    {
                        string reference = RequireString(arguments, "id");
                        string objectId = _tools.ResolveObjectId(reference);

                        _tools.DestroyObject(reference);

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["objectId"] = objectId,
                            ["deleted"] = true
                        });
                    }

                case "scene.save":
                    {
                        string fileName = RequireString(arguments, "fileName");
                        string path = _tools.SaveScene(fileName);

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["fileName"] = fileName,
                            ["path"] = path
                        });
                    }

                case "scene.load":
                    {
                        string fileName = RequireString(arguments, "fileName");

                        await _tools.LoadScene(fileName);

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["fileName"] = fileName,
                            ["sceneState"] = JToken.Parse(_tools.GetSceneState())
                        });
                    }

                case "asset.list_models":
                    return SuccessJson(new JObject
                    {
                        ["success"] = true,
                        ["models"] = JArray.FromObject(_tools.ListModels())
                    });

                case "asset.list_textures":
                    return SuccessJson(new JObject
                    {
                        ["success"] = true,
                        ["textures"] = JArray.FromObject(_tools.ListTextures())
                    });

                case "asset.list_audio":
                    return SuccessJson(new JObject
                    {
                        ["success"] = true,
                        ["audio"] = JArray.FromObject(_tools.ListAudio())
                    });

                case "asset.load_model":
                    {
                        try
                        {
                            string objectName = RequireString(arguments, "name");

                            string objectId = await _tools.LoadModel(
                                RequireString(arguments, "path"),
                                objectName,
                                RequireFloat(arguments, "x"),
                                RequireFloat(arguments, "y"),
                                RequireFloat(arguments, "z")
                            );

                            return SuccessJson(new JObject
                            {
                                ["success"] = true,
                                ["objectId"] = objectId,
                                ["name"] = objectName
                            });
                        }
                        catch (FileNotFoundException e)
                        {
                            return ErrorJson(new JObject
                            {
                                ["success"] = false,
                                ["error"] = e.Message,
                                ["availableModels"] = JArray.FromObject(_tools.ListModels())
                            });
                        }
                    }

                case "asset.set_texture":
                    {
                        try
                        {
                            string reference = RequireString(arguments, "id");
                            string path = RequireString(arguments, "path");

                            _tools.SetTexture(reference, path);

                            return SuccessJson(new JObject
                            {
                                ["success"] = true,
                                ["objectId"] = _tools.ResolveObjectId(reference),
                                ["texture"] = path
                            });
                        }
                        catch (FileNotFoundException e)
                        {
                            return ErrorJson(new JObject
                            {
                                ["success"] = false,
                                ["error"] = e.Message,
                                ["availableTextures"] = JArray.FromObject(_tools.ListTextures())
                            });
                        }
                    }

                case "asset.play_audio":
                    {
                        try
                        {
                            string reference = RequireString(arguments, "id");
                            string path = RequireString(arguments, "path");

                            _tools.PlayAudio(
                                reference,
                                path,
                                RequireBool(arguments, "loop"),
                                RequireFloat(arguments, "volume")
                            );

                            return SuccessJson(new JObject
                            {
                                ["success"] = true,
                                ["objectId"] = _tools.ResolveObjectId(reference),
                                ["audio"] = path
                            });
                        }
                        catch (FileNotFoundException e)
                        {
                            return ErrorJson(new JObject
                            {
                                ["success"] = false,
                                ["error"] = e.Message,
                                ["availableAudio"] = JArray.FromObject(_tools.ListAudio())
                            });
                        }
                    }

                case "component.configure_rigidbody":
                    {
                        RuntimeRigidbodyState state = _tools.ConfigureRigidbody(
                            RequireString(arguments, "id"),
                            RequireFloat(arguments, "mass"),
                            RequireBool(arguments, "useGravity"),
                            RequireBool(arguments, "isKinematic")
                        );

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["objectId"] = state.objectId,
                            ["objectName"] = state.objectName,
                            ["component"] = "Rigidbody",
                            ["mass"] = state.mass,
                            ["useGravity"] = state.useGravity,
                            ["isKinematic"] = state.isKinematic
                        });
                    }

                case "script.attach_rotation":
                    {
                        RuntimeRotationScriptState state = _tools.AttachRotationScript(
                            RequireString(arguments, "id"),
                            RequireFloat(arguments, "degreesPerSecond")
                        );

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["objectId"] = state.objectId,
                            ["objectName"] = state.objectName,
                            ["scriptName"] = state.scriptName,
                            ["degreesPerSecond"] = state.degreesPerSecond,
                            ["attached"] = true
                        });
                    }

                case "script.create_and_attach":
                    {
                        string reference = RequireString(arguments, "objectId");
                        string scriptName = RequireString(arguments, "scriptName");

                        _tools.CreateAndAttachScript(
                            reference,
                            scriptName,
                            RequireString(arguments, "source")
                        );

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["objectId"] = _tools.ResolveObjectId(reference),
                            ["scriptName"] = NormalizeScriptName(scriptName),
                            ["created"] = true,
                            ["attached"] = true
                        });
                    }

                case "file.read":
                    return Success(_tools.ReadFile(RequireString(arguments, "path")));

                case "file.write":
                    {
                        string path = RequireString(arguments, "path");

                        _tools.WriteFile(
                            path,
                            RequireString(arguments, "content")
                        );

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["path"] = path
                        });
                    }

                case "file.find":
                    {
                        string pattern = arguments.Value<string>("pattern");

                        if (string.IsNullOrWhiteSpace(pattern)) pattern = "*";

                        return SuccessJson(new JObject
                        {
                            ["success"] = true,
                            ["files"] = JArray.FromObject(_tools.FindFiles(pattern))
                        });
                    }

                default:
                    return Error($"Unknown tool: {name}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"MCP tool '{name}' failed:\n{e}");
            return Error(e.Message);
        }
    }

    private static JObject Tool(string name, string description, JObject properties, params string[] required)
    {
        JObject schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JArray(required ?? Array.Empty<string>()),
            ["additionalProperties"] = false
        };

        return new JObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema
        };
    }

    private static JObject StringProperty(string description)
    {
        return new JObject
        {
            ["type"] = "string",
            ["description"] = description
        };
    }

    private static JObject NumberProperty(string description)
    {
        return new JObject
        {
            ["type"] = "number",
            ["description"] = description
        };
    }

    private static JObject BoolProperty(string description)
    {
        return new JObject
        {
            ["type"] = "boolean",
            ["description"] = description
        };
    }

    private static JObject Vector3Property(string description)
    {
        return new JObject
        {
            ["type"] = "object",
            ["description"] = description,
            ["properties"] = new JObject
            {
                ["x"] = NumberProperty("X"),
                ["y"] = NumberProperty("Y"),
                ["z"] = NumberProperty("Z")
            },
            ["required"] = new JArray("x", "y", "z"),
            ["additionalProperties"] = false
        };
    }

    private static string RequireString(JObject arguments, string name)
    {
        JToken token = arguments[name];

        if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
        {
            throw new ArgumentException($"Missing or invalid string argument '{name}'.");
        }

        return token.Value<string>();
    }

    private static float RequireFloat(JObject arguments, string name)
    {
        JToken token = arguments[name];

        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
        {
            throw new ArgumentException($"Missing or invalid number argument '{name}'.");
        }

        return token.Value<float>();
    }

    private static bool RequireBool(JObject arguments, string name)
    {
        JToken token = arguments[name];

        if (token == null || token.Type != JTokenType.Boolean)
        {
            throw new ArgumentException($"Missing or invalid boolean argument '{name}'.");
        }

        return token.Value<bool>();
    }

    private static JObject Success(string text)
    {
        return new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            },
            ["isError"] = false
        };
    }

    private static JObject SuccessJson(JObject value)
    {
        return Success(value.ToString(Formatting.None));
    }

    private static JObject Error(string text)
    {
        return ErrorJson(new JObject
        {
            ["success"] = false,
            ["error"] = text
        });
    }

    private static JObject ErrorJson(JObject value)
    {
        return new JObject
        {
            ["content"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = value.ToString(Formatting.None)
                }
            },
            ["isError"] = true
        };
    }

    private static string NormalizeScriptName(string scriptName)
    {
        return scriptName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? scriptName
            : scriptName + ".lua";
    }
}