namespace AppRouter;

// Traducciones de la interfaz: clave = texto en español (tal cual aparece en el código).
// Para añadir un idioma, ver CONTRIBUTING.md («Traducciones»).
// Comprobar que no falta ninguna: python tools/check-translations.py AppRouter --missing
public static partial class L
{
    static readonly Dictionary<string, (string En, string Zh)> Strings = new()
    {
        // --- Ajustes ---
        ["No se pudo leer {0}:\n{1}\n\nSe empezará con una lista vacía."] = ("Could not read {0}:\n{1}\n\nStarting with an empty list.", "无法读取 {0}：\n{1}\n\n将使用空列表启动。"),
        ["El archivo está vacío."] = ("The file is empty.", "文件为空。"),
        ["No es un archivo de configuración de AppRouter."] = ("This is not an AppRouter settings file.", "这不是 AppRouter 的配置文件。"),
        ["Red no válida en el archivo: {0}"] = ("Invalid network in the file: {0}", "文件中的网络无效：{0}"),

        // --- Comprobar ---
        ["Comprobar por dónde sale cada programa"] = ("Check which connection each program uses", "检查每个程序使用的网络连接"),
        ["Abre los programas de la lista y úsalos (por ejemplo, empieza una descarga). Cada conexión se comprueba dos veces: lo que dice sing-box y la IP que Windows usa de verdad."] =
            ("Open the programs in the list and use them (for example, start a download). Each connection is checked twice: what sing-box reports and the IP address Windows actually uses.",
             "打开列表中的程序并使用它们（例如开始下载）。每个连接都会检查两次：sing-box 的报告，以及 Windows 实际使用的 IP 地址。"),
        ["Verde = sale por donde debe. Rojo = no. Ámbar = abierta antes del último cambio: sigue por la conexión anterior hasta que se cierre."] =
            ("Green = using the right connection. Red = it is not. Amber = opened before the last change: it keeps using the previous connection until it closes.",
             "绿色 = 使用正确的连接。红色 = 不正确。琥珀色 = 在上次更改之前打开：在关闭之前继续使用之前的连接。"),
        ["Programa"] = ("Program", "程序"),
        ["Debería"] = ("Expected", "应使用"),
        ["Conex."] = ("Conn.", "连接数"),
        ["Sale por"] = ("Going out via", "实际使用"),
        ["Resultado"] = ("Result", "结果"),
        ["Destino"] = ("Destination", "目标"),
        ["Tipo"] = ("Type", "类型"),
        ["sing-box dice"] = ("sing-box says", "sing-box 显示"),
        ["Windows dice"] = ("Windows says", "Windows 显示"),
        ["Recibido"] = ("Received", "已接收"),
        ["Resumen por programa"] = ("Summary by program", "按程序汇总"),
        ["Conexiones activas"] = ("Active connections", "活动连接"),
        ["Solo programas de la lista"] = ("Only programs in the list", "仅显示列表中的程序"),
        ["Comprobación"] = ("Check", "检查"),
        ["Abrir registro de sing-box"] = ("Open sing-box log", "打开 sing-box 日志"),
        ["Tráfico por programa"] = ("Traffic by program", "按程序统计流量"),
        ["Enviado"] = ("Sent", "已发送"),
        ["Total"] = ("Total", "合计"),
        ["Poner a cero"] = ("Reset", "清零"),
        ["El servicio no está instalado."] = ("The service is not installed.", "服务未安装。"),
        ["No se puede hablar con sing-box (servicio: {0}). Puede que no haya arrancado: abre el registro para ver el motivo."] =
            ("Cannot reach sing-box (service: {0}). It may have failed to start: open the log to see why.",
             "无法连接 sing-box（服务：{0}）。它可能未能启动：请打开日志查看原因。"),
        ["Mover ya las conexiones antiguas ({0})"] = ("Move old connections now ({0})", "立即迁移旧连接（{0}）"),
        ["Resto de programas"] = ("All other programs", "其他所有程序"),
        ["✔ Sin internet, como se pidió"] = ("✔ No internet, as requested", "✔ 已按要求断网"),
        ["Sin conexiones (úsalo para comprobar)"] = ("No connections (use it to check)", "没有连接（请使用该程序以进行检查）"),
        ["✖ {0} de {1} salen por donde no deben"] = ("✖ {0} of {1} use the wrong connection", "✖ {1} 个中有 {0} 个使用了错误的连接"),
        ["✔ Correcto ({0} antiguas aún por {1})"] = ("✔ Correct ({0} old ones still via {1})", "✔ 正确（{0} 个旧连接仍经由 {1}）"),
        ["✔ Correcto"] = ("✔ Correct", "✔ 正确"),
        ["{0} (desconectada)"] = ("{0} (disconnected)", "{0}（已断开）"),
        ["• {0}: {1} por {2} → pasarán a {3}"] = ("• {0}: {1} via {2} → will move to {3}", "• {0}：{1} 个经由 {2} → 将改为 {3}"),
        ["Se cerrarán estas conexiones antiguas para que el programa las vuelva a abrir por su conexión actual:"] =
            ("These old connections will be closed so the program reopens them through its current connection:",
             "将关闭以下旧连接，以便程序通过其当前连接重新建立："),
        ["Si alguna es una descarga, se interrumpirá un momento (normalmente el programa la reanuda solo). Las demás conexiones no se tocan."] =
            ("If any of them is a download, it will be interrupted briefly (programs usually resume on their own). Other connections are not affected.",
             "如果其中有下载，会短暂中断（程序通常会自动恢复）。其他连接不受影响。"),
        ["¿Continuar?"] = ("Continue?", "是否继续？"),
        ["No se pudieron cerrar {0} de {1} conexiones (puede que ya se hubieran cerrado)."] =
            ("Could not close {0} of {1} connections (they may have closed already).", "{1} 个连接中有 {0} 个无法关闭（可能已经关闭）。"),
        ["Desde las {0:HH:mm} (mientras AppRouter está abierto)"] = ("Since {0:HH:mm} (while AppRouter is open)", "自 {0:HH:mm} 起（AppRouter 运行期间）"),
        ["Tráfico por adaptador"] = ("Traffic by adapter", "按网卡统计流量"),
        ["Midiendo tráfico…"] = ("Measuring traffic…", "正在测量流量…"),
        ["Todavía no hay registro."] = ("There is no log yet.", "暂无日志。"),

        // --- Instalación ---
        ["Cancelar"] = ("Cancel", "取消"),
        ["Actualizar"] = ("Update", "更新"),
        ["Instalar"] = ("Install", "安装"),
        ["Ya está instalado en este equipo."] = ("It is already installed on this computer.", "此电脑上已安装。"),
        ["Pulsa «Actualizar» para pasar a la versión {0}. Tu lista de programas se conserva."] =
            ("Click “Update” to move to version {0}. Your program list is kept.", "点击“更新”升级到 {0} 版本。你的程序列表会保留。"),
        ["Durante la actualización se cortarán un momento las conexiones abiertas (por ejemplo, descargas en curso)."] =
            ("During the update, open connections (for example, running downloads) will be interrupted briefly.", "更新期间，已打开的连接（例如正在进行的下载）会短暂中断。"),
        ["AppRouter permite elegir por qué conexión de red (cable, Wi-Fi…) sale a internet cada programa."] =
            ("AppRouter lets you choose which network connection (cable, Wi-Fi…) each program uses to reach the internet.",
             "AppRouter 可以为每个程序选择访问互联网所用的网络连接（有线、Wi-Fi…）。"),
        ["Se instalará en «Archivos de programa», con acceso directo en el escritorio y en el menú Inicio, y se abrirá solo al iniciar sesión (junto al reloj). Mientras AppRouter esté abierto, cada programa sale por la conexión que elijas; al salir de AppRouter todo vuelve a la normalidad."] =
            ("It will be installed in “Program Files”, with shortcuts on the desktop and in the Start menu, and it will open automatically when you sign in (next to the clock). While AppRouter is open, each program uses the connection you choose; when you exit AppRouter everything goes back to normal.",
             "它将安装到“Program Files”，并在桌面和开始菜单中创建快捷方式，登录时会自动打开（位于时钟旁边）。AppRouter 运行时，每个程序使用你选择的连接；退出 AppRouter 后一切恢复正常。"),
        ["Sustituye a la versión anterior («Enrutado por programa») y conserva su lista de programas."] =
            ("It replaces the previous version (“Enrutado por programa”) and keeps its program list.", "它将替换旧版本（“Enrutado por programa”）并保留其程序列表。"),
        ["Incluye sing-box {0} (licencia GPLv3)."] = ("Includes sing-box {0} (GPLv3 license).", "包含 sing-box {0}（GPLv3 许可证）。"),
        ["Instalación terminada."] = ("Installation complete.", "安装完成。"),
        ["Ahora se abrirá AppRouter: añade los programas y elige su conexión. Al cerrar la ventana seguirá funcionando junto al reloj."] =
            ("AppRouter will now open: add your programs and choose their connection. When you close the window it keeps running next to the clock.",
             "现在将打开 AppRouter：添加程序并选择其连接。关闭窗口后，它会在时钟旁继续运行。"),
        ["La instalación no se ha completado."] = ("The installation did not complete.", "安装未完成。"),
        ["Cerrando versiones anteriores…"] = ("Closing previous versions…", "正在关闭旧版本…"),
        ["Copiando archivos…"] = ("Copying files…", "正在复制文件…"),
        ["Instalando sing-box {0}…"] = ("Installing sing-box {0}…", "正在安装 sing-box {0}…"),
        ["Preparando la configuración…"] = ("Preparing the configuration…", "正在准备配置…"),
        ["No se ha encontrado ningún adaptador de red."] = ("No network adapter was found.", "未找到任何网卡。"),
        ["Configuración inicial no válida:"] = ("Invalid initial configuration:", "初始配置无效："),
        ["Registrando el servicio…"] = ("Registering the service…", "正在注册服务…"),
        ["Creando accesos directos…"] = ("Creating shortcuts…", "正在创建快捷方式…"),
        ["Este ejecutable no incluye sing-box."] = ("This executable does not include sing-box.", "此可执行文件不包含 sing-box。"),
        ["{0} {1} falló ({2}):"] = ("{0} {1} failed ({2}):", "{0} {1} 失败（{2}）："),
        ["¿Desinstalar AppRouter?"] = ("Uninstall AppRouter?", "要卸载 AppRouter 吗？"),
        ["Todo el tráfico volverá a salir por la conexión normal de Windows."] = ("All traffic will go back to using Windows' normal connection.", "所有流量将恢复使用 Windows 的默认连接。"),
        ["AppRouter se ha desinstalado."] = ("AppRouter has been uninstalled.", "AppRouter 已卸载。"),

        // --- Ventana principal ---
        ["NOMBRE"] = ("NAME", "名称"),
        ["INTERFAZ"] = ("INTERFACE", "网络接口"),
        ["Volver a leer los adaptadores de red"] = ("Reload network adapters", "重新读取网卡"),
        ["Resto del tráfico:"] = ("All other traffic:", "其他流量："),
        ["Guardar y aplicar"] = ("Save and apply", "保存并应用"),
        ["Comprobar…"] = ("Check…", "检查…"),
        ["Ver en directo por qué conexión sale cada programa"] = ("See live which connection each program uses", "实时查看每个程序使用的连接"),
        ["&Archivo"] = ("&File", "文件(&F)"),
        ["Exportar configuración…"] = ("Export settings…", "导出配置…"),
        ["Importar configuración…"] = ("Import settings…", "导入配置…"),
        ["Cerrar ventana (sigue funcionando)"] = ("Close window (keeps running)", "关闭窗口（继续运行）"),
        ["Salir y desactivar el enrutado"] = ("Exit and turn off routing", "退出并关闭分流"),
        ["&Herramientas"] = ("&Tools", "工具(&T)"),
        ["Configuración…"] = ("Settings…", "设置…"),
        ["A&yuda"] = ("&Help", "帮助(&H)"),
        ["Buscar actualizaciones"] = ("Check for updates", "检查更新"),
        ["Página del proyecto en GitHub"] = ("Project page on GitHub", "GitHub 项目主页"),
        ["Acerca de AppRouter"] = ("About AppRouter", "关于 AppRouter"),
        ["Elige por qué conexión de red sale cada programa."] = ("Choose which network connection each program uses.", "为每个程序选择所用的网络连接。"),
        ["Incluye sing-box {0} (GPLv3)."] = ("Includes sing-box {0} (GPLv3).", "包含 sing-box {0}（GPLv3）。"),
        ["Software libre bajo la licencia GPLv3."] = ("Free software under the GPLv3 license.", "基于 GPLv3 许可证的自由软件。"),
        ["Abrir AppRouter"] = ("Open AppRouter", "打开 AppRouter"),
        ["Sigue funcionando aquí. Para desactivarlo: clic derecho en este icono → Salir."] =
            ("It keeps running here. To turn it off: right-click this icon → Exit.", "它仍在此处运行。要关闭：右键单击此图标 → 退出。"),
        ["Hay cambios sin aplicar que se perderán. ¿Salir igualmente?"] = ("There are unapplied changes that will be lost. Exit anyway?", "有尚未应用的更改将会丢失。仍要退出吗？"),
        ["la conexión por defecto"] = ("the default connection", "默认连接"),
        ["Al salir se desactiva el enrutado por programa."] = ("Exiting turns off per-program routing.", "退出后将关闭按程序分流。"),
        ["Todas las conexiones volverán a salir por la conexión por defecto de Windows: {0}."] =
            ("All connections will go back to Windows' default connection: {0}.", "所有连接将恢复使用 Windows 的默认连接：{0}。"),
        ["Las conexiones abiertas ahora mismo (por ejemplo, descargas en curso) se cortarán un momento."] =
            ("Connections open right now (for example, running downloads) will be interrupted briefly.", "当前已打开的连接（例如正在进行的下载）会短暂中断。"),
        ["¿Salir?"] = ("Exit?", "要退出吗？"),
        ["No se pudo detener el servicio:"] = ("Could not stop the service:", "无法停止服务："),
        ["Hay cambios sin aplicar que se perderán. ¿Reiniciar igualmente?"] = ("There are unapplied changes that will be lost. Restart anyway?", "有尚未应用的更改将会丢失。仍要重启吗？"),
        ["Activando el enrutado…"] = ("Turning on routing…", "正在开启分流…"),
        ["No se pudo activar el enrutado."] = ("Could not turn on routing.", "无法开启分流。"),
        ["No se pudo activar el enrutado:"] = ("Could not turn on routing:", "无法开启分流："),
        ["Error al actualizar las reglas:"] = ("Error updating the rules:", "更新规则时出错："),
        ["{0} se ha desconectado."] = ("{0} has disconnected.", "{0} 已断开。"),
        ["{0} sale por {1} mientras tanto."] = ("{0} uses {1} in the meantime.", "{0} 暂时改用 {1}。"),
        ["{0} se queda sin internet hasta que vuelva."] = ("{0} has no internet until it comes back.", "在其恢复之前，{0} 将无法上网。"),
        ["{0} vuelve a estar conectada."] = ("{0} is connected again.", "{0} 已重新连接。"),
        ["{0} vuelve a salir por ella."] = ("{0} uses it again.", "{0} 已恢复使用它。"),
        ["Hay una versión nueva de AppRouter: {0} (tienes la {1})."] = ("A new version of AppRouter is available: {0} (you have {1}).", "AppRouter 有新版本：{0}（当前版本 {1}）。"),
        ["Haz clic aquí para descargarla."] = ("Click here to download it.", "点击此处下载。"),
        ["Tienes la última versión ({0})."] = ("You have the latest version ({0}).", "已是最新版本（{0}）。"),
        ["(desconectada)"] = ("(disconnected)", "（已断开）"),
        ["Quitar"] = ("Remove", "移除"),
        ["+ Añadir programa"] = ("+ Add program", "+ 添加程序"),
        ["Interfaz para el programa que se añada"] = ("Interface for the program you add", "要添加的程序所用的网络接口"),
        ["Elige la conexión para el resto del tráfico."] = ("Choose the connection for all other traffic.", "请为其他流量选择连接。"),
        ["resto del tráfico"] = ("all other traffic", "其他流量"),
        ["Estas conexiones no existen ahora mismo en el equipo:"] = ("These connections do not exist on this computer right now:", "这些连接目前在此电脑上不存在："),
        ["¿Guardar igualmente?"] = ("Save anyway?", "仍要保存吗？"),
        ["Hay conexiones de red nuevas en el equipo:"] = ("There are new network connections on this computer:", "此电脑上有新的网络连接："),
        ["Para aplicar este cambio hay que reiniciar el servicio, y se cortarán un momento todas las conexiones abiertas (por ejemplo, las descargas en curso, que se reanudarán solas si el programa lo permite)."] =
            ("Applying this change requires restarting the service, which will briefly interrupt all open connections (for example, running downloads, which will resume on their own if the program supports it).",
             "应用此更改需要重启服务，所有已打开的连接都会短暂中断（例如正在进行的下载，如果程序支持会自动恢复）。"),
        ["Aplicando…"] = ("Applying…", "正在应用…"),
        ["La configuración no es válida; no se ha aplicado."] = ("The configuration is not valid; it was not applied.", "配置无效，未应用。"),
        ["sing-box ha rechazado la configuración:"] = ("sing-box rejected the configuration:", "sing-box 拒绝了该配置："),
        ["Reiniciando el servicio…"] = ("Restarting the service…", "正在重启服务…"),
        ["sing-box no está funcionando."] = ("sing-box is not running.", "sing-box 未在运行。"),
        ["Se guardó la configuración, pero sing-box no está funcionando."] = ("The configuration was saved, but sing-box is not running.", "配置已保存，但 sing-box 未在运行。"),
        ["Últimas líneas del registro:"] = ("Last lines of the log:", "日志最后几行："),
        ["✔ Aplicado a las {0:HH:mm:ss} (servicio reiniciado). Funcionando."] = ("✔ Applied at {0:HH:mm:ss} (service restarted). Running.", "✔ 已于 {0:HH:mm:ss} 应用（服务已重启）。运行中。"),
        ["✔ Aplicado a las {0:HH:mm:ss} sin cortar conexiones. Las ya abiertas siguen por donde iban."] =
            ("✔ Applied at {0:HH:mm:ss} without interrupting connections. Already open ones keep their current route.", "✔ 已于 {0:HH:mm:ss} 应用，未中断连接。已打开的连接保持原路线。"),
        ["activo"] = ("on", "已开启"),
        ["Error al aplicar."] = ("Error while applying.", "应用时出错。"),
        ["Hay cambios sin aplicar. Para comprobarlos hay que guardarlos primero."] = ("There are unapplied changes. They must be saved before checking them.", "有尚未应用的更改。检查前需要先保存。"),
        ["¿Guardar y aplicar ahora?"] = ("Save and apply now?", "现在保存并应用吗？"),
        ["Ajustes guardados; se aplicarán en el próximo «Guardar y aplicar»."] = ("Settings saved; they will be applied on the next “Save and apply”.", "设置已保存，将在下次“保存并应用”时生效。"),
        ["El idioma cambiará al reiniciar AppRouter. El enrutado no se interrumpe."] = ("The language will change when AppRouter restarts. Routing is not interrupted.", "语言将在 AppRouter 重启后更改。分流不会中断。"),
        ["¿Reiniciar ahora?"] = ("Restart now?", "现在重启吗？"),
        ["Exportar configuración"] = ("Export settings", "导出配置"),
        ["Configuración de AppRouter (*.json)|*.json"] = ("AppRouter settings (*.json)|*.json", "AppRouter 配置 (*.json)|*.json"),
        ["Configuración exportada a {0}"] = ("Settings exported to {0}", "配置已导出到 {0}"),
        ["Importar configuración"] = ("Import settings", "导入配置"),
        ["Se sustituirán la lista de programas y los ajustes por los del archivo. ¿Continuar?"] = ("The program list and settings will be replaced with those in the file. Continue?", "程序列表和设置将被文件中的内容替换。是否继续？"),
        ["Configuración importada. Revísala y pulsa «Guardar y aplicar»."] = ("Settings imported. Review them and click “Save and apply”.", "配置已导入。请检查后点击“保存并应用”。"),
        ["No se pudo importar el archivo:"] = ("Could not import the file:", "无法导入文件："),
        ["● Enrutado activo."] = ("● Routing is on.", "● 分流已开启。"),
        ["Enrutado desactivado (servicio: {0}). Pulsa «Guardar y aplicar» para activarlo."] = ("Routing is off (service: {0}). Click “Save and apply” to turn it on.", "分流已关闭（服务：{0}）。点击“保存并应用”以开启。"),
        ["desactivado"] = ("off", "已关闭"),
        ["(sin registro)"] = ("(no log)", "（无日志）"),

        // --- Añadir programas ---
        ["Añadir programas"] = ("Add programs", "添加程序"),
        ["Ubicación"] = ("Location", "位置"),
        ["Con ventana abierta"] = ("With an open window", "有打开的窗口"),
        ["En segundo plano"] = ("In the background", "后台运行"),
        ["Marca los programas que quieres añadir. Si no está abierto, ábrelo antes o búscalo con «Examinar…»."] =
            ("Tick the programs you want to add. If it is not running, open it first or find it with “Browse…”.", "勾选要添加的程序。如果程序未运行，请先打开它，或使用“浏览…”查找。"),
        ["Buscar…"] = ("Search…", "搜索…"),
        ["Mostrar procesos de Windows"] = ("Show Windows processes", "显示 Windows 进程"),
        ["Añadir"] = ("Add", "添加"),
        ["Examinar…"] = ("Browse…", "浏览…"),
        ["Elegir programa"] = ("Choose program", "选择程序"),
        ["Programas (*.exe)|*.exe"] = ("Programs (*.exe)|*.exe", "程序 (*.exe)|*.exe"),
        ["(ya en la lista)"] = ("(already in the list)", "（已在列表中）"),

        // --- Destinos y tipos de conexión ---
        ["Sin internet"] = ("No internet", "禁止上网"),
        ["Automática (Windows)"] = ("Automatic (Windows)", "自动（Windows）"),
        ["Cable (Ethernet)"] = ("Cable (Ethernet)", "有线（以太网）"),
        ["Móvil (4G/5G)"] = ("Mobile (4G/5G)", "移动网络（4G/5G）"),
        ["Virtual / VPN"] = ("Virtual / VPN", "虚拟 / VPN"),
        ["Otra"] = ("Other", "其他"),

        // --- Configuración ---
        ["Configuración"] = ("Settings", "设置"),
        ["Aceptar"] = ("OK", "确定"),
        ["General"] = ("General", "常规"),
        ["Automático (idioma de Windows)"] = ("Automatic (Windows language)", "自动（Windows 语言）"),
        ["Idioma"] = ("Language", "语言"),
        ["El cambio de idioma se aplica al reiniciar AppRouter (te lo ofrecerá al aceptar)."] =
            ("The language change takes effect when AppRouter restarts (you will be offered to do so when you click OK).", "语言更改将在 AppRouter 重启后生效（点击确定后会提示你重启）。"),
        ["Funcionamiento"] = ("Behavior", "运行方式"),
        ["Abrir AppRouter al iniciar sesión en Windows"] = ("Open AppRouter when signing in to Windows", "登录 Windows 时打开 AppRouter"),
        ["Avisarme cuando se desconecte o vuelva una conexión que uso"] = ("Notify me when a connection I use disconnects or comes back", "当我使用的连接断开或恢复时通知我"),
        ["Buscar actualizaciones automáticamente (consulta GitHub una vez al día)"] = ("Check for updates automatically (queries GitHub once a day)", "自动检查更新（每天访问一次 GitHub）"),
        ["Copia de la configuración"] = ("Settings backup", "配置备份"),
        ["Exportar…"] = ("Export…", "导出…"),
        ["Importar…"] = ("Import…", "导入…"),
        ["Guarda en un archivo la lista de programas y estos ajustes para llevarlos a otro equipo."] =
            ("Saves the program list and these settings to a file so you can take them to another computer.", "将程序列表和这些设置保存到文件，以便在其他电脑上使用。"),
        ["Abrir la carpeta de configuración y registros"] = ("Open the settings and logs folder", "打开配置和日志文件夹"),
        ["Interfaces"] = ("Interfaces", "网络接口"),
        ["Marca las conexiones que quieres ver en los desplegables. Si no marcas ninguna, se muestran todas."] =
            ("Tick the connections you want to see in the drop-down lists. If you tick none, all of them are shown.", "勾选要在下拉列表中显示的连接。如果都不勾选，则显示全部。"),
        ["Conexión"] = ("Connection", "连接"),
        ["Adaptador"] = ("Adapter", "网卡"),
        ["Conectadas"] = ("Connected", "已连接"),
        ["Desconectadas"] = ("Disconnected", "未连接"),
        ["Marcar todas"] = ("Select all", "全选"),
        ["Ninguna"] = ("None", "全不选"),
        ["Solo físicas"] = ("Physical only", "仅物理网卡"),
        ["Enrutado"] = ("Routing", "分流"),
        ["Si se desconecta la conexión de un programa"] = ("If a program's connection disconnects", "当程序所用的连接断开时"),
        ["Dejarlo sin internet hasta que vuelva (nunca sale por otra conexión)"] = ("Leave it without internet until it comes back (never use another connection)", "在其恢复前禁止上网（绝不改用其他连接）"),
        ["Usar mientras tanto la conexión del resto del tráfico"] = ("Use the connection of all other traffic in the meantime", "暂时改用其他流量所用的连接"),
        ["Con la segunda opción, si se desconecta también la del resto del tráfico, se usa la que Windows tenga disponible. El cambio es automático y no corta las demás conexiones."] =
            ("With the second option, if the connection for all other traffic also disconnects, whatever Windows has available is used. The switch is automatic and does not interrupt other connections.",
             "选择第二项时，如果其他流量所用的连接也断开，将使用 Windows 当前可用的连接。切换是自动的，不会中断其他连接。"),
        ["Redes que no pasan por AppRouter"] = ("Networks that bypass AppRouter", "不经过 AppRouter 的网络"),
        ["El tráfico hacia estas redes (por ejemplo, la red local de la oficina) sigue siempre el camino normal de Windows. Cambiarlas reinicia el servicio y corta un momento las conexiones."] =
            ("Traffic to these networks (for example, the office local network) always follows Windows' normal route. Changing them restarts the service and briefly interrupts connections.",
             "发往这些网络（例如办公室局域网）的流量始终走 Windows 的默认路线。修改它们会重启服务，并短暂中断连接。"),
        ["p. ej. 192.168.1.0/24"] = ("e.g. 192.168.1.0/24", "例如 192.168.1.0/24"),
        ["«{0}» no es una red válida. Usa el formato dirección/prefijo, por ejemplo 192.168.1.0/24."] =
            ("“{0}” is not a valid network. Use the address/prefix format, for example 192.168.1.0/24.", "“{0}”不是有效的网络。请使用“地址/前缀”格式，例如 192.168.1.0/24。"),

        // --- Límite de velocidad ---
        ["LÍMITE"] = ("LIMIT", "限速"),
        ["Límite"] = ("Limit", "限速"),
        ["Ilimitado"] = ("Unlimited", "不限速"),
        ["Velocidad máxima de este programa, en megabits por segundo (bajada y subida). Puedes escribir cualquier valor."] =
            ("Maximum speed for this program, in megabits per second (download and upload). You can type any value.",
             "此程序的最高速度，单位为兆比特每秒（下载和上传）。可以输入任意数值。"),
        ["Como máximo puede haber {0} programas con límite de velocidad (ahora hay {1})."] =
            ("At most {0} programs can have a speed limit (there are {1} now).", "最多只能为 {0} 个程序设置限速（当前有 {1} 个）。"),

        // --- Actualización automática ---
        ["AppRouter se ha actualizado a la versión {0}."] = ("AppRouter has been updated to version {0}.", "AppRouter 已更新到 {0} 版本。"),
        ["¿Actualizar ahora? Se descarga, se comprueba y AppRouter se reinicia solo; las conexiones abiertas se cortarán unos segundos."] =
            ("Update now? It is downloaded, verified and AppRouter restarts by itself; open connections will be interrupted for a few seconds.",
             "现在更新吗？将下载并校验，然后 AppRouter 自动重启；已打开的连接会中断几秒钟。"),
        ["Descargando la actualización {0}… {1} %"] = ("Downloading update {0}… {1} %", "正在下载更新 {0}… {1}%"),
        ["Se ha descargado AppRouter {0}. Se instalará solo cuando no haya descargas en curso (las conexiones se cortan unos segundos)."] =
            ("AppRouter {0} has been downloaded. It will install itself when no downloads are running (connections are interrupted for a few seconds).",
             "已下载 AppRouter {0}。将在没有正在进行的下载时自动安装（连接会中断几秒钟）。"),
        ["No se pudo actualizar:"] = ("Could not update:", "无法更新："),
        ["No se pudo instalar la actualización de AppRouter:"] = ("Could not install the AppRouter update:", "无法安装 AppRouter 更新："),
        ["Detalles en {0}"] = ("Details in {0}", "详细信息见 {0}"),
        ["Instalarlas solo (cuando no haya descargas en curso o al iniciar AppRouter)"] =
            ("Install them automatically (when no downloads are running, or when AppRouter starts)", "自动安装（在没有正在进行的下载时，或 AppRouter 启动时）"),
        ["La versión publicada no incluye el instalador o su suma de comprobación."] =
            ("The published release does not include the installer or its checksum.", "发布的版本中缺少安装程序或其校验和。"),
        ["SHA256SUMS.txt no contiene el instalador."] = ("SHA256SUMS.txt does not list the installer.", "SHA256SUMS.txt 中没有该安装程序。"),
        ["El archivo descargado no coincide con su suma de comprobación (SHA-256). No se ha instalado nada."] =
            ("The downloaded file does not match its checksum (SHA-256). Nothing was installed.", "下载的文件与其校验和（SHA-256）不符。未安装任何内容。"),
        ["El archivo descargado no es AppRouter {0}. No se ha instalado nada."] =
            ("The downloaded file is not AppRouter {0}. Nothing was installed.", "下载的文件不是 AppRouter {0}。未安装任何内容。"),

        // --- Varios ---
        ["Envía el tráfico de cada programa por el adaptador de red elegido en AppRouter (sing-box {0})."] =
            ("Sends each program's traffic through the network adapter chosen in AppRouter (sing-box {0}).", "按 AppRouter 中的设置，将每个程序的流量发送到所选网卡（sing-box {0}）。"),
        ["sing-box check no respondió."] = ("sing-box check did not respond.", "sing-box check 无响应。"),
        ["(desconocido)"] = ("(unknown)", "（未知）"),
    };
}
