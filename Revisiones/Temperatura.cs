namespace SaludNotebook.Revisiones;

static class Temperatura
{
    public record Gpu(string Nombre, int Temp, int Uso);

    public static Seccion Revisar()
    {
        var s = new Seccion { Nombre = "Temperatura", Emoji = "🌡" };

        if (LeerSistema() is { } c)
        {
            s.Metricas["sistema"] = c;
            var (e, t) = c switch
            {
                >= 90 => (Estado.Problema, "Muy caliente. Si pasa sin estar jugando o con poco uso, necesita limpieza del ventilador y cambio de pasta térmica."),
                >= 80 => (Estado.Atencion, "Bastante caliente. Normal si estás jugando; si no, conviene una limpieza."),
                _ => (Estado.Bien, "Temperatura normal."),
            };
            s.Agregar("Temperatura del sistema", $"{c:0} °C", e, t);
        }

        foreach (var g in LeerGpus())
        {
            s.Metricas["gpu"] = g.Temp;
            var (e, t) = (g.Temp, g.Uso) switch
            {
                ( >= 87, _) => (Estado.Problema, "La tarjeta gráfica está demasiado caliente. Revisa que las rejillas no estén tapadas y considera una limpieza."),
                ( >= 75, < 30) => (Estado.Atencion, "Está caliente sin estar trabajando mucho. Puede ser polvo en los ventiladores."),
                _ => (Estado.Bien, "Temperatura normal para la tarjeta gráfica."),
            };
            s.Agregar($"Tarjeta gráfica ({g.Nombre})", $"{g.Temp} °C, en uso {g.Uso}%", e, t);
        }

        if (s.Hallazgos.Count == 0)
            s.Agregar("Temperatura", "No disponible", Estado.Info,
                "Este equipo no expone sus sensores de temperatura a Windows.");
        else
            s.Agregar("Consejo", "Medición de este momento", Estado.Info,
                "La temperatura cambia según lo que estés haciendo. Para saber cómo se porta jugando, usa \"Modo juego\".");

        return s;
    }

    // Sensor ACPI de la placa (en décimas de Kelvin). Requiere administrador.
    public static double? LeerSistema()
    {
        try
        {
            var zonas = Wmi.Consultar("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\wmi")
                           .Select(z => Wmi.Numero(z, "CurrentTemperature") / 10.0 - 273.15)
                           .Where(c => c is > 0 and < 150)
                           .ToList();
            return zonas.Count > 0 ? zonas.Max() : null;
        }
        catch
        {
            return null;
        }
    }

    // Tarjetas NVIDIA, a través de nvidia-smi.
    public static List<Gpu> LeerGpus()
    {
        var lista = new List<Gpu>();
        var salida = Wmi.Ejecutar("nvidia-smi", "--query-gpu=name,temperature.gpu,utilization.gpu --format=csv,noheader,nounits", 10000);
        if (salida == null) return lista;

        foreach (var linea in salida.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = linea.Split(',').Select(x => x.Trim()).ToArray();
            if (p.Length < 3 || !int.TryParse(p[1], out var temp)) continue;
            int.TryParse(p[2], out var uso);
            lista.Add(new Gpu(p[0], temp, uso));
        }
        return lista;
    }
}
