using System.Security.Cryptography;
using VoxPad.Core.Licensing;

// Vendor-side tooling (never ship the private key):
//   voxpad-keygen newkey                       → prints a fresh P-256 key pair (PKCS#8 private, SPKI public, base64)
//   voxpad-keygen issue <private> "<name>" [1|2]  → prints a serial for the licensee (edition 1 = Standard, 2 = Streamer)
//   voxpad-keygen sign-update <private> <file>    → prints sha256 + signature for an update manifest
//   voxpad-keygen verify "<name>" <serial>        → checks a serial against the public key compiled into VoxPad.Core

if (args.Length == 0) { Usage(); return 1; }
switch (args[0].ToLowerInvariant())
{
    case "newkey":
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Console.WriteLine("PRIVATE (keep secret, PKCS#8 base64):");
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
        Console.WriteLine();
        Console.WriteLine("PUBLIC (paste into LicenseManager.PublicKeyBase64):");
        Console.WriteLine(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
        return 0;
    }
    case "issue":
    {
        if (args.Length < 3) { Usage(); return 1; }
        byte edition = args.Length > 3 && byte.TryParse(args[3], out var e) ? e : (byte)1;
        var serial = LicenseManager.Issue(args[2], edition, args[1]);
        Console.WriteLine($"Name:   {args[2]}");
        Console.WriteLine($"Serial: {serial}");
        return 0;
    }
    case "sign-update":
    {
        if (args.Length < 3) { Usage(); return 1; }
        using var fs = File.OpenRead(args[2]);
        var hash = SHA256.HashData(fs);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(args[1]), out _);
        var sig = ecdsa.SignHash(hash);
        Console.WriteLine($"\"sha256\": \"{Convert.ToHexString(hash)}\",");
        Console.WriteLine($"\"signature\": \"{Convert.ToBase64String(sig)}\"");
        return 0;
    }
    case "verify":
    {
        if (args.Length < 3) { Usage(); return 1; }
        var ok = LicenseManager.Verify(args[1], args[2], out var ed);
        Console.WriteLine(ok ? $"VALID ({ed})" : "INVALID");
        return ok ? 0 : 2;
    }
    default:
        Usage();
        return 1;
}

static void Usage()
{
    Console.WriteLine("voxpad-keygen newkey | issue <privateKey> \"<name>\" [1|2] | sign-update <privateKey> <file> | verify \"<name>\" <serial>");
}
