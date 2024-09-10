# Check Python 3.9 installation, required libraries, and install missing ones

# Function to check if a command exists
function Test-Command($command) {
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'stop'
    try { if (Get-Command $command) { return $true } }
    catch { return $false }
    finally { $ErrorActionPreference = $oldPreference }
}

# Check if Python 3.9 is installed
if (Test-Command python) {
    $pythonVersion = python --version
    if ($pythonVersion -like "*Python 3.9*") {
        Write-Host "Python 3.9 is installed: $pythonVersion" -ForegroundColor Green
    } else {
        Write-Host "Python 3.9 is not installed. Found: $pythonVersion" -ForegroundColor Red
        exit
    }
} else {
    Write-Host "Python is not installed or not in PATH" -ForegroundColor Red
    exit
}

# Check if pip is installed
if (-not (Test-Command pip)) {
    Write-Host "pip is not installed. Please install pip to proceed." -ForegroundColor Red
    exit
}

# List of required libraries
$requiredLibs = @{
    "numpy" = "1.26.4"
    "pandas" = "1.3.2"
    "matplotlib" = "3.4.2"
    "scipy" = "1.7.1"
    "scikit-learn" = "1.4.2"
    "tensorflow" = "2.10.0"
    "torch" = "2.2.2"
    "pillow" = "10.3.0"
    "requests" = "2.32.3"
    "opencv-python" = "4.5.5.64"
}

# Function to get installed version of a library
function Get-InstalledVersion($lib) {
    $version = pip show $lib 2>$null | Select-String -Pattern "Version:"
    if ($version) {
        return $version.ToString().Split(":")[1].Trim()
    }
    return $null
}

# Function to install a specific version of a library
function Install-SpecificVersion($lib, $version) {
    Write-Host "Installing $lib version $version..." -ForegroundColor Yellow
    $output = pip install "$lib==$version" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "$lib version $version installed successfully." -ForegroundColor Green
    } else {
        Write-Host "Failed to install $lib version $version. Error: $output" -ForegroundColor Red
    }
}

# Check each required library and install if missing or version mismatch
foreach ($lib in $requiredLibs.Keys) {
    $requiredVersion = $requiredLibs[$lib]
    $installedVersion = Get-InstalledVersion $lib
    
    if ($installedVersion -eq $null) {
        Write-Host "$lib is not installed" -ForegroundColor Red
        $install = Read-Host "Do you want to install $lib version $requiredVersion? (Y/N)"
        if ($install -eq 'Y' -or $install -eq 'y') {
            Install-SpecificVersion $lib $requiredVersion
        }
    } elseif ($installedVersion -ne $requiredVersion) {
        Write-Host "$lib version mismatch. Installed: $installedVersion, Required: $requiredVersion" -ForegroundColor Yellow
        $update = Read-Host "Do you want to update $lib to version $requiredVersion? (Y/N)"
        if ($update -eq 'Y' -or $update -eq 'y') {
            Install-SpecificVersion $lib $requiredVersion
        }
    } else {
        Write-Host "$lib version $installedVersion is correctly installed." -ForegroundColor Green
    }
}

Write-Host "Script execution completed." -ForegroundColor Cyan