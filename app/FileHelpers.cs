using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlexCrome.Acr.CredentialHelper;

internal static class FileHelpers
{
    internal static async Task AtomicallyWriteJson(
        string path,
        JsonNode json,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        // Same directory keeps the move a rename; a cross-filesystem move degrades to copy-into-destination,
        // which concurrent writers could interleave.
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var formatted = json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(temporaryPath,
                formatted,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
