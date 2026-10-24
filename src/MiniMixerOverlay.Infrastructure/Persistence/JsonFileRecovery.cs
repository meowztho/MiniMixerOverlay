namespace MiniMixerOverlay.Infrastructure.Persistence;

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

/// <summary>Preserves exact source bytes before repairing locally stored JSON.</summary>
internal static class JsonFileRecovery
{
    public static bool TryPreserve(string path, string reason)
    {
        try
        {
            if (!File.Exists(path)) return true;
            var original = File.ReadAllBytes(path);
            var hash = Convert.ToHexString(SHA256.HashData(original))[..16];
            var backup = path + "." + reason + "." + hash + ".bak";
            if (File.Exists(backup))
            {
                return File.ReadAllBytes(backup).SequenceEqual(original);
            }

            File.WriteAllBytes(backup, original);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"JSON source backup failed for {path}: {ex.Message}");
            return false;
        }
    }
}
