using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace ChatZ;

public class AppSettings
{
    public string? ViberPath { get; set; }
    public string LastService { get; set; } = "wa";

    /// <summary>PBKDF2 heš PIN-a (Base64). null = PIN zaštita isključena.</summary>
    public string? PinHash { get; set; }
    public string? PinSalt { get; set; }

    /// <summary>Zaključavanje posle ovoliko minuta neaktivnosti (0 = nikad).</summary>
    public int AutoLockMinutes { get; set; } = 5;

    public bool PinEnabled => !string.IsNullOrEmpty(PinHash) && !string.IsNullOrEmpty(PinSalt);

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChatZ");

    private static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void SetPin(string pin)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        PinSalt = Convert.ToBase64String(salt);
        PinHash = Convert.ToBase64String(Derive(pin, salt));
    }

    public void ClearPin()
    {
        PinHash = null;
        PinSalt = null;
    }

    public bool VerifyPin(string pin)
    {
        if (!PinEnabled) return true;
        try
        {
            byte[] salt = Convert.FromBase64String(PinSalt!);
            byte[] expected = Convert.FromBase64String(PinHash!);
            return CryptographicOperations.FixedTimeEquals(Derive(pin, salt), expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, 200_000, HashAlgorithmName.SHA256, 32);
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ChatZ";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
        else
            key.DeleteValue(ValueName, false);
    }
}
