namespace SaludNotebook.Revisiones;

static class Temperatura
{
    public static Seccion Revisar()
    {
        var s = new Seccion { Nombre = "Temperatura", Emoji = "🌡" };

        // Sensor ACPI de la placa (en décimas de Kelvin). Requiere administrador.
        try
        {
            var zonas = Wmi.Consultar("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\wmi")
                           .Select(z => Wmi.Numero(z, "CurrentTemperature") / 10.0 - 273.15)
                           .Where(c => c is > 0 and < 150)
                           .ToList();
            if (zonas.Count > 0)
            {
                var c = zonas.Max();
                var (e, t) = c switch
                {
                    >= 90 => (Estado.Problema, "Muy caliente. Si pasa sin estar jugando o con poco uso, necesita limpieza del ventilador y cambio de pasta térmica."),
                    >= 80 => (Estado.Atencion, "Bastante caliente. Normal si estás jugando; si no, conviene una limpieza."),
                    _ => (Estado.Bien, "Temperatura normal."),
                };
                s.Agregar("Temperatura del sistema", $"{c:0} °C", e, t);
            }
        }
        catch
        {
        }

        // Tarjeta gráfica NVIDIA, si está.
        var nvidia = Wmi.Ejecutar("nvidia-smi", "--query-gpu=name,temperature.gpu,utilization.gpu --format=csv,noheader,nounits", 10000);
        if (nvidia != null)
        {
            foreach (var linea in nvidia.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = linea.Split(',').Select(x => x.Trim()).ToArray();
                if (p.Length < 3 || !int.TryParse(p[1], out var c)) continue;
                int.TryParse(p[2], out var uso);
                var (e, t) = (c, uso) switch
                {
                    ( >= 87, _) => (Estado.Problema, "La tarjeta gráfica está demasiado caliente. Revisa que las rejillas no estén tapadas y considera una limpieza."),
                    ( >= 75, < 30) => (Estado.Atencion, "Está caliente sin estar trabajando mucho. Puede ser polvo en los ventiladores."),
                    _ => (Estado.Bien, "Temperatura normal para la tarjeta gráfica."),
                };
                s.Agregar($"Tarjeta gráfica ({p[0]})", $"{c} °C, en uso {uso}%", e, t);
            }
        }

        if (s.Hallazgos.Count == 0)
            s.Agregar("Temperatura", "No disponible", Estado.Info,
                "Este equipo no expone sus sensores de temperatura a Windows.");
        else
            s.Agregar("Consejo", "Medición de este momento", Estado.Info,
                "La temperatura cambia según lo que estés haciendo. Lo importante es que no esté muy caliente sin estar usando programas pesados.");

        return s;
    }
}
