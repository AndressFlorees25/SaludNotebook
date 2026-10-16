using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SaludNotebook;

// Gráfico de líneas sencillo dibujado sobre un Canvas.
static class Grafico
{
    static readonly Brush Guia = Colores.Crear("#E0E6EB");
    static readonly Brush TextoGuia = Colores.Crear("#90A4AE");
    const double MargenIzq = 44, MargenSup = 8, MargenInf = 8;

    public static void Dibujar(Canvas lienzo, IReadOnlyList<IReadOnlyList<double?>> series, Brush[] colores, string unidad)
    {
        lienzo.Children.Clear();
        var ancho = lienzo.ActualWidth;
        var alto = lienzo.ActualHeight;
        var valores = series.SelectMany(s => s).OfType<double>().ToList();
        var puntos = series.Count == 0 ? 0 : series.Max(s => s.Count);
        if (ancho < 60 || alto < 40 || valores.Count == 0)
        {
            lienzo.Children.Add(new TextBlock { Text = "Todavía no hay datos.", Foreground = TextoGuia, Margin = new Thickness(8) });
            return;
        }

        // Escala con un poco de aire arriba y abajo.
        var min = valores.Min();
        var max = valores.Max();
        var aire = Math.Max(1, (max - min) * 0.15);
        min = Math.Max(0, Math.Floor(min - aire));
        max = Math.Ceiling(max + aire);

        double Y(double v) => MargenSup + (alto - MargenSup - MargenInf) * (1 - (v - min) / (max - min));
        double X(int i) => puntos <= 1 ? MargenIzq + (ancho - MargenIzq) / 2 : MargenIzq + i * (ancho - MargenIzq - 4) / (puntos - 1);

        foreach (var v in new[] { min, (min + max) / 2, max })
        {
            lienzo.Children.Add(new Line { X1 = MargenIzq, X2 = ancho, Y1 = Y(v), Y2 = Y(v), Stroke = Guia, StrokeThickness = 1 });
            var etiqueta = new TextBlock { Text = $"{v:0}{unidad}", Foreground = TextoGuia, FontSize = 11 };
            Canvas.SetLeft(etiqueta, 0);
            Canvas.SetTop(etiqueta, Y(v) - 8);
            lienzo.Children.Add(etiqueta);
        }

        for (var s = 0; s < series.Count; s++)
        {
            var linea = new Polyline { Stroke = colores[s % colores.Length], StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
            for (var i = 0; i < series[s].Count; i++)
                if (series[s][i] is { } v) linea.Points.Add(new Point(X(i), Y(v)));
            lienzo.Children.Add(linea);

            // Con un solo punto, se marca con un círculo para que se vea.
            if (linea.Points.Count == 1)
            {
                var punto = new Ellipse { Width = 8, Height = 8, Fill = linea.Stroke };
                Canvas.SetLeft(punto, linea.Points[0].X - 4);
                Canvas.SetTop(punto, linea.Points[0].Y - 4);
                lienzo.Children.Add(punto);
            }
        }
    }
}
