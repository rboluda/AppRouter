using System.Reflection;

namespace AppRouter;

public static class AppIcon
{
    /// <summary>Icono de la app al tamaño pedido (por defecto, el del área de notificación).</summary>
    public static Icon Load(Size? size = null)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppRouter.ico")!;
        return new Icon(s, size ?? SystemInformation.SmallIconSize);
    }
}
