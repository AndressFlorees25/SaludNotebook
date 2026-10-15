namespace SaludNotebook.Revisiones;

static class Equipo
{
    public static InfoEquipo Leer()
    {
        var cs = Wmi.Primero("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
        var prod = Wmi.Primero("SELECT Version FROM Win32_ComputerSystemProduct");
        var bios = Wmi.Primero("SELECT SerialNumber, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
        var cpu = Wmi.Primero("SELECT Name FROM Win32_Processor");
        var os = Wmi.Primero("SELECT Caption, BuildNumber FROM Win32_OperatingSystem");

        var fabricante = cs == null ? "" : Wmi.Texto(cs, "Manufacturer");
        // Lenovo guarda el nombre comercial (ej. "LOQ 15ARP9") en Version; el Model es un código interno.
        var modelo = prod != null && Wmi.Texto(prod, "Version") is { Length: > 0 } v && fabricante.Contains("LENOVO", StringComparison.OrdinalIgnoreCase)
            ? v
            : cs == null ? "" : Wmi.Texto(cs, "Model");

        var biosFecha = bios == null ? null : Wmi.Fecha(bios, "ReleaseDate");
        return new InfoEquipo
        {
            Fabricante = fabricante.Length > 0 ? char.ToUpper(fabricante[0]) + fabricante[1..].ToLower() : "Desconocido",
            Modelo = modelo,
            NumeroSerie = bios == null ? "" : Wmi.Texto(bios, "SerialNumber"),
            Procesador = cpu == null ? "" : Wmi.Texto(cpu, "Name"),
            Sistema = os == null ? "" : $"{Wmi.Texto(os, "Caption")} (compilación {Wmi.Texto(os, "BuildNumber")})",
            Bios = bios == null ? "" : $"{Wmi.Texto(bios, "SMBIOSBIOSVersion")}{(biosFecha is { } f ? $" del {f:dd-MM-yyyy}" : "")}",
        };
    }

    public static string PaginaSoporte(InfoEquipo e)
    {
        var f = e.Fabricante.ToLowerInvariant();
        if (f.Contains("lenovo")) return "https://pcsupport.lenovo.com/";
        if (f.Contains("hp")) return "https://support.hp.com/drivers";
        if (f.Contains("dell")) return "https://www.dell.com/support/home";
        if (f.Contains("asus")) return "https://www.asus.com/support/download-center/";
        if (f.Contains("acer")) return "https://www.acer.com/support";
        if (f.Contains("msi") || f.Contains("micro-star")) return "https://www.msi.com/support";
        return "https://www.google.com/search?q=" + Uri.EscapeDataString($"{e.Fabricante} {e.Modelo} drivers");
    }
}
