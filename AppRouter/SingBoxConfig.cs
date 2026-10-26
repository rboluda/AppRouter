using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AppRouter;

/// <summary>
/// Genera la configuración de sing-box en dos partes:
///  - config.json: solo la estructura (TUN, una salida direct por adaptador conocido, reglas
///    que apuntan a rule-sets). Cambiarlo obliga a reiniciar sing-box y corta las conexiones.
///  - rule-sets locales (rules\*.json): qué programas van por cada adaptador y cuál es el
///    adaptador por defecto. sing-box los recarga solo al modificarse, sin cortar nada.
/// Validado con sing-box 1.14.3.
/// </summary>
public static class SingBoxConfig
{
    public const string TunInterfaceName = "sing-tun";
    const string TagPrefix = "direct-";
    const string AutoTag = "direct-auto";

    const string BlockedTag = "blocked";
    const string LimitTagPrefix = "lim-";

    public static string OutboundTag(string iface) => TagPrefix + iface;
    static string LimitTag(int slot) => LimitTagPrefix + slot;
    static string SlotRuleSet(int slot) => "s-" + slot;

    /// <summary>Destino (interfaz o <see cref="Routing.Auto"/>) de una salida de sing-box; null si no es nuestra.</summary>
    public static string? TargetFromTag(string tag)
    {
        if (tag == AutoTag) return Routing.Auto;
        if (tag.StartsWith(TagPrefix, StringComparison.Ordinal)) return tag[TagPrefix.Length..];
        // Programas con límite: salen por el limitador, que usa la interfaz de su hueco.
        if (tag.StartsWith(LimitTagPrefix, StringComparison.Ordinal) && int.TryParse(tag[LimitTagPrefix.Length..], out var slot))
            return LimiterMap.Read().TryGetValue(slot, out var s) ? s.Interface : null;
        return null;
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // "Conexión" legible, no ó
    };

