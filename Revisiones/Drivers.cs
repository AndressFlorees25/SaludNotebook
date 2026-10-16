namespace SaludNotebook.Revisiones;

static class Drivers
{
    // Un driver de fabricante con más de esta antigüedad se marca como "antiguo".
    const int AnosAntiguo = 3;

    public static (Seccion, List<DriverInfo>) Revisar()
    {
        var s = new Seccion { Nombre = "Drivers", Emoji = "🔧" };
        var limite = DateTime.Now.AddYears(-AnosAntiguo);

        var lista = Wmi.Consultar("SELECT DeviceName, DeviceClass, DriverProviderName, DriverVersion, DriverDate FROM Win32_PnPSignedDriver")
            .Where(d => Wmi.Texto(d, "DeviceName").Length > 0)
            .Select(d =>
            {
                var proveedor = Wmi.Texto(d, "DriverProviderName");
                var fecha = Wmi.Fecha(d, "DriverDate");
                return new DriverInfo
                {
                    Dispositivo = Wmi.Texto(d, "DeviceName"),
                    Tipo = Wmi.Texto(d, "DeviceClass"),
                    Proveedor = proveedor,
                    Version = Wmi.Texto(d, "DriverVersion"),
                    Fecha = fecha,
                    // Los drivers genéricos de Microsoft llevan fechas viejas a propósito; no cuentan.
                    Antiguo = fecha < limite && !proveedor.Contains("Microsoft", StringComparison.OrdinalIgnoreCase),
                };
            })
            .OrderByDescending(d => d.Antiguo)
            .ThenBy(d => d.Fecha)
            .ToList();

        // Dispositivos con error en el Administrador de dispositivos.
        var conError = Wmi.Consultar("SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Where(d => Wmi.Numero(d, "ConfigManagerErrorCode") != 22) // 22 = deshabilitado a propósito
            .ToList();
        if (conError.Count == 0)
            s.Agregar("Dispositivos con problemas", "Ninguno", Estado.Bien,
                "Todos los componentes tienen su driver funcionando.");
        foreach (var d in conError)
            s.Agregar("Dispositivo con problemas", Wmi.Texto(d, "Name") is { Length: > 0 } n ? n : "Dispositivo desconocido",
                Estado.Problema,
                $"Windows no puede usar este componente (código {Wmi.Numero(d, "ConfigManagerErrorCode")}). Instala su driver desde la app o la página de soporte del fabricante (botón en Resumen).");

        // Tarjetas de video: son los drivers que más conviene tener al día.
        foreach (var v in Wmi.Consultar("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))
        {
            var nombre = Wmi.Texto(v, "Name");
            var fecha = Wmi.Fecha(v, "DriverDate");
            if (fecha == null) continue;
            var meses = (DateTime.Now - fecha.Value).TotalDays / 30;
            var nvidia = nombre.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
            var dondeActualizar = nvidia ? "la app NVIDIA" : nombre.Contains("AMD", StringComparison.OrdinalIgnoreCase) || nombre.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                ? "AMD Software (Adrenalin)" : "la página del fabricante";
            s.Agregar($"Driver de video: {nombre}", $"{fecha:dd-MM-yyyy} (versión {Wmi.Texto(v, "DriverVersion")})",
                meses > 12 ? Estado.Atencion : Estado.Bien,
                meses > 12
                    ? $"Tiene más de un año. Actualízalo con {dondeActualizar} para mejor rendimiento en juegos y menos fallas."
                    : "Está razonablemente al día.");
        }

        var antiguos = lista.Count(d => d.Antiguo);
        s.Agregar("Drivers antiguos", antiguos == 0 ? "Ninguno" : $"{antiguos} de {lista.Count}",
            antiguos == 0 ? Estado.Bien : Estado.Atencion,
            antiguos == 0
                ? $"Ningún driver del fabricante tiene más de {AnosAntiguo} años."
                : $"Tienen más de {AnosAntiguo} años. No siempre es un problema (algunos componentes no reciben más actualizaciones), pero revisa en la app o página de soporte del fabricante si hay versiones nuevas. Aparecen primero en la lista de abajo.");

        return (s, lista);
    }
}
