using System.Reflection;

namespace RdpDefender.Ui;

/// <summary>Иконка приложения из встроенного ресурса.</summary>
public static class AppIcon
{
    private const string ResourceName = "RdpDefender.Assets.AppIcon.ico";
    private static readonly Lazy<Icon> Master = new(LoadMaster, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Icon Get(int size)
    {
        try
        {
            return new Icon(Master.Value, new Size(size, size));
        }
        catch
        {
            return SystemIcons.Shield;
        }
    }

    public static Bitmap GetBitmap(int size)
    {
        using var icon = Get(size);
        return icon.ToBitmap();
    }

    private static Icon LoadMaster()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            return SystemIcons.Shield;

        return new Icon(stream);
    }
}
