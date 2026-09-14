using System.Security.Cryptography;
using System.Text;

namespace Doctor.Web.Services.Security;

public static class PasswordHasher
{
    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        byte[] result = new byte[1 + salt.Length + hash.Length];
        result[0] = 0x01; // Version
        Buffer.BlockCopy(salt, 0, result, 1, salt.Length);
        Buffer.BlockCopy(hash, 0, result, 1 + salt.Length, hash.Length);

        return Convert.ToBase64String(result);
    }

    public static bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(hashedPassword);
            if (bytes.Length != 1 + 16 + 32 || bytes[0] != 0x01)
            {
                return false;
            }

            byte[] salt = new byte[16];
            Buffer.BlockCopy(bytes, 1, salt, 0, 16);

            byte[] actualHash = new byte[32];
            Buffer.BlockCopy(bytes, 17, actualHash, 0, 32);

            byte[] expectedHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(providedPassword),
                salt,
                iterations: 100_000,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: 32);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
