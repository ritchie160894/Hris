# Run once per terminal session if `git` is not recognized:
#   . D:\hris\setup-git-path.ps1
if (Test-Path "C:\Program Files\Git\cmd\git.exe") {
    $env:Path = "C:\Program Files\Git\cmd;" + $env:Path
    Write-Host "Git ready:" -ForegroundColor Green
    git --version
} else {
    Write-Host "Git not found. Install from https://git-scm.com/download/win" -ForegroundColor Red
}
