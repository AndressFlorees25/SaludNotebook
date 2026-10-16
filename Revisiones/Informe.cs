using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SaludNotebook.Revisiones;

// Genera un informe PDF con todo lo revisado, para guardar o llevar al técnico.
static class Informe
{
    public static void Generar(ResultadoRevision r, IReadOnlyList<ActualizacionPendiente> actualizaciones,
                               IReadOnlyList<SesionJuego> sesiones, string ruta)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // La fuente del PDF (Lato) no trae emojis: se quitan del texto, y si quedara algún símbolo raro
        // se dibuja vacío en vez de cancelar el informe.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Color.FromHex("#263238")));

            page.Header().PaddingBottom(10).Column(col =>
            {
                col.Item().Text("Informe de salud del notebook").FontSize(20).Bold();
                col.Item().Text($"{r.Equipo.Fabricante} {r.Equipo.Modelo} · N° de serie {r.Equipo.NumeroSerie}").FontColor(Color.FromHex("#546E7A"));
                col.Item().Text($"Generado el {r.Fecha:dd-MM-yyyy} a las {r.Fecha:HH:mm}").FontColor(Color.FromHex("#546E7A"));
            });

            page.Content().Column(col =>
            {
                col.Spacing(6);

                col.Item().Background(Color.FromHex(Colores.Hex(r.EstadoGeneral))).Padding(10)
                   .Text($"Estado general: {Colores.Etiqueta(r.EstadoGeneral)}").FontColor(Colors.White).FontSize(14).Bold();

                Titulo(col, "Equipo");
                Tabla(col, new[]
                {
                    ("Procesador", r.Equipo.Procesador),
                    ("Sistema", r.Equipo.Sistema),
                    ("BIOS", r.Equipo.Bios),
                });

                foreach (var s in r.Secciones)
                {
                    Titulo(col, $"{s.Nombre} — {s.EstadoTexto}", s.Estado);
                    foreach (var h in s.Hallazgos)
                        Linea(col, h.Estado, $"{h.Titulo}: {h.Valor}", h.Explicacion);
                }

                if (r.Componentes.Count > 0)
                {
                    Titulo(col, "Componentes internos");
                    foreach (var c in r.Componentes)
                        Linea(col, c.Estado, $"{c.Categoria}: {c.Nombre} ({c.EstadoTexto})", c.Detalle);
                }

                if (r.Fallos.Count > 0)
                {
                    Titulo(col, "Historial de fallos (últimos 90 días)");
                    foreach (var f in r.Fallos)
                        Linea(col, f.Estado, $"{f.FechaTexto} · {f.Tipo}: {f.Causa}",
                              (f.Codigo.Length > 0 ? f.Codigo + ". " : "") + f.Explicacion);
                }

                if (actualizaciones.Count > 0)
                {
                    Titulo(col, "Actualizaciones pendientes");
                    foreach (var a in actualizaciones)
                        Linea(col, a.Opcional ? Estado.Info : Estado.Atencion, a.Titulo, a.Tipo);
                }

                if (sesiones.Count > 0)
                {
                    Titulo(col, "Temperaturas jugando (Modo juego)");
                    foreach (var j in sesiones.TakeLast(10))
                        Linea(col, j.Estado, $"{j.FechaTexto} · {j.DuracionTexto} · tarjeta gráfica máx. {j.GpuTexto} · sistema máx. {j.SistemaTexto}", j.Veredicto);
                }

                var drivers = r.Drivers.Where(d => d.Antiguo).ToList();
                if (drivers.Count > 0)
                {
                    Titulo(col, "Drivers antiguos");
                    foreach (var d in drivers)
                        Linea(col, Estado.Info, d.Dispositivo, $"{d.Proveedor} · versión {d.Version} · {d.FechaTexto}");
                }
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Salud del Notebook · página ").FontColor(Color.FromHex("#90A4AE"));
                t.CurrentPageNumber().FontColor(Color.FromHex("#90A4AE"));
                t.Span(" de ").FontColor(Color.FromHex("#90A4AE"));
                t.TotalPages().FontColor(Color.FromHex("#90A4AE"));
            });
        })).GeneratePdf(ruta);
    }

    static void Titulo(ColumnDescriptor col, string texto, Estado? estado = null)
    {
        col.Item().PaddingTop(10).BorderBottom(1).BorderColor(Color.FromHex(estado is { } e ? Colores.Hex(e) : "#B0BEC5"))
           .PaddingBottom(2).Text(SinEmojis(texto)).FontSize(13).Bold();
    }

    static string SinEmojis(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (!char.IsSurrogate(c) && c is not (>= '☀' and <= '➿') && c != '️') sb.Append(c);
        return sb.ToString().Replace("  ", " ").Trim();
    }

    static void Linea(ColumnDescriptor col, Estado estado, string titulo, string detalle)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(70).AlignTop().Background(Color.FromHex(Colores.Hex(estado))).Padding(2).AlignCenter()
               .Text(Etiqueta(estado)).FontColor(Colors.White).FontSize(8).Bold();
            row.RelativeItem().PaddingLeft(8).Column(c =>
            {
                c.Item().Text(SinEmojis(titulo)).SemiBold();
                if (detalle.Length > 0) c.Item().Text(SinEmojis(detalle)).FontColor(Color.FromHex("#546E7A")).FontSize(9);
            });
        });
    }

    static void Tabla(ColumnDescriptor col, (string, string)[] filas)
    {
        foreach (var (k, v) in filas)
            col.Item().Row(row =>
            {
                row.ConstantItem(110).Text(k).FontColor(Color.FromHex("#607D8B"));
                row.RelativeItem().Text(SinEmojis(v)).SemiBold();
            });
    }

    static string Etiqueta(Estado e) => e switch
    {
        Estado.Bien => "BIEN",
        Estado.Atencion => "REVISAR",
        Estado.Problema => "PROBLEMA",
        _ => "INFO",
    };
}
