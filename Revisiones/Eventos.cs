using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;

namespace SaludNotebook.Revisiones;

// Lectura del registro de eventos "Sistema" de Windows.
static class Eventos
{
    public record Evento(int Id, DateTime Fecha, Dictionary<string, string> Datos);

    public static List<Evento> Leer(string proveedor, int dias, int? id = null)
    {
        var ms = (long)TimeSpan.FromDays(dias).TotalMilliseconds;
        var filtroId = id == null ? "" : $" and EventID={id}";
        var consulta = new EventLogQuery("System", PathType.LogName,
            $"*[System[Provider[@Name='{proveedor}']{filtroId} and TimeCreated[timediff(@SystemTime) <= {ms}]]]");

        var lista = new List<Evento>();
        try
        {
            using var lector = new EventLogReader(consulta);
            for (var ev = lector.ReadEvent(); ev != null; ev = lector.ReadEvent())
            {
                using (ev)
                    lista.Add(new Evento(ev.Id, ev.TimeCreated ?? DateTime.MinValue, Datos(ev)));
            }
        }
        catch (EventLogException)
        {
        }
        return lista;
    }

    // Los campos con nombre de <EventData>, ej. BugcheckCode del evento 41.
    static Dictionary<string, string> Datos(EventRecord ev)
    {
        var datos = new Dictionary<string, string>();
        try
        {
            foreach (var d in XElement.Parse(ev.ToXml()).Descendants().Where(e => e.Name.LocalName == "Data"))
            {
                var nombre = d.Attribute("Name")?.Value;
                if (nombre != null) datos[nombre] = d.Value;
            }
        }
        catch
        {
        }
        return datos;
    }
}
