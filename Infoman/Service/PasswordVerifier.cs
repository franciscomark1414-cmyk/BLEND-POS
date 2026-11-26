// Services/PasswordVerifier.cs
using Infoman.Models;
using Microsoft.AspNetCore.Identity;

namespace Infoman.Services
{
    public static class PasswordVerifier
    {
        // 🔐 IDINAGDAG ITO PARA MA-FIX ANG ERROR
        public static string Hash(User user, string plainPassword)
        {
            var hasher = new PasswordHasher<User>();
            return hasher.HashPassword(user, plainPassword);
        }

        public static bool Matches(User user, string input)
        {
            var stored = user.Password?.Trim() ?? "";
            input = input?.Trim() ?? "";
            if (stored.Length == 0 || input.Length == 0) return false;

            if (stored.StartsWith("AQAAAA") && stored.Length >= 80)
            {
                var hasher = new PasswordHasher<User>();
                return hasher.VerifyHashedPassword(user, stored, input)
                       != PasswordVerificationResult.Failed;
            }

            return stored == input;
        }
    }
}