using System.Globalization;
using System.Net;
using System.Xml.Linq;
using RdpDefender.Core;

namespace RdpDefender.Security;

/// <summary>Разбор XML события Security 4625 (неудачный вход в систему).</summary>
public static class EventXmlParser
{
    public static FailedLogonEvent? Parse(string xml, DateTime? timeUtc = null, long recordId = 0)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch
        {
            return null;
        }

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "Data"))
        {
            var name = element.Attribute("Name")?.Value;
            if (!string.IsNullOrEmpty(name) && !data.ContainsKey(name))
                data[name] = element.Value;
        }

        var ipText = Get(data, "IpAddress");
        if (!IpMatcher.TryParseAddress(ipText, out var address) || address is null)
            return null;
        if (IpMatcher.IsLoopback(address))
            return null;

        var eventTime = timeUtc;
        if (eventTime is null)
        {
            var timeText = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "TimeCreated")
                ?.Attribute("SystemTime")?.Value;

            if (!string.IsNullOrEmpty(timeText) &&
                DateTime.TryParse(timeText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                eventTime = parsed;
            }
            else
            {
                eventTime = DateTime.UtcNow;
            }
        }

        if (recordId <= 0)
        {
            var idText = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "EventRecordID")?.Value;
            _ = long.TryParse(idText, out recordId);
        }

        _ = int.TryParse(Get(data, "LogonType"), out var logonType);
        var user = Get(data, "TargetUserName");
        if (user == "-") user = "";

        return new FailedLogonEvent
        {
            TimeUtc = eventTime.Value,
            Ip = address.ToString(),
            UserName = user,
            LogonType = logonType,
            Workstation = Get(data, "WorkstationName"),
            Status = Get(data, "Status"),
            SubStatus = Get(data, "SubStatus"),
            RecordId = recordId
        };
    }

    private static string Get(IReadOnlyDictionary<string, string> data, string key)
        => data.TryGetValue(key, out var value) ? value?.Trim() ?? "" : "";
}
