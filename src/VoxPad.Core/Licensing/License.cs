using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoxPad.Core.Storage;

namespace VoxPad.Core.Licensing;

/// <summary>
/// Trial + serial licensing. A serial is an ECDSA P-256 signature (64 bytes) over "VoxPad|{name}|{edition}",
/// encoded as groups of Crockford base-32. The vendor keeps the private key (tools/KeyGen); the app only embeds the public key.
/// </summary>
public static class LicenseManager
{
    public const int TrialMaxSounds = 10;
    public const int TrialMaxPlaysPerLaunch = 10;

    /// <summary>Public key (SubjectPublicKeyInfo, base64). Replace with your own when you run tools/KeyGen.</summary>
    public static string PublicKeyBase64 { get; set; } =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEFawBttoCL/6+1wNz4oAD6P8UW8Y7YIUQOctOuQ/MtE3EBGmxNHi3WmQXw1qyQ5Hcg1FnNbmZN3bODzwhEdMOOQ==";

    public sealed record License(string Name, string Serial, string Edition, DateTime Issued);

    private static License? _current;
    private static int _playsThisLaunch;

    public static License? Current => _current;
    public static bool IsRegistered => _current != null;

    /// <summary>
    /// VoxPad is free: the trial limits are off. The serial/registration code is kept (and tested) for anyone who
    /// builds a paid edition; set this to true to turn the 10-sound / 10-mic-play limits back on.
    /// </summary>
    public static bool TrialLimitsEnabled { get; set; }

    /// <summary>True when the trial limits apply to this session.</summary>
    public static bool IsLimited => TrialLimitsEnabled && !IsRegistered;
    public static int PlaysThisLaunch => _playsThisLaunch;
    public static int PlaysLeftThisLaunch => !IsLimited ? int.MaxValue : Math.Max(0, TrialMaxPlaysPerLaunch - _playsThisLaunch);

    public static void LoadFromDisk(string? path = null)
    {
        path ??= AppPaths.LicenseFile;
        _current = null;
        try
        {
            if (!File.Exists(path)) return;
            var doc = JsonSerializer.Deserialize<LicenseFileDto>(File.ReadAllText(path));
            if (doc == null) return;
            if (Verify(doc.Name, doc.Serial, out var edition))
                _current = new License(doc.Name, doc.Serial, edition, doc.Issued);
            else Logging.Log.Warn("license.dat present but the serial does not verify");
        }
        catch (Exception ex) { Logging.Log.Error("license.dat unreadable", ex); }
    }

    /// <summary>Register: verifies the serial for the name and writes license.dat.</summary>
    public static bool Register(string name, string serial, string? path = null)
    {
        if (!Verify(name, serial, out var edition)) return false;
        path ??= AppPaths.LicenseFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lic = new LicenseFileDto { Name = name.Trim(), Serial = Clean(serial), Issued = DateTime.UtcNow };
        File.WriteAllText(path, JsonSerializer.Serialize(lic, new JsonSerializerOptions { WriteIndented = true }));
        _current = new License(lic.Name, lic.Serial, edition, lic.Issued);
        return true;
    }

    public static void Unregister(string? path = null)
    {
        path ??= AppPaths.LicenseFile;
        try { File.Delete(path); } catch { }
        _current = null;
    }

    /// <summary>Call before every microphone play. Returns false (and does not count) when the trial limit is hit.</summary>
    public static bool TryConsumePlay()
    {
        if (!IsLimited) return true;
        if (_playsThisLaunch >= TrialMaxPlaysPerLaunch) return false;
        _playsThisLaunch++;
        return true;
    }

    public static void ResetLaunchCounter() => _playsThisLaunch = 0;

    public static bool CanAddSounds(int currentCount, int adding) => !IsLimited || currentCount + adding <= TrialMaxSounds;

    public static bool Verify(string name, string serial, out string edition)
    {
        edition = "";
        try
        {
            var sig = Base32.Decode(Clean(serial));
            if (sig.Length != 65) return false; // 1 byte edition + 64 byte signature
            edition = sig[0] switch { 1 => "Standard", 2 => "Streamer", _ => "Standard" };
            var message = Encoding.UTF8.GetBytes($"VoxPad|{name.Trim()}|{sig[0]}");
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);
            return ecdsa.VerifyData(message, sig.AsSpan(1).ToArray(), HashAlgorithmName.SHA256);
        }
        catch { return false; }
    }

    /// <summary>Vendor side (tools/KeyGen): produce a serial for a licensee.</summary>
    public static string Issue(string name, byte edition, string privateKeyPkcs8Base64)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyPkcs8Base64), out _);
        var message = Encoding.UTF8.GetBytes($"VoxPad|{name.Trim()}|{edition}");
        var sig = ecdsa.SignData(message, HashAlgorithmName.SHA256);
        var payload = new byte[65];
        payload[0] = edition;
        sig.CopyTo(payload, 1);
        return Base32.Encode(payload, groupSize: 6);
    }

    private static string Clean(string serial) => new(serial.Where(c => char.IsLetterOrDigit(c)).Select(char.ToUpperInvariant).ToArray());

    private sealed class LicenseFileDto
    {
        public string Name { get; set; } = "";
        public string Serial { get; set; } = "";
        public DateTime Issued { get; set; }
    }
}

/// <summary>Crockford base-32 (no I, L, O, U) for human-typeable serials.</summary>
public static class Base32
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(byte[] data, int groupSize = 0)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; sb.Append(Alphabet[(buffer >> bits) & 31]); }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        if (groupSize <= 0) return sb.ToString();
        var s = sb.ToString();
        return string.Join("-", Enumerable.Range(0, (s.Length + groupSize - 1) / groupSize).Select(i => s.Substring(i * groupSize, Math.Min(groupSize, s.Length - i * groupSize))));
    }

    public static byte[] Decode(string text)
    {
        var result = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var raw in text)
        {
            char c = char.ToUpperInvariant(raw);
            if (c == '-' || c == ' ') continue;
            c = c switch { 'I' => '1', 'L' => '1', 'O' => '0', _ => c };
            int v = Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException("Invalid serial character");
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bits -= 8; result.Add((byte)((buffer >> bits) & 255)); }
        }
        return result.ToArray();
    }
}
