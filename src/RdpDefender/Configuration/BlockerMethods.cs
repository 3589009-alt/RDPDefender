namespace RdpDefender.Configuration;

public static class BlockerMethods
{
    public const string Firewall = "firewall";
    public const string WinDivert = "windivert";
    public const string IpPolicy = "ippolicy";

    public static IReadOnlyList<string> All { get; } = new[] { Firewall, WinDivert, IpPolicy };

    public static string DisplayName(string? method) => method switch
    {
        Firewall => "Брандмауэр Windows",
        IpPolicy => "Политики IP (IPsec)",
        _ => "WinDivert"
    };

    public static int ToIndex(string? method) => method switch
    {
        Firewall => 0,
        IpPolicy => 2,
        _ => 1
    };

    public static string FromIndex(int index) => index switch
    {
        0 => Firewall,
        2 => IpPolicy,
        _ => WinDivert
    };
}
