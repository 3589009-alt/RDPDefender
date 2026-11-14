using System.Text.Json;
using RdpDefender.Configuration;
using RdpDefender.Infrastructure;

namespace RdpDefender.Ipc;

public sealed class ControlRequest
{
    public string Cmd { get; set; } = "";
    public string? Ip { get; set; }
    public int? Minutes { get; set; }
    public string? Reason { get; set; }
    public bool? Enabled { get; set; }
    public GuardConfig? Config { get; set; }
    public int? Count { get; set; }
    public int? AttemptsCount { get; set; }
}

public sealed class ControlResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public JsonElement? Data { get; set; }

    public static ControlResponse Success(object? data = null) => new()
    {
        Ok = true,
        Data = data is null ? null : JsonSerializer.SerializeToElement(data, JsonDefaults.Options)
    };

    public static ControlResponse Failure(string error) => new()
    {
        Ok = false,
        Error = error
    };

    public T? GetData<T>() =>
        Data is JsonElement element ? element.Deserialize<T>(JsonDefaults.Options) : default;
}
