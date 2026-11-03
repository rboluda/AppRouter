# Publica una versión de AppRouter: compila, FIRMA, calcula SHA256SUMS.txt (después de firmar:
# la actualización automática comprueba ese hash), sube la etiqueta y crea la Release en GitHub.
#
# Se ejecuta en el equipo de rboluda, que es donde está el certificado de firma
# (ver scripts\firmar.ps1). GitHub Actions compila cada push y cada etiqueta, pero ya no crea
# Releases: sin el certificado, lo que publicara iría sin firmar.
#
# Requisitos: SDK de .NET 8+, GitHub CLI con sesión iniciada (gh auth login), árbol de git
# limpio en main y <Version> del .csproj igual a la versión que se publica.
#
# Uso:  .\scripts\publicar.ps1 1.4.1              compila, firma y publica
#       .\scripts\publicar.ps1 1.4.1 -SoloCompilar deja en dist\ lo que se publicaría, sin publicar
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$SoloCompilar,
    # Publicar aunque no se haya podido firmar (otra persona, sin el certificado)
    [switch]$PermitirSinFirma
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
Set-Location $raiz
$repo = "rboluda/approuter"
$etiqueta = "v$Version"

# --- Comprobaciones ---
$enCsproj = ([xml](Get-Content AppRouter\AppRouter.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($enCsproj -ne $Version) { throw "El .csproj tiene la versión $enCsproj, no $Version. Súbela y haz commit antes de publicar." }
if (-not $SoloCompilar) {
    if (git status --porcelain) { throw "Hay cambios sin commit: publica solo lo que está en git." }
    if ((git rev-parse --abbrev-ref HEAD) -ne "main") { throw "Publica desde la rama main." }
    if (git tag --list $etiqueta) { throw "La etiqueta $etiqueta ya existe." }
    gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI sin sesión: ejecuta 'gh auth login'." }
}

# --- Compilar ---
if (Test-Path dist) { Remove-Item dist -Recurse -Force }
dotnet publish AppRouter\AppRouter.csproj -c Release -o dist -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "La compilación ha fallado." }
$exe = Join-Path $raiz "dist\AppRouter-$Version.exe"
Move-Item (Join-Path $raiz "dist\AppRouter.exe") $exe

# --- Firmar (antes de calcular los hashes) ---
& (Join-Path $PSScriptRoot "firmar.ps1") $exe
$firma = Get-AuthenticodeSignature $exe
$firmado = $null -ne $firma.SignerCertificate
if ($firmado) {
    Write-Host "Firma: $($firma.SignerCertificate.Subject) · sello de tiempo: $($firma.TimeStamperCertificate.Subject)"
} else {
    Write-Warning "El ejecutable NO está firmado."
}

# --- Archivos de la Release ---
Copy-Item docs\LEEME.txt dist\
Get-ChildItem dist -File | Where-Object Name -ne "SHA256SUMS.txt" | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)"
} | Out-File dist\SHA256SUMS.txt -Encoding ascii
Get-ChildItem dist -File | Format-Table Name, Length -AutoSize

if ($SoloCompilar) { Write-Host "Listo en dist\ (no se ha publicado nada)."; exit 0 }
if (-not $firmado -and -not $PermitirSinFirma) { throw "No se publica sin firmar (usa -PermitirSinFirma si es lo que quieres)." }

# --- Publicar ---
git tag -a $etiqueta -m "AppRouter $Version"
git push origin main
git push origin $etiqueta
if ($LASTEXITCODE -ne 0) { throw "No se pudo subir la etiqueta." }

$aviso = if ($firmado) {
    "El ejecutable está firmado digitalmente por rboluda (certificado propio). Como no es de una entidad reconocida, Windows puede mostrar igualmente «Windows protegió su PC»: pulsa «Más información» → «Ejecutar de todas formas»."
} else {
    "Si Windows muestra «Windows protegió su PC», pulsa «Más información» → «Ejecutar de todas formas» (el ejecutable no está firmado digitalmente)."
}
$notas = @"
**Descarga [AppRouter-$Version.exe](https://github.com/$repo/releases/download/$etiqueta/AppRouter-$Version.exe)**, ábrelo y pulsa «Instalar» (o «Actualizar»).
No hace falta instalar nada más: incluye .NET y sing-box.

$aviso

Cambios: ver [CHANGELOG.md](https://github.com/$repo/blob/$etiqueta/CHANGELOG.md).

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org) — see the [code signing policy](https://github.com/$repo#code-signing-policy) and [privacy](https://github.com/$repo#privacy) statements.
"@
gh release create $etiqueta (Get-ChildItem dist -File).FullName --repo $repo --title "AppRouter $Version" --notes $notas
if ($LASTEXITCODE -ne 0) { throw "No se pudo crear la Release." }
Write-Host "Publicada: https://github.com/$repo/releases/tag/$etiqueta"
