# Colaborar

## Compilar y probar

Requisitos: Windows 10/11 x64 y el [SDK de .NET 8](https://dotnet.microsoft.com/download) o posterior.

```bash
dotnet build AppRouter/AppRouter.csproj -c Debug
```

La primera compilación descarga sing-box y verifica su SHA-256 (ver [AppRouter/vendor/README.txt](AppRouter/vendor/README.txt)).
Para probar la app hace falta instalarla (necesita permisos de administrador): `dotnet publish AppRouter/AppRouter.csproj -c Release -o dist` y ejecutar `dist/AppRouter.exe`.

## Traducciones

Los textos de la interfaz se escriben en español en el código, siempre a través de `L.T("…")` (o `L.F("… {0} …", valor)` si llevan datos).
[AppRouter/Strings.cs](AppRouter/Strings.cs) tiene, para cada texto en español, su versión en inglés y en chino simplificado. Un texto sin traducir se muestra en español.

Después de añadir o cambiar textos, comprueba que no falta ninguno:

```bash
python tools/check-translations.py AppRouter --missing
```

Debe terminar con `faltan: 0  sobran: 0`. Las traducciones al chino agradecen revisión de alguien nativo.

**Añadir un idioma:** añade su código y nombre a `L.Languages` y su caso en `L.T`, añade un campo a la tupla de `Strings.cs` con todas las traducciones y, si su alfabeto no está en Segoe UI, su fuente en `L.FontName`.

## Cambios

- Una rama por cambio y un pull request contra `main`; GitHub Actions compila cada pull request.
- Anota los cambios visibles para el usuario en [CHANGELOG.md](CHANGELOG.md).
- Al tocar la configuración de sing-box, comprueba que `sing-box check` la acepta y prueba el enrutado de verdad.
