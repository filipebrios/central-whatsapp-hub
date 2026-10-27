using System.Text.Json;

namespace CentralWhatsApp.WebView2;

internal sealed record ModuxAccountInfo(string Id, string Name, string? AvatarData = null, int UnreadCount = 0);

/// <summary>Fresh installs are empty. Existing names, IDs and profile directories stay local.</summary>
internal static class ModuxAccountStorage
{
    internal static IReadOnlyList<ModuxAccountInfo> Load(string root)
    {
        var file = Path.Combine(root, "accounts.json");
        if (File.Exists(file))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<List<ModuxAccountInfo>>(File.ReadAllText(file));
                if (saved is null || saved.Any(a => a is null || string.IsNullOrWhiteSpace(a.Id) ||
                    a.Id != Path.GetFileName(a.Id) || a.Id.IndexOfAny(['/', '\\']) >= 0 || a.Id is "." or ".." ||
                    string.IsNullOrWhiteSpace(a.Name)) || saved.Select(a => a.Id).Distinct().Count() != saved.Count)
                    throw new InvalidOperationException("Cadastro local de contas inválido.");
                return saved;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            {
                // Never overwrite an unreadable account list without preserving the exact original.
                var recovery = Path.Combine(root, "accounts-recovery-" + Guid.NewGuid().ToString("N") + ".json");
                File.Copy(file, recovery);
                throw new InvalidOperationException("Não foi possível ler as contas. Uma cópia local foi preservada para recuperação.", error);
            }
        }
        // Compatibility with the earliest version, only if its real profile is present.
        var legacy = Path.Combine(root, "waseller-account");
        return Directory.Exists(legacy) && Directory.EnumerateFileSystemEntries(legacy).Any()
            ? [new ModuxAccountInfo("waseller-account", "Conta recuperada")]
            : [];
    }

    internal static void Save(string root, IReadOnlyList<ModuxAccountInfo> accounts)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "accounts.json");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
