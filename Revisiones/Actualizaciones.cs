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
                pendientes.Add(new ActualizacionPendiente
                {
                    Titulo = (string)u.Title,
                    Tipo = (int)u.Type == 2 ? "Driver" : "Windows / programas",
                });
            }
        }
        catch (Exception ex)
        {
            s.Agregar("Windows Update", "No se pudo consultar", Estado.Info,
                $"Revisa tu conexión a internet. Detalle: {ex.Message}");
            return (s, pendientes);
        }

        var drivers = pendientes.Count(p => p.Tipo == "Driver");
        if (pendientes.Count == 0)
            s.Agregar("Actualizaciones pendientes", "Ninguna", Estado.Bien, "Windows está al día.");
        else
            s.Agregar("Actualizaciones pendientes", $"{pendientes.Count} ({drivers} de drivers)", Estado.Atencion,
                "Hay actualizaciones esperando. Instálalas desde Windows Update (botón de abajo) y reinicia.");

        return (s, pendientes);
    }
}
