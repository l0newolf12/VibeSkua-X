using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Skua.Core.Utils;

public static class CryptoUtils
{
    private static readonly byte[] Salt = { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 };

    private static byte[] GetKey()
    {
        string machineName = Environment.MachineName ?? "UnknownMachine";
        string userName = Environment.UserName ?? "UnknownUser";
        string passPhrase = $"{machineName}-{userName}-SkuaSecureKey";

        return Rfc2898DeriveBytes.Pbkdf2(passPhrase, Salt, 10000, HashAlgorithmName.SHA256, 32); // 256 bits
    }

    private static byte[] GetIV()
    {
        return Rfc2898DeriveBytes.Pbkdf2("SkuaStaticIV-DoNotChange", Salt, 10000, HashAlgorithmName.SHA256, 16); // 128 bits
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        if (plainText.StartsWith("enc:"))
            return plainText;

        try
        {
            using Aes aes = Aes.Create();
            aes.Key = GetKey();
            aes.IV = GetIV();

            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using MemoryStream ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (StreamWriter sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);
            }
            return "enc:" + Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[CryptoUtils.Encrypt] Failed: {ex.Message}");
            return plainText;
        }
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        if (!cipherText.StartsWith("enc:"))
            return cipherText;

        try
        {
            string actualCipher = cipherText.Substring(4);
            byte[] cipherBytes = Convert.FromBase64String(actualCipher);

            using Aes aes = Aes.Create();
            aes.Key = GetKey();
            aes.IV = GetIV();

            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using MemoryStream ms = new MemoryStream(cipherBytes);
            using CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using StreamReader sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[CryptoUtils.Decrypt] Failed: {ex.Message}");
            return cipherText;
        }
    }
}
