using System.Security.Cryptography;
using System.Text.Json;
using VoxPad.Core.Licensing;

namespace VoxPad.Core.Updates;

/// <summary>
/// Update manifest fetched from <see cref="Settings.Preferences.UpdateManifestUrl"/>:
/// { "version": "1.2.0", "url": "https://.../VoxPad-1.2.0.msi", "sha256": "hex", "signature": "base64 ECDSA over the sha256 bytes", "notes": "..." }.
/// Packages are only offered for install when the signature verifies against the embedded public key.
/// </summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Signature { get; set; } = "";
    public string? Notes { get; set; }

    public static UpdateManifest? Parse(string json) => JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    public bool IsNewerThan(string currentVersion)
        => System.Version.TryParse(Version, out var v) && System.Version.TryParse(currentVersion, out var c) && v > c;

    /// <summary>Verifies that the manifest's hash was signed by the vendor key.</summary>
    public bool VerifySignature()
    {
        try
        {
            var hash = Convert.FromHexString(Sha256);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(LicenseManager.PublicKeyBase64), out _);
            return ecdsa.VerifyHash(hash, Convert.FromBase64String(Signature));
        }
        catch { return false; }
    }

    /// <summary>Verifies the downloaded package against the manifest hash.</summary>
    public bool VerifyPackage(string path)
    {
        using var fs = File.OpenRead(path);
        var hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash).Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
