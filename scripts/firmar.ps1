# Firma un ejecutable de AppRouter con el certificado personal de firma de código (CN=rboluda).
# Lo llama scripts\publicar.ps1 después de compilar. En AppRouter el programa, el instalador y el
# desinstalador son el mismo ejecutable (AppRouter.exe), así que se firma un único archivo.
#
# El certificado NO está en el repositorio: %USERPROFILE%\.firma\rboluda-firma-codigo.pfx y su
# contraseña en rboluda-firma-codigo.clave. Si no están (otra persona compilando el código),
# no se firma y la compilación sigue.
#
# Uso:  .\scripts\firmar.ps1 <archivo.exe>
param([Parameter(Mandatory)][string]$Archivo)

$ErrorActionPreference = "Stop"

# Solo se firman ejecutables nuestros. sing-box (y cualquier otro binario de terceros) se
# distribuye tal como lo publican sus autores.
if ((Split-Path $Archivo -Leaf) -notlike "AppRouter*.exe") {
    Write-Host "No es un ejecutable de AppRouter; no se firma: $Archivo"
    exit 0
}

$carpeta = Join-Path $env:USERPROFILE ".firma"
$pfx = Join-Path $carpeta "rboluda-firma-codigo.pfx"
$clave = Join-Path $carpeta "rboluda-firma-codigo.clave"
if (-not (Test-Path $pfx) -or -not (Test-Path $clave)) {
    Write-Warning "Sin certificado de firma en $carpeta`: $Archivo queda sin firmar"
    exit 0
}

# signtool del Windows SDK: el de versión más alta
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]"0.0" } } -Descending |
    Select-Object -First 1
if (-not $signtool) { throw "No se encuentra signtool.exe (Windows SDK)" }

# Con sello de tiempo: la firma sigue siendo válida después de que caduque el certificado
& $signtool.FullName sign /fd sha256 /f $pfx /p ([IO.File]::ReadAllText($clave).Trim()) `
    /tr "http://timestamp.digicert.com" /td sha256 /d "AppRouter" /du "https://github.com/rboluda/approuter" `
    /q $Archivo
if ($LASTEXITCODE -ne 0) { throw "No se pudo firmar $Archivo" }
Write-Host "Firmado: $Archivo"
