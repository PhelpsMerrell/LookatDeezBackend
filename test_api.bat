@echo off
echo Testing LookAtDeez Backend API...
echo.

REM You'll need to replace YOUR_JWT_TOKEN with an actual token from your browser
REM 1. Go to http://localhost:5173
REM 2. Sign in with Microsoft
REM 3. Open browser DevTools (F12) -> Console
REM 4. Run: localStorage.getItem('ms_access_token')
REM 5. Copy the token (without quotes) and replace YOUR_JWT_TOKEN below

set JWT_TOKEN=YOUR_JWT_TOKEN_HERE

if "%JWT_TOKEN%"=="YOUR_JWT_TOKEN_HERE" (
    echo ERROR: You need to set your JWT token first!
    echo 1. Sign in at http://localhost:5173
    echo 2. Open browser DevTools ^(F12^) -^> Console
    echo 3. Run: localStorage.getItem^('ms_access_token'^)
    echo 4. Copy the token and edit this file
    echo.
    pause
    exit /b 1
)

echo Testing with JWT token: %JWT_TOKEN:~0,20%...
echo.

REM Test 1: Create a user (this should work if JWT is valid)
echo === Test 1: Create User ===
curl -X POST "http://localhost:7071/api/users" ^
    -H "Authorization: Bearer %JWT_TOKEN%" ^
    -H "Content-Type: application/json" ^
    -d "{\"email\":\"test@example.com\",\"displayName\":\"Test User\"}" ^
    -v

echo.
echo.

REM Test 2: Get playlists (should return empty list initially)
echo === Test 2: Get Playlists ===
curl -X GET "http://localhost:7071/api/playlists" ^
    -H "Authorization: Bearer %JWT_TOKEN%" ^
    -H "Content-Type: application/json" ^
    -v

echo.
echo.

REM Test 3: Create a playlist
echo === Test 3: Create Playlist ===
curl -X POST "http://localhost:7071/api/playlists" ^
    -H "Authorization: Bearer %JWT_TOKEN%" ^
    -H "Content-Type: application/json" ^
    -d "{\"title\":\"Test Playlist\",\"isPublic\":false}" ^
    -v

echo.
echo.

REM Test 4: Get playlists again (should show the created playlist)
echo === Test 4: Get Playlists Again ===
curl -X GET "http://localhost:7071/api/playlists" ^
    -H "Authorization: Bearer %JWT_TOKEN%" ^
    -H "Content-Type: application/json" ^
    -v

echo.
echo.
echo Tests completed! Check the responses above for any errors.
pause
