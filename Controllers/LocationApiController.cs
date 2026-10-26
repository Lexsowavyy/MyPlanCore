using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace PlanCore.Web.Controllers
{
    // Server-side proxy to OpenStreetMap Nominatim search so the browser never
    // hits OSM directly (keeps the User-Agent header correct, respects OSM usage
    // policy, and lets us reshape the payload for the autocomplete UI).
    //
    // The official OSM API is keyless. If you use an OSM-based provider that
    // requires a key (e.g. LocationIQ, Geoapify), set LOCATION_API_URL and
    // LOCATION_API_KEY in .env and it will be sent automatically.
    [ApiController]
    [Route("api/locations")]
    public class LocationApiController : ControllerBase
    {
        private static readonly HttpClient _http = new(new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        static LocationApiController()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("PlanCorePMS/1.0 (https://plancore.com; contact: admin@plancore.com)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        }

        private static string ApiUrl =>
            DotNetEnv.Env.GetString("LOCATION_API_URL", "https://nominatim.openstreetmap.org/search");

        private static string ApiKey =>
            DotNetEnv.Env.GetString("LOCATION_API_KEY", "");

        public class LocationSuggestion
        {
            public string Label { get; set; } = "";
            public string FullName { get; set; } = "";
            public double? Lat { get; set; }
            public double? Lon { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Suggest([FromQuery] string q, [FromQuery] int? limit = 8)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Ok(Array.Empty<LocationSuggestion>());

            var count = Math.Clamp(limit ?? 8, 1, 20);
            var query = Uri.EscapeDataString(q.Trim());
            var sb = new System.Text.StringBuilder(ApiUrl);
            sb.Append(ApiUrl.Contains('?') ? '&' : '?');
            sb.Append("q=").Append(query);
            sb.Append("&format=jsonv2&addressdetails=1&limit=").Append(count);
            if (!string.IsNullOrEmpty(ApiKey))
                sb.Append("&key=").Append(Uri.EscapeDataString(ApiKey));
            var osmUrl = sb.ToString();

            try
            {
                using var resp = await _http.GetAsync(osmUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                    return Ok(Array.Empty<LocationSuggestion>());

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var results = new List<LocationSuggestion>();
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var label = BuildLabel(item);
                        var full = item.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(full)) continue;
                        if (string.IsNullOrEmpty(label)) label = full;

                        double? lat = item.TryGetProperty("lat", out var latEl) && double.TryParse(latEl.GetString(), out var latVal) ? latVal : null;
                        double? lon = item.TryGetProperty("lon", out var lonEl) && double.TryParse(lonEl.GetString(), out var lonVal) ? lonVal : null;

                        results.Add(new LocationSuggestion
                        {
                            Label = label,
                            FullName = full,
                            Lat = lat,
                            Lon = lon
                        });
                    }
                }

                return Ok(results);
            }
            catch
            {
                return Ok(Array.Empty<LocationSuggestion>());
            }
        }

        private static string BuildLabel(JsonElement item)
        {
            if (!item.TryGetProperty("address", out var addr) || addr.ValueKind != JsonValueKind.Object)
                return "";

            string? Pick(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (addr.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
                return null;
            }

            var city = Pick("city", "town", "village", "municipality", "county");
            var region = Pick("state", "state_district", "province");
            var country = Pick("country");

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(city)) parts.Add(city);
            if (!string.IsNullOrEmpty(region) && !string.Equals(region, city, StringComparison.OrdinalIgnoreCase)) parts.Add(region);
            if (!string.IsNullOrEmpty(country) && !string.Equals(country, region, StringComparison.OrdinalIgnoreCase)) parts.Add(country);

            return string.Join(", ", parts);
        }
    }
}