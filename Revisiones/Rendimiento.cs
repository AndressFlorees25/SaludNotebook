namespace SaludNotebook.Revisiones;

static class Rendimiento
{
    public static Seccion Revisar()
    {
        var s = new Seccion { Nombre = "Rendimiento", Emoji = "⚡" };

        var os = Wmi.Primero("SELECT TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime FROM Win32_OperatingSystem");
        if (os != null)
        {
            var totalGb = Wmi.Numero(os, "TotalVisibleMemorySize") / 1024.0 / 1024.0;
            var usado = 100 - Wmi.Numero(os, "FreePhysicalMemory") * 100.0 / Math.Max(1, Wmi.Numero(os, "TotalVisibleMemorySize"));
            var (e, t) = usado switch
            {
                >= 90 => (Estado.Atencion, "La memoria está casi llena. Cierra programas o pestañas del navegador que no uses."),
                _ => (Estado.Bien, "Hay memoria disponible."),
            };
            s.Agregar("Memoria RAM en uso", $"{usado:0}% de {totalGb:0} GB", e, t);
            if (totalGb < 7.5)
                s.Agregar("Cantidad de RAM", $"{totalGb:0} GB", Estado.Atencion,
                    "Hoy 8 GB es lo mínimo cómodo. Ampliar la RAM es una mejora barata si tu notebook lo permite.");

            if (Wmi.Fecha(os, "LastBootUpTime") is { } arranque)
            {
                var dias = (DateTime.Now - arranque).TotalDays;
                s.Agregar("Tiempo encendido sin reiniciar", dias < 1 ? $"{(DateTime.Now - arranque).TotalHours:0} horas" : $"{dias:0} días",
                    dias >= 7 ? Estado.Atencion : Estado.Bien,
                    dias >= 7
                        ? "Hace mucho que no reinicias. Reiniciar de vez en cuando limpia la memoria y termina de instalar actualizaciones."
                        : "Bien, reinicias seguido.");
            }
        }

        var cpu = Wmi.Primero("SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
        if (cpu != null)
        {
            var uso = Wmi.Numero(cpu, "PercentProcessorTime");
            s.Agregar("Uso del procesador", $"{uso}%", uso >= 90 ? Estado.Atencion : Estado.Bien,
                uso >= 90
                    ? "El procesador está a tope. Si no estás usando nada pesado, abre el Administrador de tareas (Ctrl+Shift+Esc) para ver qué lo ocupa."
                    : "El procesador tiene capacidad libre.");
        }

        var inicio = Wmi.Consultar("SELECT Name FROM Win32_StartupCommand")
                        .Select(o => Wmi.Texto(o, "Name"))
                        .Where(n => n.Length > 0)
                        .Distinct()
                        .ToList();
        s.Agregar("Programas que arrancan con Windows", $"{inicio.Count}",
            inicio.Count > 12 ? Estado.Atencion : Estado.Bien,
            (inicio.Count > 12
                ? "Son muchos y hacen que el notebook tarde en encender. Desactiva los que no necesites en Administrador de tareas > Aplicaciones de arranque."
                : "Cantidad razonable.")
            + (inicio.Count > 0 ? "\n" + string.Join(", ", inicio) : ""));

        return s;
    }
}
