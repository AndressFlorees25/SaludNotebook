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
                    Com = u,
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
                "No son obligatorias y por eso Windows Update (y la app del fabricante) dicen \"todo actualizado\". " +
                "Suelen ser drivers nuevos del fabricante: instálalas solo si algo te está fallando. " +
                "Puedes instalarlas aquí mismo con el botón \"Instalar\" de cada una.");

        return (s, pendientes);
    }

    // Descarga e instala una actualización. Devuelve el mensaje final y si pide reiniciar.
    public static (bool Ok, bool Reiniciar, string Mensaje) Instalar(ActualizacionPendiente p, Action<string> progreso)
    {
        try
        {
            dynamic sesion = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)!;
            dynamic lista = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
            dynamic u = p.Com ?? throw new InvalidOperationException("Actualización no disponible.");
            if (!(bool)u.EulaAccepted) u.AcceptEula();
            lista.Add(u);

            progreso("Descargando...");
            dynamic descargador = sesion.CreateUpdateDownloader();
            descargador.Updates = lista;
            descargador.Download();

            progreso("Instalando...");
            dynamic instalador = sesion.CreateUpdateInstaller();
            instalador.Updates = lista;
            dynamic resultado = instalador.Install();

            // 2 = correcta, 3 = correcta con errores; el resto es fallo.
            int codigo = resultado.ResultCode;
            bool reiniciar = resultado.RebootRequired;
            if (codigo is 2 or 3)
                return (true, reiniciar, reiniciar ? "Instalada ✔ (reinicia para terminar)" : "Instalada ✔");

            uint hr = unchecked((uint)(int)resultado.GetUpdateResult(0).HResult);
            return (false, false, hr switch
            {
                0x80240016 => "Había otra instalación en curso. Pulsa Instalar de nuevo.",
                0x8024A000 or 0x80240030 => "Windows pide reiniciar antes. Reinicia y vuelve a intentarlo.",
                _ => $"No se pudo instalar (error 0x{hr:X8})",
            });
        }
        catch (Exception ex)
        {
            return (false, false, "Error: " + ex.Message);
        }
    }
}
