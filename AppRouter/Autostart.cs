using System.Security;
using System.Security.Principal;

namespace AppRouter;

/// <summary>
/// Arranque de AppRouter al iniciar sesión mediante una tarea programada "con privilegios
/// más altos": así se abre en el área de notificación sin el aviso de UAC. (Una entrada en
/// "Run" no sirve para programas que piden administrador.)
/// </summary>
public static class Autostart
{
    const string TaskName = "AppRouter";

    public static bool IsEnabled()
    {
        try { Installer.Run("schtasks.exe", $"/query /tn \"{TaskName}\""); return true; }
        catch (InvalidOperationException) { return false; } // schtasks devuelve error si la tarea no existe
    }

    public static void Enable()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape(Paths.InstalledExe);
        // XML para poder desactivar "solo con corriente alterna" y el límite de 72 h que pone schtasks por defecto.
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Abre AppRouter en el área de notificación al iniciar sesión.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{exe}</Command><Arguments>--tray</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "AppRouter-task.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try { Installer.Run("schtasks.exe", $"/create /tn \"{TaskName}\" /xml \"{file}\" /f"); }
        finally { File.Delete(file); }
    }

    public static void Disable() =>
        Installer.Run("schtasks.exe", $"/delete /tn \"{TaskName}\" /f", throwOnError: false);
}
