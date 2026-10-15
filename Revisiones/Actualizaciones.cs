namespace SaludNotebook.Revisiones;

static class Actualizaciones
{
    // Consulta a Windows Update (puede tardar uno o dos minutos).
    public static (Seccion, List<ActualizacionPendiente>) Revisar()
    {
        var s = new Seccion { Nombre = "Actualizaciones", Emoji = "🔄" };
        var pendientes = new List<ActualizacionPendiente>();

        try
        {
            var tipo = Type.GetTypeFromProgID("Microsoft.Update.Session")
                       ?? throw new InvalidOperationException("Windows Update no está disponible.");
            dynamic sesion = Activator.CreateInstance(tipo)!;
            dynamic buscador = sesion.CreateUpdateSearcher();
            dynamic resultado = buscador.Search("IsInstalled=0 and IsHidden=0");
            dynamic updates = resultado.Updates;
            int total = updates.Count;
            for (int i = 0; i < total; i++)
            {
                dynamic u = updates.Item(i);
                // Windows Update solo instala solo las "autoseleccionadas"; el resto las muestra
                // escondidas en Opciones avanzadas > Actualizaciones opcionales.
                bool opcional = !(bool)u.AutoSelectOnWebSites;
                pendientes.Add(new ActualizacionPendiente
                {
                    Titulo = (string)u.Title,
                    Tipo = ((int)u.Type == 2 ? "Driver" : "Windows") + (opcional ? " · opcional" : ""),
                    Opcional = opcional,
                });
            }
        }
        catch (Exception ex)
        {
            s.Agregar("Windows Update", "No se pudo consultar", Estado.Info,
                $"Revisa tu conexión a internet. Detalle: {ex.Message}");
            return (s, pendientes);
        }

        var importantes = pendientes.Count(p => !p.Opcional);
        var opcionales = pendientes.Count(p => p.Opcional);

        if (importantes == 0)
            s.Agregar("Actualizaciones importantes", "Ninguna", Estado.Bien, "Windows está al día.");
        else
            s.Agregar("Actualizaciones importantes", $"{importantes} pendiente{(importantes == 1 ? "" : "s")}", Estado.Atencion,
                "Windows normalmente las instala solo. Si quieres adelantarlo, toca \"Abrir Windows Update\" (arriba) y luego \"Buscar actualizaciones\".");

        if (opcionales > 0)
            s.Agregar("Actualizaciones opcionales", $"{opcionales} disponible{(opcionales == 1 ? "" : "s")}", Estado.Info,
                "No son obligatorias y por eso Windows Update y Lenovo Vantage dicen \"todo actualizado\". " +
                "Suelen ser drivers nuevos del fabricante: instálalas solo si algo te está fallando. " +
                "Para verlas, toca \"Ver actualizaciones opcionales\" (arriba).");

        return (s, pendientes);
    }
}
