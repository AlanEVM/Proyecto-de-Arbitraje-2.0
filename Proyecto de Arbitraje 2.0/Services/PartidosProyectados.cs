using ProyectoArbitraje.Models;

namespace ProyectoArbitraje.Services;

// Misma fórmula del panel del torneo, para que panel y Excel muestren el mismo total
public static class PartidosProyectados
{
    public static int Calcular(Categoria categoria, List<int> conteosPorGrupo, int totalActivos,
        int partidosRondaInicialReales, int partidosFaseFinalReales)
    {
        switch (categoria.Formato)
        {
            case "GruposFaseFinal":
                {
                    int partidosGrupos = partidosRondaInicialReales > 0
                        ? partidosRondaInicialReales
                        : conteosPorGrupo.Sum(n => n * (n - 1) / 2);

                    int clasificadosPorGrupo = categoria.ClasificadosPorGrupo ?? 2;
                    int clasificados = conteosPorGrupo.Sum(n => Math.Min(n, clasificadosPorGrupo));
                    int partidosFaseFinal = partidosFaseFinalReales > 0
                        ? partidosFaseFinalReales
                        : (clasificados >= 2 ? clasificados - 1 : 0);

                    return partidosGrupos + partidosFaseFinal;
                }
            case "Eliminatoria":
                return totalActivos >= 2 ? totalActivos - 1 : 0;
            case "RoundRobin":
                return partidosRondaInicialReales > 0
                    ? partidosRondaInicialReales
                    : (totalActivos >= 2 ? totalActivos * (totalActivos - 1) / 2 : 0);
            case "Jornadas":
                {
                    const int vueltasFijas = 2;
                    int partidosLiga = totalActivos >= 2 ? vueltasFijas * totalActivos * (totalActivos - 1) / 2 : 0;

                    int numBandas = totalActivos / 4;
                    int partidosBandas = partidosFaseFinalReales > 0 ? partidosFaseFinalReales : numBandas * 3;

                    return partidosLiga + partidosBandas;
                }
            default:
                return 0;
        }
    }
}