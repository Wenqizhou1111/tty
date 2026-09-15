using System;
using System.Security.Cryptography;
using System.Text;

namespace MyIme.Shared
{
    /// <summary>
    /// API Key 的存取。用 Windows DPAPI 按当前用户加密，
    /// 所以 settings.json 里**永远不会出现明文 key**，复制到别的机器也解不开。
    /// </summary>
    public static class SecretStore
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MyIME.SecretStore.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(enc);
            }
            catch (Exception ex)
            {
                Log.Error("加密密钥失败", ex);
                return "";
            }
        }

        public static string Unprotect(string protectedBase64)
        {
            if (string.IsNullOrEmpty(protectedBase64)) return "";
            try
            {
                byte[] enc = Convert.FromBase64String(protectedBase64);
                byte[] raw = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch (Exception ex)
            {
                Log.Warn("解密密钥失败（可能换了 Windows 账户或换了机器）: " + ex.Message);
                return "";
            }
        }

        public static bool HasApiKey(AiSettings s)
        {
            return s != null && !string.IsNullOrEmpty(s.ApiKeyProtected) && Unprotect(s.ApiKeyProtected).Length > 0;
        }

        public static string GetApiKey(AiSettings s)
        {
            if (s == null) return "";
            return Unprotect(s.ApiKeyProtected);
        }

        public static void SetApiKey(AiSettings s, string plain)
        {
            if (s == null) return;
            s.ApiKeyProtected = string.IsNullOrEmpty(plain) ? "" : Protect(plain);
        }

        /// <summary>把 key 打码，只留前 4 位，写日志用。</summary>
        public static string Mask(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(未设置)";
            if (key.Length <= 8) return "****";
            return key.Substring(0, 4) + "****" + key.Substring(key.Length - 2, 2);
        }
    }
}
