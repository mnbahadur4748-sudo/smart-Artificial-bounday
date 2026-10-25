using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AIBoundary.Core.Security;

/// <summary>পাসওয়ার্ড হ্যাশ, ডেটা এনক্রিপশন (AES-256-GCM) ও কী সুরক্ষা (Windows DPAPI)।</summary>
public static class CryptoService
{
    private const int Iterations = 600_000;   // OWASP সুপারিশ (PBKDF2-SHA512)
    private static byte[]? _key;
    private static readonly object _lock = new();

    private static byte[] MasterKey
    {
        get { lock (_lock) { _key ??= LoadOrCreateKey(); return _key; } }
    }

    private static string KeyPath => Path.Combine(AppPaths.DataDir, "master.key");

    // মাস্টার কী ফাইলে সরাসরি থাকে না; Windows ইউজার অ্যাকাউন্টের সাথে বাঁধা (DPAPI) হয়ে থাকে
    private static byte[] LoadOrCreateKey()
    {
        if (File.Exists(KeyPath))
        {
            var protectedBytes = File.ReadAllBytes(KeyPath);
            return ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        }
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(KeyPath, ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser));
        return key;
    }

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt,
            Iterations, HashAlgorithmName.SHA512, 64);
        return $"pbkdf2-sha512${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        try
        {
            var p = stored.Split('$');
            if (p.Length != 4) return false;
            int it = int.Parse(p[1]);
            var salt = Convert.FromBase64String(p[2]);
            var expected = Convert.FromBase64String(p[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt,
                it, HashAlgorithmName.SHA512, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }

    public static string Encrypt(string plain) => Convert.ToBase64String(EncryptBytes(Encoding.UTF8.GetBytes(plain)));

    public static string Decrypt(string base64) => Encoding.UTF8.GetString(DecryptBytes(Convert.FromBase64String(base64)));

    /// <summary>ছবির মতো বাইনারি ডেটার এনক্রিপশন (AES-256-GCM)। ফরম্যাট: nonce(12) + tag(16) + ciphertext।</summary>
    public static byte[] EncryptBytes(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(MasterKey, 16);
        aes.Encrypt(nonce, data, cipher, tag);
        var all = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(all, 0);
        tag.CopyTo(all, 12);
        cipher.CopyTo(all, 28);
        return all;
    }

    public static byte[] DecryptBytes(byte[] all)
    {
        var nonce = all[..12];
        var tag = all[12..28];
        var cipher = all[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(MasterKey, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    /// <summary>খোঁজার জন্য (যেমন ইমেইল আগে ব্যবহার হয়েছে কিনা) আসল ইমেইল না রেখে এই চিহ্ন রাখা হয়।</summary>
    public static string Hmac(string value)
    {
        using var h = new HMACSHA256(MasterKey);
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
