namespace SaludNotebook.Revisiones;

public class ResultadoRevision
{
    public InfoEquipo Equipo { get; init; } = new();
    public List<Seccion> Secciones { get; } = new();
    public List<DriverInfo> Drivers { get; init; } = new();
    public List<Componente> Componentes { get; init; } = new();
    public List<Fallo> Fallos { get; init; } = new();
    public DateTime Fecha { get; } = DateTime.Now;

    public Estado EstadoGeneral => Revisor.PeorEstado(Secciones);
}

// Corre todas las revisiones rápidas en paralelo (Windows Update va aparte porque tarda).
static class Revisor
{
    public static async Task<ResultadoRevision> RevisarAsync()
    {
        var equipo = Task.Run(Equipo.Leer);
        var bateria = Task.Run(Bateria.Revisar);
        var disco = Task.Run(Disco.Revisar);
        var temperatura = Task.Run(Temperatura.Revisar);
        var rendimiento = Task.Run(Rendimiento.Revisar);
        var drivers = Task.Run(Drivers.Revisar);
        var componentes = Task.Run(Componentes.Revisar);
        var fallos = Task.Run(Fallos.Revisar);
        await Task.WhenAll(equipo, bateria, disco, temperatura, rendimiento, drivers, componentes, fallos);

        var r = new ResultadoRevision
        {
            Equipo = equipo.Result,
            Drivers = drivers.Result.Item2,
            Componentes = componentes.Result.Item2,
            Fallos = fallos.Result.Item2,
        };
        r.Secciones.AddRange(new[]
        {
            componentes.Result.Item1, bateria.Result, disco.Result, temperatura.Result,
            rendimiento.Result, drivers.Result.Item1, fallos.Result.Item1,
        });
        return r;
    }

    public static Estado PeorEstado(IEnumerable<Seccion> secciones)
    {
        var evaluadas = secciones.Where(s => s.Estado != Estado.Info).ToList();
        return evaluadas.Count == 0 ? Estado.Info : evaluadas.Max(s => s.Estado);
    }
}
