# Salud del Notebook

App de escritorio (WPF, .NET 10) que revisa el estado de un notebook con Windows y lo muestra con un semáforo fácil de entender:

- **Batería**: vida útil (capacidad actual vs. original) y ciclos de carga.
- **Disco**: estado SMART, desgaste del SSD, errores y espacio libre.
- **Temperatura**: sensor del sistema y tarjeta gráfica NVIDIA.
- **Rendimiento**: RAM, procesador, días sin reiniciar y programas de inicio.
- **Drivers**: dispositivos con error, driver de video y drivers antiguos.
- **Actualizaciones**: pendientes en Windows Update (incluye drivers).

Pide permisos de administrador al abrirse (para leer temperaturas y el desgaste del disco).

## Ejecutar

```
dotnet run
```
