# Cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versiones según [SemVer](https://semver.org/lang/es/).

## [1.2.0] - 2026-10-16

### Añadido
- Barra de menú (*Archivo*, *Herramientas*, *Ayuda*) y ventana **Configuración** con pestañas General, Interfaces y Enrutado.
- Interfaz en **inglés y chino simplificado**, además de español (o el idioma de Windows). El cambio se aplica reiniciando AppRouter sin cortar el enrutado.
- **Interfaces preferidas**: elegir qué conexiones aparecen en los desplegables (con su tipo, estado e IP).
- Opción **«Sin internet»** por programa (sing-box rechaza sus conexiones).
- **Si se desconecta la conexión de un programa**: dejarlo sin internet hasta que vuelva, o usar mientras tanto la del resto del tráfico (y, si también cae, la ruta de Windows). El cambio es automático y sin cortar lo demás.
- **Redes excluidas editables** (antes fija en `10.0.0.0/8`).
- **Añadir desde los programas abiertos**, con su icono, buscador y opción de examinar.
- **Avisos** junto al reloj cuando una conexión usada se desconecta o vuelve.
- **Exportar/importar** la lista de programas y los ajustes a un archivo.
- **Buscar actualizaciones** (manual en *Ayuda*, o automática una vez al día si se activa; desactivada por defecto).
- Pestaña **Tráfico por programa** en «Comprobar…»: enviado/recibido por programa y conexión desde que se abrió AppRouter.
- Los desplegables marcan las conexiones desconectadas.

### Cambiado
- El generador de `config.json` añade el rule-set de programas sin internet: al actualizar desde 1.1 el servicio se reinicia una vez.

## [1.1.0] - 2026-10-16

Primera versión pública, con el nombre **AppRouter**.

### Añadido
- Icono junto al reloj: cerrar la ventana solo la oculta; «Salir y desactivar el enrutado» detiene el servicio y avisa de por qué conexión volverá a salir todo.
- Se abre solo al iniciar sesión (tarea programada, sin aviso de UAC). Se puede desactivar desde el menú del icono.
- Una sola copia abierta: abrir el acceso directo muestra la ventana existente.
- «Comprobar…»: marca en ámbar las conexiones abiertas antes del último cambio y botón «Mover ya las conexiones antiguas».
- Icono propio, número de versión y compilación automática con GitHub Actions.
- sing-box se descarga al compilar y se verifica su SHA-256.

### Cambiado
- «Guardar y aplicar» ya no reinicia sing-box: la lista de programas y la conexión por defecto van en rule-sets que sing-box recarga en caliente, sin cortar las conexiones abiertas. Solo un adaptador de red nuevo obliga a reiniciar, y la app avisa antes.
- El servicio pasa a inicio manual: el enrutado solo está activo mientras AppRouter está abierto.
- Nombre del servicio, carpetas y accesos directos: `AppRouter`. El instalador migra la versión anterior («Enrutado por programa») y conserva su lista de programas.

## [1.0.0] - 2026-10-16

Versión interna («Enrutado por programa» / `SingBoxRouter`).

- Lista de programas con su interfaz de red; genera el config de sing-box (TUN, salida `direct` con `bind_interface` por adaptador, reglas `process_name`, 10.0.0.0/8 fuera del túnel).
- Instalador en un único `.exe` (incluye .NET y sing-box 1.14.3) y servicio de Windows propio.
- Ventana «Comprobar…»: por dónde sale cada conexión según sing-box y según Windows.

[1.2.0]: https://github.com/rboluda/approuter/releases/tag/v1.2.0
[1.1.0]: https://github.com/rboluda/approuter/releases/tag/v1.1.0
