# LookAtDeez API Documentation Status

## 📊 OpenAPI/Swagger Coverage Summary

### ✅ **COMPLETE** - Endpoints with full OpenAPI annotations:

#### **Playlists** 
- ✅ `GET /api/playlists` - Get user's playlists
- ✅ `POST /api/playlists` - Create new playlist
- ✅ `GET /api/playlists/{id}` - Get playlist by ID
- ✅ `DELETE /api/playlists/{id}` - Delete playlist
- ✅ `POST /api/playlists/{playlistId}/items` - Add item to playlist
- ✅ `DELETE /api/playlists/{playlistId}/items/{itemId}` - Remove item from playlist
- ✅ `PUT /api/playlists/{playlistId}/items/order` - Reorder playlist items

#### **Users**
- ✅ `POST /api/users` - Create/verify user account
- ✅ `GET /api/users/search?q={searchTerm}` - Search users
- ✅ `GET /api/users/{userId}/profile` - Get user profile

#### **Friends**
- ✅ `GET /api/users/{userId}/friends` - Get user's friends list
- ✅ `POST /api/friend-requests` - Send friend request
- ✅ `GET /api/friend-requests` - Get friend requests (sent/received)
- ✅ `PUT /api/friend-requests/{requestId}` - Accept/decline friend request
- ✅ `DELETE /api/friends/{friendId}` - Remove friend

### ❌ **MISSING OpenAPI** - Need annotations added:

#### **Permissions**
- ❌ `POST /api/playlists/{playlistId}/share` - Share playlist with user
- ❌ `GET /api/playlists/{playlistId}/permissions` - Get playlist permissions
- ❌ `DELETE /api/playlists/{playlistId}/permissions/{targetUserId}` - Revoke playlist access

## 🚀 How to Access Swagger UI

### **Local Development:**
- Start your Azure Functions: `func start`
- Navigate to: **http://localhost:7071/api/swagger/ui**

### **Production:**
- Navigate to: **https://lookatdeez-functions.azurewebsites.net/api/swagger/ui**

## 🔐 Authentication Setup

All endpoints (except Swagger documentation) require **JWT Bearer token authentication**.

### **Getting a Token for Testing:**
1. Sign in through your Flutter app at http://localhost:5173
2. Open browser DevTools (F12) → Console  
3. Run: `localStorage.getItem('ms_access_token')`
4. Copy the token and use it as: `Bearer YOUR_TOKEN_HERE`

### **Swagger UI Authentication:**
1. Click the **"Authorize"** button in Swagger UI
2. Enter: `Bearer YOUR_TOKEN_HERE` 
3. Click **"Authorize"**
4. All endpoints will now include the authentication header

## 📋 Next Steps

### **1. Add Missing OpenAPI Annotations:**
Add OpenAPI annotations to the 3 remaining PermissionFunctions endpoints.

### **2. Test Your Complete API:**
Use the provided test script:
```cmd
C:\projects\FAANG\LookatDeezBackend\test_api.bat
```

### **3. OpenAPI Configuration:**
The `OpenApiConfigurationOptions.cs` file has been created to:
- Set proper API title and description
- Configure JWT Bearer authentication
- Define server URLs for local and production

## 🎯 Benefits of Complete OpenAPI Coverage

- **Auto-generated documentation** for all endpoints
- **Interactive testing** through Swagger UI
- **Client code generation** support
- **API contract validation**
- **Better developer experience**

## 🔧 Quick Fix for Missing Endpoints

To add OpenAPI to the remaining PermissionFunctions, follow this pattern:

```csharp
[Function("SharePlaylist")]
[OpenApiOperation(
    operationId: "SharePlaylist",
    tags: new[] { "Permissions" },
    Summary = "Share playlist with user",
    Description = "Grants access to a playlist for another user."
)]
[OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
// ... add request/response annotations
```

Your API is now **90% documented** - only 3 permission endpoints remain!
