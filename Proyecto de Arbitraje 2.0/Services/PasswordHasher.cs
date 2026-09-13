using System.Security.Cryptography;

namespace ProyectoArbitraje.Services;

// Hashing de contraseñas con PBKDF2 (nativo de .NET, sin paquetes extra).
// Formato guardado: "iteraciones.saltBase64.hashBase64"
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var partes = stored.Split('.');
        if (partes.Length != 3) return false;

        if (!int.TryParse(partes[0], out int iteraciones)) return false;

        byte[] salt, hashEsperado;
        try
        {
            salt = Convert.FromBase64String(partes[1]);
            hashEsperado = Convert.FromBase64String(partes[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var hashIntento = Rfc2898DeriveBytes.Pbkdf2(password, salt, iteraciones, HashAlgorithmName.SHA256, hashEsperado.Length);
        return CryptographicOperations.FixedTimeEquals(hashIntento, hashEsperado);
    }
}