    /// <summary>Id corto y estable por adaptador para nombres de archivo y tags (los nombres pueden llevar '*', etc.).</summary>
    static string Id(string iface) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(iface)))[..10].ToLowerInvariant();

    static string ProgramsTag(string iface) => "p-" + Id(iface);
    static string DefaultTag(string iface) => "d-" + Id(iface);
    public static string RuleSetPath(string tag) => Path.Combine(Paths.RulesDir, tag + ".json");

    /// <summary>config.json. Solo depende de los adaptadores conocidos y de opciones fijas.</summary>
    public static string BuildConfig(AppSettings settings)
    {
        var interfaces = settings.KnownInterfaces;

        var outbounds = new JsonArray();
        foreach (var iface in interfaces)
            outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = OutboundTag(iface), ["bind_interface"] = iface });
        // Respaldo si ninguna regla coincide (no debería ocurrir): sigue la ruta por defecto de Windows.
        outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = AutoTag });
        // Huecos del limitador de velocidad (proxy SOCKS5 local dentro del servicio). Usuario = hueco.
        for (var slot = 1; slot <= LimiterMap.SlotCount; slot++)
            outbounds.Add(new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = LimitTag(slot),
                ["server"] = "127.0.0.1",
                ["server_port"] = settings.LimiterPort,
                ["version"] = "5",
                ["username"] = slot.ToString(),
                ["password"] = settings.ApiSecret,
            });

        var ruleSets = new JsonArray();
        var slotSets = Enumerable.Range(1, LimiterMap.SlotCount).Select(SlotRuleSet);
        foreach (var tag in slotSets.Concat(interfaces.SelectMany(i => new[] { ProgramsTag(i), DefaultTag(i) })).Prepend(BlockedTag))
            ruleSets.Add(new JsonObject
            {
                ["type"] = "local",
                ["tag"] = tag,
                ["format"] = "source",
                ["path"] = RuleSetPath(tag).Replace('\\', '/'),
            });

        // Primero los programas sin internet, luego los programas de la lista y por último el "resto del tráfico".
        // Lo que no coincide con nada (resto del tráfico desconectado) va por "final": la ruta de Windows.
        var rules = new JsonArray
        {
            new JsonObject { ["rule_set"] = BlockedTag, ["action"] = "reject" },
        };
        // El DNS (puerto 53) de los programas no pasa por el limitador: strict_route de sing-box bloquea
        // el puerto 53 a cualquier otro proceso, y además su velocidad es insignificante.
        foreach (var iface in interfaces)
            rules.Add(new JsonObject { ["rule_set"] = ProgramsTag(iface), ["port"] = new JsonArray(53), ["action"] = "route", ["outbound"] = OutboundTag(iface) });
        for (var slot = 1; slot <= LimiterMap.SlotCount; slot++)
            rules.Add(new JsonObject { ["rule_set"] = SlotRuleSet(slot), ["action"] = "route", ["outbound"] = LimitTag(slot) });
        foreach (var iface in interfaces)
            rules.Add(new JsonObject { ["rule_set"] = ProgramsTag(iface), ["action"] = "route", ["outbound"] = OutboundTag(iface) });
        foreach (var iface in interfaces)
            rules.Add(new JsonObject { ["rule_set"] = DefaultTag(iface), ["action"] = "route", ["outbound"] = OutboundTag(iface) });

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info", ["timestamp"] = true },
            ["inbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = TunInterfaceName,
                    ["address"] = Array(["172.19.0.1/30"]),
                    ["auto_route"] = true,
                    ["strict_route"] = true,
                    ["route_exclude_address"] = Array(settings.ExcludedSubnets),
                },
            },
            ["outbounds"] = outbounds,
            ["route"] = new JsonObject
            {
                ["rule_set"] = ruleSets,
                ["rules"] = rules,
                ["final"] = AutoTag,
                ["auto_detect_interface"] = true,
                // Siempre: así la ventana de comprobación ve el programa de cada conexión.
                ["find_process"] = true,
            },
            ["experimental"] = new JsonObject
            {
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = $"127.0.0.1:{settings.ApiPort}",
                    ["secret"] = settings.ApiSecret,
                },
            },
        };
        return root.ToJsonString(JsonOpts);
    }

    /// <summary>
    /// Contenido de cada rule-set (ruta del archivo → JSON) según el destino actual de cada programa:
    /// si una interfaz está desconectada y la opción es «usar la del resto», sus programas pasan a esa.
    /// </summary>
    public static Dictionary<string, string> BuildRuleSets(AppSettings settings, ISet<string> usable)
    {
        var target = settings.Rules
            .Where(r => r.ProcessName != "")
            .Select(r => (Name: r.ProcessName, Target: Routing.Effective(settings, r, usable)))
            .ToList();
        var defaultTarget = Routing.EffectiveDefault(settings, usable);

        // Programas con límite (y una interfaz real como destino ahora mismo): cada uno a su hueco del limitador.
        // También siguen en el rule-set de su interfaz, que se usa para su DNS (ver BuildConfig).
        var slots = AssignSlots(settings, usable);

        List<string> NamesFor(string t) => target
            .Where(x => x.Target.Equals(t, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // limits.json va primero: el limitador debe conocer el hueco antes de que sing-box lo use.
        var files = new Dictionary<string, string>
        {
            [Paths.LimitsPath] = LimiterMap.Serialize(slots.ToDictionary(kv => kv.Key, kv => new LimiterMap.Slot(kv.Value.Interface, kv.Value.Mbps))),
            [RuleSetPath(BlockedTag)] = ProcessRuleSet(NamesFor(Routing.Blocked)),
        };
        for (var slot = 1; slot <= LimiterMap.SlotCount; slot++)
            files[RuleSetPath(SlotRuleSet(slot))] = ProcessRuleSet(slots.TryGetValue(slot, out var s) ? [s.Name] : []);
        foreach (var iface in settings.KnownInterfaces)
        {
            files[RuleSetPath(ProgramsTag(iface))] = ProcessRuleSet(NamesFor(iface));
            var isDefault = iface.Equals(defaultTarget, StringComparison.OrdinalIgnoreCase);
            files[RuleSetPath(DefaultTag(iface))] = RuleSet(!isDefault ? [] :
                [new JsonObject { ["ip_cidr"] = Array(["0.0.0.0/0", "::/0"]) }]);
        }
        // Programas que en este momento van por la ruta de Windows (Auto) no se listan: caen en "final".
        return files;
    }

    /// <summary>
    /// Hueco del limitador para cada programa con límite, en el orden de la lista. Si hay más de
    /// <see cref="LimiterMap.SlotCount"/>, los que sobran van sin límite (la ventana no lo permite).
    /// </summary>
    public static Dictionary<int, (string Name, string Interface, double Mbps)> AssignSlots(AppSettings settings, ISet<string> usable)
    {
        var result = new Dictionary<int, (string, string, double)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in settings.Rules.Where(r => r.LimitMbps > 0 && r.ProcessName != ""))
        {
            if (!seen.Add(r.ProcessName)) continue; // mismo nombre de proceso dos veces: vale el primero
            var t = Routing.Effective(settings, r, usable);
            if (t is Routing.Blocked or Routing.Auto) continue;
            if (result.Count == LimiterMap.SlotCount) break;
            result[result.Count + 1] = (r.ProcessName, t, r.LimitMbps);
        }
        return result;
    }

    static string ProcessRuleSet(List<string> names) =>
        RuleSet(names.Count == 0 ? [] : [new JsonObject { ["process_name"] = Array(names) }]);

    static string RuleSet(JsonNode[] rules) =>
        new JsonObject { ["version"] = 3, ["rules"] = new JsonArray(rules) }.ToJsonString(JsonOpts);

    static JsonArray Array(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
}
