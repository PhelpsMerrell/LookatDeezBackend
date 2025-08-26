@echo off
echo Creating required CosmosDB containers...

REM Start the CosmosDB Emulator if it's not running
echo Checking if CosmosDB Emulator is running...
curl -s -k "https://localhost:8081/" > nul 2>&1
if %errorlevel% neq 0 (
    echo CosmosDB Emulator is not running. Please start it first.
    echo Run: "C:\Program Files\Azure Cosmos DB Emulator\Microsoft.Azure.Cosmos.Emulator.exe"
    pause
    exit /b 1
)

echo CosmosDB Emulator is running!

REM Create database and containers using PowerShell and REST API
powershell -Command "& { $headers = @{'Accept'='application/json'; 'Content-Type'='application/json'; 'Authorization'='type%%3Dmaster%%26ver%%3D1.0%%26sig%%3DC2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw%%3D%%3D'}; try { Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db' -Headers $headers -Method Get -SkipCertificateCheck } catch { Write-Host 'Database does not exist, creating...'; $dbBody = @{id='lookatdeez-db'} | ConvertTo-Json; Invoke-RestMethod -Uri 'https://localhost:8081/dbs' -Headers $headers -Method Post -Body $dbBody -ContentType 'application/json' -SkipCertificateCheck } }"

REM Create containers
echo Creating containers...

REM Create users container
powershell -Command "& { $headers = @{'Accept'='application/json'; 'Content-Type'='application/json'; 'Authorization'='type%%3Dmaster%%26ver%%3D1.0%%26sig%%3DC2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw%%3D%%3D'}; $body = @{id='users'; partitionKey=@{paths=@('/id'); kind='Hash'}} | ConvertTo-Json -Depth 3; try { Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db/colls/users' -Headers $headers -Method Get -SkipCertificateCheck } catch { Write-Host 'Creating users container...'; Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db/colls' -Headers $headers -Method Post -Body $body -ContentType 'application/json' -SkipCertificateCheck } }"

REM Create playlists container  
powershell -Command "& { $headers = @{'Accept'='application/json'; 'Content-Type'='application/json'; 'Authorization'='type%%3Dmaster%%26ver%%3D1.0%%26sig%%3DC2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw%%3D%%3D'}; $body = @{id='playlists'; partitionKey=@{paths=@('/ownerId'); kind='Hash'}} | ConvertTo-Json -Depth 3; try { Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db/colls/playlists' -Headers $headers -Method Get -SkipCertificateCheck } catch { Write-Host 'Creating playlists container...'; Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db/colls' -Headers $headers -Method Post -Body $body -ContentType 'application/json' -SkipCertificateCheck } }"

REM Create permissions container
powershell -Command "& { $headers = @{'Accept'='application/json'; 'Content-Type'='application/json'; 'Authorization'='type%%3Dmaster%%26ver%%3D1.0%%26sig%%3DC2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw%%3D%%3D'}; $body = @{id='permissions'; partitionKey=@{paths=@('/playlistId'); kind='Hash'}} | ConvertTo-Json -Depth 3; try { Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db/colls/permissions' -Headers $headers -Method Get -SkipCertificateCheck } catch { Write-Host 'Creating permissions container...'; Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db/colls' -Headers $headers -Method Post -Body $body -ContentType 'application/json' -SkipCertificateCheck } }"

REM Create friend-requests container
powershell -Command "& { $headers = @{'Accept'='application/json'; 'Content-Type'='application/json'; 'Authorization'='type%%3Dmaster%%26ver%%3D1.0%%26sig%%3DC2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw%%3D%%3D'}; $body = @{id='friend-requests'; partitionKey=@{paths=@('/fromUserId'); kind='Hash'}} | ConvertTo-Json -Depth 3; try { Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db/colls/friend-requests' -Headers $headers -Method Get -SkipCertificateCheck } catch { Write-Host 'Creating friend-requests container...'; Invoke-RestMethod -Uri 'https://localhost:8081/dbs/lookatdeez-db/colls' -Headers $headers -Method Post -Body $body -ContentType 'application/json' -SkipCertificateCheck } }"

echo.
echo All containers created successfully!
echo You can now start your backend functions.
echo.
pause
