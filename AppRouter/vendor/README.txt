Aquí se descarga sing-box al compilar (no se guarda en el repositorio).

AppRouter.csproj descarga
  https://github.com/SagerNet/sing-box/releases/download/v<SingBoxVersion>/sing-box-<SingBoxVersion>-windows-amd64.zip
y la compilación falla si su SHA-256 no coincide con <SingBoxSha256>.

Versión actual: 1.14.3
SHA-256:        d8b86ef58936dba9230ec6a57ffa4243933f3c14ef4809828b6c553440ed1518
                (coincide con el "digest" que publica GitHub para ese archivo)

sing-box se incluye sin modificar bajo la licencia GPLv3 (su LICENSE va dentro del zip y se
instala junto al programa). Código fuente: https://github.com/SagerNet/sing-box

Para actualizar sing-box:
  1. Cambiar <SingBoxVersion> y <SingBoxSha256> en AppRouter.csproj
     (el hash está en la página de la release de sing-box, campo "digest" del asset).
  2. Borrar el zip viejo de esta carpeta y compilar.
  3. Comprobar que "sing-box check" acepta el config generado y probar el enrutado.
