using System.IO;
using System.Xml.Linq;

namespace SaludNotebook.Revisiones;

static class Bateria
{
    public static Seccion Revisar()
    {
        var s = new Seccion { Nombre = "Batería", Emoji = "🔋" };

        var bat = Wmi.Primero("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
        if (bat == null)
        {
            s.Agregar("Batería", "No detectada", Estado.Info,
                "No se encontró batería. Si es un notebook, puede estar desconectada o dañada.");
            return s;
        }

        var carga = Wmi.Numero(bat, "EstimatedChargeRemaining");
        var enchufado = Wmi.Numero(bat, "BatteryStatus") == 2;
        s.Agregar("Carga actual", $"{carga}% {(enchufado ? "(enchufado)" : "(usando batería)")}", Estado.Info,
            "Cuánta carga tiene la batería en este momento.");

        // powercfg genera un informe con la capacidad original y la actual.
        var archivo = Path.Combine(Path.GetTempPath(), "salud-notebook-bateria.xml");
        if (Wmi.Ejecutar("powercfg", $"/batteryreport /xml /output \"{archivo}\"") == null || !File.Exists(archivo))
        {
            s.Agregar("Vida útil", "No disponible", Estado.Info, "Windows no entregó el informe de batería.");
            return s;
        }

        var batXml = XDocument.Load(archivo).Descendants().FirstOrDefault(e => e.Name.LocalName == "Battery");
        File.Delete(archivo);
        if (batXml == null) return s;

        long Valor(string nombre) =>
            long.TryParse(batXml.Elements().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value, out var v) ? v : 0;

        var diseno = Valor("DesignCapacity");
        var actual = Valor("FullChargeCapacity");
        var ciclos = Valor("CycleCount");

        if (diseno > 0 && actual > 0)
        {
            var salud = Math.Min(100, actual * 100.0 / diseno);
            var (estado, texto) = salud switch
            {
                >= 80 => (Estado.Bien, "La batería está sana. Es normal que pierda un poco de capacidad con el uso."),
                >= 60 => (Estado.Atencion, "La batería ya se desgastó bastante. Dura menos que nueva, pero aún sirve. Vigílala."),
                _ => (Estado.Problema, "La batería perdió mucha capacidad. Conviene cambiarla pronto."),
            };
            s.Agregar("Vida útil de la batería", $"{salud:0}% de su capacidad original", estado,
                $"{texto} (Original: {diseno / 1000.0:0.#} Wh, ahora: {actual / 1000.0:0.#} Wh)");
        }

        if (ciclos > 0)
        {
            var estado = ciclos >= 800 ? Estado.Atencion : Estado.Bien;
            s.Agregar("Ciclos de carga", $"{ciclos} ciclos", estado,
                ciclos >= 800
                    ? "Muchos ciclos: la batería se acerca al final de su vida útil típica (~800-1000)."
                    : "Un ciclo es usar el 100% de la carga (sumando cargas parciales). Una batería típica aguanta 800-1000.");
        }

        return s;
    }
}
