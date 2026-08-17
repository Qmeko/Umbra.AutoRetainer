using System.Text.Json;

namespace Umbra.Plugin.AutoRetainer.Services;

public sealed class ArRoster
{
    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XIVLauncher",
        "pluginConfigs",
        "AutoRetainer",
        "DefaultConfig.json");

    public List<ArCharacter> Characters { get; } = [];

    public static ArRoster Load()
    {
        var roster = new ArRoster();
        if (!File.Exists(ConfigPath))
            return roster;

        using var stream = File.Open(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (!doc.RootElement.TryGetProperty("OfflineData", out var offlineData)
            || offlineData.ValueKind != JsonValueKind.Array)
        {
            return roster;
        }

        foreach (var character in offlineData.EnumerateArray())
        {
            var cid = ReadUInt64(character, "CID");
            var name = ReadString(character, "Name");
            var world = ReadString(character, "World");
            if (cid == 0 || string.IsNullOrWhiteSpace(name))
                continue;

            var entry = new ArCharacter
            {
                Cid = cid,
                Name = name,
                World = world,
                ExcludeRetainer = ReadBool(character, "ExcludeRetainer"),
                ExcludeWorkshop = ReadBool(character, "ExcludeWorkshop")
            };

            if (character.TryGetProperty("RetainerData", out var retainers)
                && retainers.ValueKind == JsonValueKind.Array)
            {
                foreach (var retainer in retainers.EnumerateArray())
                {
                    var retainerName = ReadString(retainer, "Name");
                    if (string.IsNullOrWhiteSpace(retainerName))
                        continue;

                    entry.Retainers.Add(new ArRetainer
                    {
                        Name = retainerName,
                        HasVenture = ReadBool(retainer, "HasVenture"),
                        VentureEndsAt = ReadInt64(retainer, "VentureEndsAt")
                    });
                }
            }

            if (character.TryGetProperty("OfflineSubmarineData", out var subs)
                && subs.ValueKind == JsonValueKind.Array)
            {
                foreach (var sub in subs.EnumerateArray())
                {
                    var subName = ReadString(sub, "Name");
                    if (string.IsNullOrWhiteSpace(subName))
                        continue;

                    entry.Submarines.Add(new ArVessel
                    {
                        Name = subName,
                        ReturnTime = ReadUInt64(sub, "ReturnTime")
                    });
                }
            }

            roster.Characters.Add(entry);
        }

        roster.Characters.Sort((a, b) =>
        {
            var world = string.Compare(a.World, b.World, StringComparison.Ordinal);
            return world != 0 ? world : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return roster;
    }

    public static string CharacterKey(ulong cid) => $"CharEnabled_{cid:X16}";

    public static string RetainerKey(ulong cid, string name) => $"RetEnabled_{cid:X16}_{name}";

    private static bool ReadBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
               && value.ValueKind is JsonValueKind.True;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static long ReadInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0
        };
    }

    private static ulong ReadUInt64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetUInt64(out var number) => number,
            JsonValueKind.String when ulong.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0
        };
    }
}

public sealed class ArCharacter
{
    public ulong Cid { get; init; }
    public string Name { get; init; } = "";
    public string World { get; init; } = "";
    public bool ExcludeRetainer { get; init; }
    public bool ExcludeWorkshop { get; init; }
    public List<ArRetainer> Retainers { get; } = [];
    public List<ArVessel> Submarines { get; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(World) ? Name : $"{Name}@{World}";
}

public sealed class ArRetainer
{
    public string Name { get; init; } = "";
    public bool HasVenture { get; init; }
    public long VentureEndsAt { get; init; }
}

public sealed class ArVessel
{
    public string Name { get; init; } = "";
    public ulong ReturnTime { get; init; }
}
