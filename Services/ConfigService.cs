using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace NovaSCM.Services;

/// <summary>
/// Servizio centralizzato per la gestione della configurazione.
/// Estrae la logica da MainWindow: LoadConfig, SaveConfig, DPAPI, hot-reload.
/// </summary>
public class ConfigService
{
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PolarisManager");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");
    public static string ProfilesDir => Path.Combine(ConfigDir, "profiles");
    public static string DbPath => Path.Combine(ConfigDir, "novascm.db");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(ProfilesDir);
    }

    /// <summary>Carica config dal JSON. Crea default se non esiste.</summary>
    public static Dictionary<string, string> Load()
    {
        EnsureDirectories();
        if (!File.Exists(ConfigPath))
            return new Dictionary<string, string>();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Salva config su disco con indentazione.</summary>
    public static void Save(Dictionary<string, string> config)
    {
        EnsureDirectories();
        var json = JsonSerializer.Serialize(config, _jsonOpts);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>Cifra stringa con DPAPI (CurrentUser). Fallback: plain base64 con prefisso "plain:".</summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            // Fallback: plain base64 (es. Kaspersky blocca DPAPI). Loggato perché
            // significa che un campo sensibile (password/API key) verrà salvato
            // in una forma reversibile invece che cifrata — l'utente deve saperlo.
            try { PolarisManager.App.Log($"ConfigService.Encrypt: DPAPI non disponibile, fallback plain base64 ({ex.GetType().Name}: {ex.Message})"); }
            catch { /* logging best-effort */ }
            return "plain:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }
    }

    /// <summary>Decifra stringa DPAPI. Supporta fallback "plain:" per backward-compat.</summary>
    public static string Decrypt(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        try
        {
            if (encrypted.StartsWith("plain:"))
                return Encoding.UTF8.GetString(Convert.FromBase64String(encrypted[6..]));

            var bytes = Convert.FromBase64String(encrypted);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            // Se il blob DPAPI è stato cifrato con un profilo Windows diverso
            // (config.json copiato su un'altra macchina/reinstallo profilo), qui
            // si restituirebbe il base64 cifrato grezzo come se fosse la password
            // in chiaro — meglio loggare per far capire perché l'auth fallisce
            // "senza motivo" invece di lasciare l'utente a indovinare.
            try { PolarisManager.App.Log($"ConfigService.Decrypt: impossibile decifrare, valore restituito as-is ({ex.GetType().Name}: {ex.Message})"); }
            catch { /* logging best-effort */ }
            return encrypted; // già in chiaro o corrotto — restituisci as-is
        }
    }
}
