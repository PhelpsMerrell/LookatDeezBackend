# Azure Function App Configuration for CIAM

## Required Application Settings:

### CIAM Configuration:
- AzureAd_TenantId = f8c9ea6d-89ab-4b1e-97db-dc03a426ec60
- AzureAd_ClientId = f0749993-27a7-486f-930d-16a825e017bf
- AzureAd_UserFlow = B2C_1_signupsignin1

### CosmosDB Configuration:
- CosmosConnectionString = [Your Production CosmosDB Connection String]
- CosmosDb_DatabaseName = lookatdeez-db

### CORS Configuration:
- WEBSITE_CORS_ALLOWED_ORIGINS = https://lookatdeez.com,http://localhost:5173
- WEBSITE_CORS_SUPPORT_CREDENTIALS = true

## IMPORTANT: Remove Any B2C References
Make sure to remove any old/conflicting settings that might reference:
- AzureAd_ClientSecret (not needed for CIAM)
- Any old CIAM domain references
- Any regular Azure AD settings

## CIAM Endpoints Used:
- Authority: https://lookatdeez.ciamlogin.com/f8c9ea6d-89ab-4b1e-97db-dc03a426ec60
- JWKS: https://lookatdeez.ciamlogin.com/f8c9ea6d-89ab-4b1e-97db-dc03a426ec60/discovery/v2.0/keys?p=B2C_1_signupsignin1
- Token: https://lookatdeez.ciamlogin.com/f8c9ea6d-89ab-4b1e-97db-dc03a426ec60/oauth2/v2.0/token?p=B2C_1_signupsignin1

## Expected CIAM Token Claims:
- Issuer: https://lookatdeez.ciamlogin.com/f8c9ea6d-89ab-4b1e-97db-dc03a426ec60/v2.0/
- Audience: f0749993-27a7-486f-930d-16a825e017bf or https://lookatdeez.onmicrosoft.com/f0749993-27a7-486f-930d-16a825e017bf/access
- User ID: oid or sub claim
