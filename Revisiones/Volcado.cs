using System.IO;
using System.Text;

namespace SaludNotebook.Revisiones;

// Lector mínimo de minidumps de Windows (formato "PAGEDU64" de 64 bits).
// Saca el código del pantallazo y adivina qué driver lo causó.
static class Volcado
{
    public record Resultado(uint Codigo, ulong[] Parametros, string? Culpable);

    record Modulo(string Nombre, ulong Base, uint Tamano)
    {
        public bool Contiene(ulong dir) => dir >= Base && dir < Base + Tamano;
    }

    // Módulos del núcleo que aparecen en casi todos los fallos y no sirven como "culpable".
    static readonly string[] Genericos = { "ntoskrnl.exe", "hal.dll", "ntkrnlmp.exe", "ntkrnlpa.exe" };

    const int OffsetCodigo = 0x38;
    const int OffsetParametros = 0x40;
    const int OffsetTriage = 0x2000;

    public static Resultado? Analizar(string ruta)
    {
        byte[] b;
        try
        {
            b = File.ReadAllBytes(ruta);
        }
        catch
        {
            return null;
        }
        if (b.Length < OffsetTriage + 0x100 || Encoding.ASCII.GetString(b, 0, 8) != "PAGEDU64") return null;

        var codigo = BitConverter.ToUInt32(b, OffsetCodigo);
        var parametros = Enumerable.Range(0, 4).Select(i => BitConverter.ToUInt64(b, OffsetParametros + 8 * i)).ToArray();

        string? culpable = null;
        try
        {
            var modulos = LeerModulos(b);
            if (modulos.Count > 0) culpable = BuscarCulpable(b, parametros, modulos);
        }
        catch
        {
        }
        return new Resultado(codigo, parametros, culpable);
    }

    static string? BuscarCulpable(byte[] b, ulong[] parametros, List<Modulo> modulos)
    {
        bool EsGenerico(Modulo m) => Genericos.Contains(m.Nombre, StringComparer.OrdinalIgnoreCase);

        // 1. Algún parámetro del error apunta dentro de un driver (ej. la dirección donde falló).
        foreach (var p in parametros)
        {
            var m = modulos.FirstOrDefault(x => x.Contiene(p));
            if (m != null && !EsGenerico(m)) return m.Nombre;
        }

        // 2. El driver que más aparece en la pila de llamadas del momento del fallo.
        var pila = U32(b, OffsetTriage + 0x28);
        var tamanoPila = U32(b, OffsetTriage + 0x2C);
        if (pila == 0 || pila + tamanoPila > b.Length) return null;

        var conteo = new Dictionary<Modulo, int>();
        for (long o = pila; o + 8 <= pila + tamanoPila; o += 8)
        {
            var valor = BitConverter.ToUInt64(b, (int)o);
            var m = modulos.FirstOrDefault(x => x.Contiene(valor));
            if (m != null) conteo[m] = conteo.GetValueOrDefault(m) + 1;
        }
        var candidato = conteo.Where(kv => !EsGenerico(kv.Key)).OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (candidato.Key != null) return candidato.Key.Nombre;
        return conteo.Count > 0 ? conteo.OrderByDescending(kv => kv.Value).First().Key.Nombre : null;
    }

    static List<Modulo> LeerModulos(byte[] b)
    {
        var lista = U32(b, OffsetTriage + 0x30);
        var cantidad = U32(b, OffsetTriage + 0x34);
        var cadenas = U32(b, OffsetTriage + 0x38);
        if (lista == 0 || cantidad == 0 || cantidad > 2000 || cadenas <= lista || cadenas > b.Length) return new();

        // Cada entrada: offset del nombre + KLDR_DATA_TABLE_ENTRY (DllBase en +0x38, SizeOfImage en +0x48).
        var tamanoEntrada = (cadenas - lista) / cantidad;
        if (tamanoEntrada is < 0x80 or > 0x100) tamanoEntrada = 0x90;

        var modulos = new List<Modulo>();
        for (uint i = 0; i < cantidad; i++)
        {
            var e = lista + i * tamanoEntrada;
            if (e + 0x50 > b.Length) break;
            var offNombre = U32(b, e);
            if (offNombre + 4 > b.Length) continue;
            var largo = (int)Math.Min(U32(b, offNombre), 260);
            if (offNombre + 4 + largo * 2 > b.Length) continue;
            var nombre = Encoding.Unicode.GetString(b, (int)offNombre + 4, largo * 2);
            modulos.Add(new Modulo(nombre, BitConverter.ToUInt64(b, (int)e + 0x38), U32(b, e + 0x48)));
        }
        return modulos;
    }

    static uint U32(byte[] b, long offset) => BitConverter.ToUInt32(b, (int)offset);
}
