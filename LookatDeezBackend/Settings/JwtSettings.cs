using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace LookatDeezBackend.Settings
{
    /// <summary>
    /// Centralized JWT signing configuration. Registered as a singleton in DI.
    /// The signing key comes from the "JwtSigningKey" app setting, which in Azure
    /// should be a Key Vault reference: @Microsoft.KeyVault(SecretUri=https://your-vault.vault.azure.net/secrets/JwtSigningKey)
    /// </summary>
    public class JwtSettings
    {
        public const string Issuer = "lookatdeez-api";
        public const string Audience = "lookatdeez-app";

        public SymmetricSecurityKey SigningKey { get; }
        public SigningCredentials SigningCredentials { get; }

        public JwtSettings(string signingKey)
        {
            if (string.IsNullOrWhiteSpace(signingKey))
                throw new InvalidOperationException(
                    "JwtSigningKey is not configured. " +
                    "Set it in local.settings.json for local dev, or as a Key Vault reference in Azure app settings.");

            if (signingKey.Length < 32)
                throw new InvalidOperationException(
                    "JwtSigningKey must be at least 32 characters for HMAC-SHA256.");

            SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);
        }

        public TokenValidationParameters GetValidationParameters() => new()
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
        };
    }
}
