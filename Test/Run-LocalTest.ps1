$uri = "http://localhost:7071/api/CodeSandboxOrchestrator_HttpStart"
$filePath = ".\\Loop.cs"

$res = Invoke-RestMethod -Uri $uri -Method Post -InFile $filePath

Write-Host "Status: $($res.statusQueryGetUri)"
$res