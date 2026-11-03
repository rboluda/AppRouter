# AppRouter

App de escritorio (WinForms, .NET 8) para Windows que elige por qué interfaz de red sale cada
programa, usando sing-box. Código en `AppRouter/`, documentación de usuario en `docs/LEEME.txt`,
historial en `CHANGELOG.md`.

- Un único ejecutable autocontenido, `AppRouter.exe`, hace de programa, instalador (si se ejecuta
  fuera de `C:\Program Files\AppRouter`), desinstalador (`--uninstall`), servicio de Windows
  (`--service`) y actualizador silencioso (`--update`).
- Textos de la interfaz siempre con `L.T("…")` / `L.F("…")` y su traducción en `AppRouter/Strings.cs`;
  comprobar con `python tools/check-translations.py AppRouter --missing`.
- Commits sin atribución a Claude y con el correo privado de GitHub (ya configurado en el repo).

## Publicar una versión

1. Subir `<Version>` en `AppRouter/AppRouter.csproj`, añadir la entrada en `CHANGELOG.md` y hacer commit.
2. `.\scripts\publicar.ps1 X.Y.Z` (necesita GitHub CLI con sesión iniciada). Compila, firma, calcula
   `SHA256SUMS.txt` **después** de firmar (lo comprueba la actualización automática), sube la
   etiqueta `vX.Y.Z` y crea la Release con `gh`. `-SoloCompilar` deja el resultado en `dist\` sin publicar.

GitHub Actions (`.github/workflows/build.yml`) solo compila como comprobación; **no** crea Releases,
porque allí no está el certificado y saldrían sin firmar.

## Firma de código

- Certificado personal de rboluda (no el de Azor; nunca usar AzorCodeSign aquí): autofirmado,
  `CN=rboluda`, huella SHA1 `AFCC10CD1778706726F64437CE0AC9D35A8ED4E5`, válido hasta 2031-11-03.
- Está fuera del repositorio, en `%USERPROFILE%\.firma\`: `rboluda-firma-codigo.pfx` (clave privada),
  `rboluda-firma-codigo.clave` (contraseña del .pfx) y `rboluda-firma-codigo.cer` (público).
  El .pfx y la .clave nunca van al repositorio ni a CI; la contraseña no se escribe en ningún
  archivo del proyecto ni se muestra: `scripts/firmar.ps1` la lee de ese archivo al firmar.
- `scripts/firmar.ps1 <archivo>` firma con el `signtool` de versión más alta del Windows SDK,
  SHA-256 y sello de tiempo de DigiCert (`/d "AppRouter"`, `/du` = URL del repo). Si no encuentra
  el .pfx o la .clave, avisa y sigue sin firmar (otra persona compilando).
- Solo se firman ejecutables nuestros (`AppRouter*.exe`); el script ignora cualquier otro.
  sing-box (de terceros) va dentro del ejecutable tal como lo publican sus autores y no se firma;
  la firma solo añade el bloque de firma al final del .exe y no toca su contenido.
- Comprobar: `Get-AuthenticodeSignature dist\AppRouter-X.Y.Z.exe` → firmante `CN=rboluda`, sello de
  tiempo de DigiCert. El estado `UnknownError` es normal (firma correcta, pero el certificado
  autofirmado no es de confianza en ese equipo).
