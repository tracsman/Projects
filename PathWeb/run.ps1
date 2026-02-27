# Run PathWeb Application

Write-Host 'Starting PathWeb...' -ForegroundColor Green
Write-Host 'The application will open in your browser automatically.' -ForegroundColor Cyan
Write-Host 'Press Ctrl+C to stop the application.' -ForegroundColor Yellow
Write-Host ''

# Start the application
Start-Process 'https://localhost:7001' -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Start-Process 'http://localhost:5001' -ErrorAction SilentlyContinue

dotnet run
