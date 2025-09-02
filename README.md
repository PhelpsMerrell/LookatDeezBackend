# LookAtDeez Backend 🎵

A powerful Azure Functions-based backend API for managing music playlists, social features, and user authentication.

## 🌐 Live Application
**Frontend**: [https://lookatdeez.com](https://lookatdeez.com)  
**API Documentation**: [[https://lookatdeez-functions.azurewebsites.net/api/swagger/ui]([https://lookatdeez-functions.azurewebsites.net/api/swagger/ui](https://lookatdeez-functions.azurewebsites.net/api/swagger/ui?url=/api/openapi/v3.json))
](https://lookatdeez-functions.azurewebsites.net/api/swagger/ui?url=/api/openapi/v3.json)
## 🚀 What is LookAtDeez?

LookAtDeez is a modern social music playlist platform that lets users create, share, and manage video playlists from various sources like YouTube, TikTok, Instagram Reels, and more. Think of it as your personal playlist curator with social sharing capabilities.

## 🏗️ Architecture

### Technology Stack
- **Runtime**: .NET 8 Azure Functions (Isolated Worker)
- **Database**: Azure Cosmos DB
- **Authentication**: Microsoft Entra ID (Azure AD) with JWT validation
- **API Documentation**: OpenAPI/Swagger
- **Deployment**: Azure Functions Premium Plan



## 📁 Project Structure

```
LookatDeezBackend/
├── LookatDeezBackend/           # Main Azure Functions project
│   ├── Functions/               # HTTP-triggered functions
│   │   ├── UserFunctions.cs     # User management endpoints
│   │   ├── PlaylistFunctions.cs # Playlist CRUD operations
│   │   └── FriendFunctions.cs   # Social features
│   ├── Middleware/              # Custom middleware
│   │   └── JwtAuthenticationMiddleware.cs
│   ├── Extensions/              # Helper extensions
│   │   ├── AuthHelper.cs        # JWT validation logic
│   │   └── FunctionContextExtensions.cs
│   └── Helpers/                 # Utility classes
│       └── CorsHelper.cs        # CORS management
├── LookatDeezBackend.Data/      # Data access layer
│   ├── Models/                  # Domain models
│   ├── Repositories/            # Data repositories
│   └── Services/                # Business logic services
└── README.md                    # This file
```

## 🔌 API Endpoints

### 👤 User Management
- `POST /api/users` - Create user account
- `GET /api/users/{userId}/profile` - Get user profile
- `GET /api/users/search?q={term}` - Search users

### 📱 Playlist Operations
- `GET /api/playlists` - List user's playlists
- `POST /api/playlists` - Create new playlist
- `GET /api/playlists/{id}` - Get playlist details
- `DELETE /api/playlists/{id}` - Delete playlist
- `POST /api/playlists/{id}/items` - Add item to playlist
- `DELETE /api/playlists/{id}/items/{itemId}` - Remove playlist item
- `PUT /api/playlists/{id}/items/order` - Reorder playlist items

### 👥 Social Features
- `POST /api/friend-requests` - Send friend request
- `GET /api/friend-requests` - List friend requests
- `PUT /api/friend-requests/{id}` - Accept/decline friend request
- `GET /api/users/{userId}/friends` - Get user's friends
- `DELETE /api/friends/{friendId}` - Remove friend

## 🔑 Authentication

The API uses **Microsoft Entra ID** for authentication with JWT Bearer tokens. All endpoints (except user creation) require valid authentication.

### Headers Required
```http
Authorization: Bearer {jwt-token}
Content-Type: application/json
```

## 🗄️ Database Schema

### Collections
- **users** - User profiles and authentication data
- **playlists** - Playlist metadata and items
- **permissions** - Playlist sharing permissions
- **friendrequests** - Social connection requests

### Partition Keys
- **users**: `id` (user ID)
- **playlists**: `ownerId` (playlist owner)
- **permissions**: `playlistId`
- **friendrequests**: `fromUserId`

## 🚀 Local Development

### Prerequisites
- .NET 8 SDK
- Azure Functions Core Tools v4
- Azure Cosmos DB Emulator
- Visual Studio 2022 or VS Code

### Setup
1. **Clone and restore packages**
   ```bash
   cd LookatDeezBackend
   dotnet restore
   ```

2. **Configure local settings**
   ```json
   // local.settings.json
   {
     \"Values\": {
       \"CosmosConnectionString\": \"AccountEndpoint=https://localhost:8081/;AccountKey=...\",
       \"CosmosDb_DatabaseName\": \"lookatdeez-db\",
       \"AzureAd_TenantId\": \"your-tenant-id\",
       \"AzureAd_ClientId\": \"your-client-id\"
     },
     \"Host\": {
       \"CORS\": \"http://localhost:5173, https://lookatdeez.com\"
     }
   }
   ```

3. **Run locally**
   ```bash
   cd LookatDeezBackend
   func start
   ```

4. **Access Swagger UI**
   Open `http://localhost:7071/api/swagger/ui`




### CORS Settings
Configured to allow requests from:
- `https://lookatdeez.com` (production)


## 🔍 Monitoring & Logging

- **Application Insights**: Integrated telemetry and performance monitoring
- **Structured Logging**: Detailed request/response logging for debugging
- **Health Checks**: Built-in Azure Functions health monitoring



