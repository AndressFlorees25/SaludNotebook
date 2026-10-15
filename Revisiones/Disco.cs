using System.IO;
using System.Management;

namespace SaludNotebook.Revisiones;

static class Disco
{
    const string NsStorage = @"root\Microsoft\Windows\Storage";

    public static Seccion Revisar()
    {
        var s = new Seccion { Nombre = "Disco", Emoji = "💾" };

        try
        {
            foreach (var d in Wmi.Consultar("SELECT * FROM MSFT_PhysicalDisk", NsStorage))
                RevisarDisco(s, d);
        }
        catch (Exception ex)
        {
            s.Agregar("Estado del disco", "No disponible", Estado.Info, ex.Message);
        }

        foreach (var unidad in DriveInfo.GetDrives().Where(u => u.DriveType == DriveType.Fixed && u.IsReady))
        {
            var libre = unidad.AvailableFreeSpace * 100.0 / unidad.TotalSize;
            var gbLibres = unidad.AvailableFreeSpace / 1e9;
            var (estado, texto) = libre switch
            {
                < 10 => (Estado.Problema, "Queda muy poco espacio: Windows se pone lento y puede fallar al actualizar. Borra archivos o desinstala programas."),
                < 20 => (Estado.Atencion, "Se está llenando. Conviene liberar espacio (descargas, papelera, juegos que ya no usas)."),
                _ => (Estado.Bien, "Hay espacio de sobra."),
            };
            s.Agregar($"Espacio libre en {unidad.Name.TrimEnd('\\')}",
                $"{gbLibres:0} GB libres de {unidad.TotalSize / 1e9:0} GB ({libre:0}%)", estado, texto);
        }

        return s;
    }

    static void RevisarDisco(Seccion s, ManagementObject d)
    {
        var nombre = Wmi.Texto(d, "FriendlyName");
        var tipo = Wmi.Numero(d, "MediaType") switch { 3 => "HDD", 4 => "SSD", _ => "disco" };

        var (estado, texto) = Wmi.Numero(d, "HealthStatus") switch
        {
            0 => (Estado.Bien, "El disco reporta que está sano."),
            1 => (Estado.Atencion, "El disco reporta advertencias. Haz respaldo de tus archivos importantes."),
            2 => (Estado.Problema, "¡El disco reporta fallas! Respalda tus archivos YA y piensa en cambiarlo."),
            _ => (Estado.Info, "El disco no informó su estado."),
        };
        s.Agregar($"Estado del {tipo}", nombre, estado, texto);

        // Contadores de confiabilidad (desgaste, temperatura, errores). Requiere administrador.
        ManagementBaseObject? rel = null;
        try
        {
            rel = d.GetRelated("MSFT_StorageReliabilityCounter").Cast<ManagementBaseObject>().FirstOrDefault();
        }
        catch
        {
        }
        if (rel == null) return;

        if (rel["Wear"] is { } w)
        {
            var desgaste = Convert.ToInt32(w);
            var (e, t) = desgaste switch
            {
                >= 90 => (Estado.Problema, "El SSD está casi al límite de escrituras. Planea cambiarlo."),
                >= 70 => (Estado.Atencion, "El SSD ya tiene bastante uso. Aún funciona, pero vigílalo."),
                _ => (Estado.Bien, "El SSD tiene poco desgaste."),
            };
            s.Agregar("Desgaste del SSD", $"{desgaste}% usado", e, t);
        }

        if (rel["Temperature"] is { } temp && Convert.ToInt32(temp) > 0)
        {
            var c = Convert.ToInt32(temp);
            s.Agregar("Temperatura del disco", $"{c} °C", c >= 70 ? Estado.Atencion : Estado.Bien,
                c >= 70 ? "El disco está caliente. Revisa la ventilación." : "Temperatura normal.");
        }

        var errores = Wmi.Numero(rel, "ReadErrorsUncorrected") + Wmi.Numero(rel, "WriteErrorsUncorrected");
        if (errores > 0)
            s.Agregar("Errores de lectura/escritura", $"{errores}", Estado.Problema,
                "El disco tuvo errores que no pudo corregir. Respalda tus archivos.");

        if (Wmi.Numero(rel, "PowerOnHours") is > 0 and var horas)
            s.Agregar("Horas de uso del disco", $"{horas:N0} horas", Estado.Info,
                "Tiempo total que el disco ha estado encendido.");
    }
}
