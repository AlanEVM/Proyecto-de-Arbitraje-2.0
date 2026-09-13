using Microsoft.EntityFrameworkCore;
using ProyectoArbitraje.Components.Pages;
using ProyectoArbitraje.Data;
using ProyectoArbitraje.Models;

namespace ProyectoArbitraje.Services;

public class PartidoQueueItem
{
    public Partido Partido { get; set; } = null!;
    public string NombreA { get; set; } = "";
    public string NombreB { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string? Grupo { get; set; }
    public int? Jornada { get; set; }
    public int? CanchaNumero { get; set; }
}

public class CalendarioService
{
    private readonly IDbContextFactory<TorneoContext> _factory;
    private readonly FaseFinalService _faseFinalSvc;
    private readonly TorneoEventBus _eventBus;

    public CalendarioService(IDbContextFactory<TorneoContext> factory, FaseFinalService faseFinalSvc, TorneoEventBus eventBus)
    {
        _factory = factory;
        _faseFinalSvc = faseFinalSvc;
        _eventBus = eventBus;
    }

    // =================================================================
    // GENERACIÓN DE PARTIDOS (round robin, método del círculo)
    // =================================================================

    private static List<List<(int a, int b)>> GenerarRondasRoundRobin(List<int> ids)
    {
        var lista = new List<int?>(ids.Cast<int?>());
        if (lista.Count % 2 == 1) lista.Add(null);
        int n = lista.Count;
        var rotables = new List<int?>(lista);
        var rondas = new List<List<(int, int)>>();

        for (int ronda = 0; ronda < n - 1; ronda++)
        {
            var pares = new List<(int, int)>();
            for (int i = 0; i < n / 2; i++)
            {
                var a = rotables[i];
                var b = rotables[n - 1 - i];
                if (a.HasValue && b.HasValue)
                    pares.Add((a.Value, b.Value));
            }
            rondas.Add(pares);
            var ultimo = rotables[^1];
            rotables.RemoveAt(rotables.Count - 1);
            rotables.Insert(1, ultimo);
        }
        return rondas;
    }

    private static List<(int a, int b)> GenerarParesRoundRobin(List<int> ids)
    {
        return GenerarRondasRoundRobin(ids).SelectMany(r => r).ToList();
    }

    public async Task<int> GenerarRoundRobinAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return 0;
        if (categoria.Formato != "RoundRobin" && categoria.Formato != "Eliminatoria") return 0;

        bool yaExisten = await db.Partidos.AnyAsync(p => p.CategoriaId == categoriaId);
        if (yaExisten) return 0;

        var ids = await db.Competidores.Where(c => c.CategoriaId == categoriaId && c.Activo).Select(c => c.Id).ToListAsync();
        if (ids.Count < 2 || ids.Count > 6) return 0;

        using var transaccion = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        int generados;
        try
        {
            var rondas = GenerarRondasRoundRobin(ids);
            var pares = rondas.SelectMany(r => r).ToList();

            int orden = 1;
            foreach (var (a, b) in pares)
            {
                db.Partidos.Add(new Partido
                {
                    CategoriaId = categoriaId,
                    CompetidorAid = a,
                    CompetidorBid = b,
                    Fase = "Liga",
                    Estado = "Pendiente",
                    OrdenCola = orden++
                });
            }
            generados = pares.Count;

            categoria.Formato = "RoundRobin";
            await db.SaveChangesAsync();
            await transaccion.CommitAsync();
        }
        catch
        {
            await transaccion.RollbackAsync();
            return 0;
        }

        _eventBus.Notificar();
        return generados;
    }

    public async Task<int> GenerarPartidosGrupoFaseAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null || categoria.Formato != "GruposFaseFinal") return 0;

        using var transaccion = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        int generados;
        try
        {
            var grupos = await db.Grupos.Where(g => g.CategoriaId == categoriaId).ToListAsync();
            generados = 0;

            foreach (var grupo in grupos)
            {
                bool yaGenerado = await db.Partidos.AnyAsync(p => p.GrupoId == grupo.Id && p.Fase == "Grupos");
                if (yaGenerado) continue;

                var competidorIds = await db.Competidores
                    .Where(c => c.GrupoId == grupo.Id && c.Activo)
                    .Select(c => c.Id)
                    .ToListAsync();

                if (competidorIds.Count < 2) continue;

                var pares = GenerarParesRoundRobin(competidorIds);
                int orden = 0;
                foreach (var (a, b) in pares)
                {
                    db.Partidos.Add(new Partido
                    {
                        CategoriaId = categoriaId,
                        GrupoId = grupo.Id,
                        CompetidorAid = a,
                        CompetidorBid = b,
                        Fase = "Grupos",
                        Estado = "Pendiente",
                        OrdenCola = orden++
                    });
                    generados++;
                }
            }

            await db.SaveChangesAsync();
            await transaccion.CommitAsync();
        }
        catch
        {
            await transaccion.RollbackAsync();
            return 0;
        }

        _eventBus.Notificar();
        return generados;
    }

    public static int CalcularTotalJornadas(int totalCompetidores, int vueltas)
    {
        if (totalCompetidores < 2) return 0;
        var idsFalsos = Enumerable.Range(1, totalCompetidores).ToList();
        var rondasBase = GenerarRondasRoundRobin(idsFalsos);
        int totalRondas = rondasBase.Count * Math.Max(1, vueltas);
        return (int)Math.Ceiling(totalRondas / 2.0);
    }

    private static List<List<(int a, int b)>> ArmarJornadas(List<int> ids, int vueltas)
    {
        var rondasBase = GenerarRondasRoundRobin(ids);
        var todasLasRondas = new List<List<(int a, int b)>>();

        for (int v = 0; v < Math.Max(1, vueltas); v++)
            todasLasRondas.AddRange(rondasBase);

        var jornadas = new List<List<(int a, int b)>>();
        for (int i = 0; i < todasLasRondas.Count; i += 2)
        {
            var combinada = new List<(int a, int b)>(todasLasRondas[i]);
            if (i + 1 < todasLasRondas.Count)
                combinada.AddRange(todasLasRondas[i + 1]);

            BalancearPartidosCortos(combinada, ids);

            jornadas.Add(combinada);
        }

        return jornadas;
    }

    private static void BalancearPartidosCortos(List<(int a, int b)> combinada, List<int> idsCategoria)
    {
        if (combinada.Count == 0) return;

        var conteo = idsCategoria.ToDictionary(id => id, id => 0);
        foreach (var (a, b) in combinada)
        {
            conteo[a]++;
            conteo[b]++;
        }

        int objetivo = conteo.Values.Max();
        var cortos = conteo.Where(kv => kv.Value < objetivo).Select(kv => kv.Key).ToList();

        while (cortos.Count >= 2)
        {
            int a = cortos[0];
            int b = cortos[1];
            cortos.RemoveRange(0, 2);
            combinada.Add((a, b));
        }

        if (cortos.Count == 1)
        {
            int corto = cortos[0];
            int companero = idsCategoria.First(id => id != corto);
            combinada.Add((corto, companero));
        }
    }

    public async Task<(bool ok, string mensaje, int totalJornadas)> GenerarSiguienteJornadaAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null || categoria.Formato != "Jornadas")
            return (false, "Esta categoría no es de formato de Jornadas.", 0);

        var config = await db.Configuracions.FirstOrDefaultAsync(c => c.CategoriaId == categoriaId);
        int vueltas = config?.NumeroVueltas ?? 1;
        if (vueltas < 1) vueltas = 1;

        var competidorIds = await db.Competidores
            .Where(c => c.CategoriaId == categoriaId && c.Activo)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync();

        if (competidorIds.Count < 2)
            return (false, "No hay suficientes competidores registrados.", 0);

        var jornadas = ArmarJornadas(competidorIds, vueltas);
        int totalJornadas = jornadas.Count;

        int ultimaJornadaGenerada = await db.Partidos
            .Where(p => p.CategoriaId == categoriaId && p.Fase == "Liga")
            .Select(p => (int?)p.Jornada)
            .MaxAsync() ?? 0;

        if (ultimaJornadaGenerada >= totalJornadas)
            return (false, "Ya se generaron todas las jornadas configuradas.", totalJornadas);

        if (ultimaJornadaGenerada > 0)
        {
            bool faltanPartidos = await db.Partidos.AnyAsync(p =>
                p.CategoriaId == categoriaId && p.Fase == "Liga" &&
                p.Jornada == ultimaJornadaGenerada && p.Estado != "Jugado");

            if (faltanPartidos)
                return (false, $"Todavía hay partidos sin jugar en la Jornada {ultimaJornadaGenerada}. Termínala antes de generar la siguiente.", totalJornadas);
        }

        int siguienteJornada = ultimaJornadaGenerada + 1;
        var partidosJornada = jornadas[siguienteJornada - 1];

        using var transaccion = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            int orden = 0;
            foreach (var (a, b) in partidosJornada)
            {
                db.Partidos.Add(new Partido
                {
                    CategoriaId = categoriaId,
                    GrupoId = null,
                    Jornada = siguienteJornada,
                    CompetidorAid = a,
                    CompetidorBid = b,
                    Fase = "Liga",
                    Estado = "Pendiente",
                    OrdenCola = orden++
                });
            }

            await db.SaveChangesAsync();
            await transaccion.CommitAsync();
        }
        catch
        {
            await transaccion.RollbackAsync();
            return (false, "Ocurrió un error al generar la jornada. Intenta de nuevo.", totalJornadas);
        }

        _eventBus.Notificar();
        return (true, $"Se generó la Jornada {siguienteJornada} de {totalJornadas}.", totalJornadas);
    }

    // =================================================================
    // COLA JUSTA ENTRE CATEGORÍAS/GRUPOS/JORNADAS
    // =================================================================

    public async Task<List<PartidoQueueItem>> ObtenerColaAsync(int torneoId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoriaIds = await db.Categorias
            .Where(c => c.TorneoId == torneoId)
            .Select(c => c.Id)
            .ToListAsync();

        var partidos = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId)
                && (p.Estado == "Pendiente" || p.Estado == "EnCancha")
                && (p.Fase == "Grupos" || p.Fase == "Liga"))
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.Categoria)
            .Include(p => p.Grupo)
            .Include(p => p.Cancha)
            .ToListAsync();

        var colas = partidos
            .Where(p => p.Estado == "Pendiente")
            .GroupBy(p => (p.CategoriaId, p.GrupoId, p.Jornada))
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.OrdenCola).ToList());

        var clavesGrupos = colas.Keys.ToList();
        var ordenados = new List<Partido>();
        bool huboAvance = true;

        while (huboAvance)
        {
            huboAvance = false;
            foreach (var key in clavesGrupos)
            {
                var cola = colas[key];
                if (!cola.Any()) continue;

                var elegido = cola
                    .OrderBy(p => Math.Max(p.CompetidorA.Pg + p.CompetidorA.Pp, p.CompetidorB.Pg + p.CompetidorB.Pp))
                    .ThenBy(p => p.OrdenCola)
                    .First();

                cola.Remove(elegido);
                ordenados.Add(elegido);
                huboAvance = true;
            }
        }

        var enCancha = partidos.Where(p => p.Estado == "EnCancha").OrderBy(p => p.CanchaId).ToList();
        var listaFinal = enCancha.Concat(ordenados).ToList();

        return listaFinal.Select(p => new PartidoQueueItem
        {
            Partido = p,
            NombreA = string.Join(" / ", p.CompetidorA.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)),
            NombreB = string.Join(" / ", p.CompetidorB.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)),
            Categoria = $"{p.Categoria.Nombre} · {p.Categoria.Modalidad} · {p.Categoria.Rama}",
            Grupo = p.Grupo?.Letra,
            Jornada = p.Jornada,
            CanchaNumero = p.Cancha?.Numero
        }).ToList();
    }

    public async Task<List<PartidoQueueItem>> ObtenerPospuestosAsync(int torneoId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneoId).Select(c => c.Id).ToListAsync();

        var partidos = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId) && p.Estado == "Pospuesto")
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.Categoria)
            .Include(p => p.Grupo)
            .ToListAsync();

        return partidos.Select(p => new PartidoQueueItem
        {
            Partido = p,
            NombreA = string.Join(" / ", p.CompetidorA.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)),
            NombreB = string.Join(" / ", p.CompetidorB.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)),
            Categoria = $"{p.Categoria.Nombre} · {p.Categoria.Modalidad} · {p.Categoria.Rama}",
            Grupo = p.Grupo?.Letra,
            Jornada = p.Jornada
        }).ToList();
    }

    // =================================================================
    // REPARTO DE CANCHAS
    // =================================================================

    private async Task<HashSet<int>> ObtenerAtletaIdsDeCompetidor(TorneoContext db, int competidorId)
    {
        return (await db.CompetidorIntegrantes
            .Where(ci => ci.CompetidorId == competidorId)
            .Select(ci => ci.AtletaId)
            .ToListAsync()).ToHashSet();
    }

    private async Task<(int contadorGlobal, Dictionary<int, int> ultimoIndice)> ObtenerEstadoDescansoAsync(TorneoContext db, int torneoId)
    {
        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneoId).Select(c => c.Id).ToListAsync();

        var jugados = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId) && p.Estado == "Jugado")
            .OrderBy(p => p.FechaCaptura)
            .Select(p => new { p.CompetidorAid, p.CompetidorBid })
            .ToListAsync();

        var ultimoIndice = new Dictionary<int, int>();
        for (int i = 0; i < jugados.Count; i++)
        {
            ultimoIndice[jugados[i].CompetidorAid] = i + 1;
            ultimoIndice[jugados[i].CompetidorBid] = i + 1;
        }

        return (jugados.Count, ultimoIndice);
    }

    public async Task AsignarCanchasAsync(int torneoId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var canchas = await db.Canchas.Where(c => c.TorneoId == torneoId).ToListAsync();
        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneoId).Select(c => c.Id).ToListAsync();

        var enCancha = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId) && p.Estado == "EnCancha")
            .ToListAsync();

        var canchasOcupadas = enCancha.Select(p => p.CanchaId!.Value).ToHashSet();
        var canchasLibres = canchas.Select(c => c.Id).Where(id => !canchasOcupadas.Contains(id)).ToList();

        if (!canchasLibres.Any()) return;

        var atletasEnCancha = new HashSet<int>();
        foreach (var p in enCancha)
        {
            atletasEnCancha.UnionWith(await ObtenerAtletaIdsDeCompetidor(db, p.CompetidorAid));
            atletasEnCancha.UnionWith(await ObtenerAtletaIdsDeCompetidor(db, p.CompetidorBid));
        }

        var (contadorGlobal, ultimoIndice) = await ObtenerEstadoDescansoAsync(db, torneoId);

        // La cola se calcula con su propia conexión interna (ObtenerColaAsync ya
        // crea la suya propia), pero solo necesitamos los IDs y el orden aquí.
        var colaItems = await ObtenerColaAsync(torneoId);
        var idsOrdenados = colaItems.Where(i => i.Partido.Estado == "Pendiente").Select(i => i.Partido.Id).ToList();

        var pendientesOrdenados = await db.Partidos
            .Where(p => idsOrdenados.Contains(p.Id))
            .ToListAsync();
        pendientesOrdenados = idsOrdenados.Select(id => pendientesOrdenados.First(p => p.Id == id)).ToList();

        var asignadosPrimeraPasada = new HashSet<int>();

        foreach (var partido in pendientesOrdenados)
        {
            if (!canchasLibres.Any()) break;

            var atletasA = await ObtenerAtletaIdsDeCompetidor(db, partido.CompetidorAid);
            var atletasB = await ObtenerAtletaIdsDeCompetidor(db, partido.CompetidorBid);

            if (atletasA.Overlaps(atletasEnCancha) || atletasB.Overlaps(atletasEnCancha))
                continue;

            bool aOk = !ultimoIndice.TryGetValue(partido.CompetidorAid, out var ia) || (contadorGlobal - ia) >= 4;
            bool bOk = !ultimoIndice.TryGetValue(partido.CompetidorBid, out var ib) || (contadorGlobal - ib) >= 4;

            if (!aOk || !bOk) continue;

            int canchaId = canchasLibres[0];
            canchasLibres.RemoveAt(0);

            partido.CanchaId = canchaId;
            partido.Estado = "EnCancha";

            atletasEnCancha.UnionWith(atletasA);
            atletasEnCancha.UnionWith(atletasB);
            asignadosPrimeraPasada.Add(partido.Id);
        }

        if (canchasLibres.Any())
        {
            foreach (var partido in pendientesOrdenados)
            {
                if (!canchasLibres.Any()) break;
                if (asignadosPrimeraPasada.Contains(partido.Id)) continue;

                var atletasA = await ObtenerAtletaIdsDeCompetidor(db, partido.CompetidorAid);
                var atletasB = await ObtenerAtletaIdsDeCompetidor(db, partido.CompetidorBid);

                if (atletasA.Overlaps(atletasEnCancha) || atletasB.Overlaps(atletasEnCancha))
                    continue;

                int canchaId = canchasLibres[0];
                canchasLibres.RemoveAt(0);

                partido.CanchaId = canchaId;
                partido.Estado = "EnCancha";

                atletasEnCancha.UnionWith(atletasA);
                atletasEnCancha.UnionWith(atletasB);
            }
        }

        await db.SaveChangesAsync();
    }

    // =================================================================
    // POSPONER / REACTIVAR
    // =================================================================

    public async Task PosponerPartidoAsync(int partidoId, int torneoId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var partido = await db.Partidos.FindAsync(partidoId);
        if (partido == null) return;

        partido.CanchaId = null;
        partido.Estado = "Pospuesto";
        await db.SaveChangesAsync();

        _eventBus.Notificar();
    }

    public async Task ReactivarPartidoAsync(int partidoId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var partido = await db.Partidos.FindAsync(partidoId);
        if (partido == null) return;

        int maxOrden = await db.Partidos
            .Where(p => p.CategoriaId == partido.CategoriaId && p.GrupoId == partido.GrupoId && p.Jornada == partido.Jornada)
            .MaxAsync(p => (int?)p.OrdenCola) ?? 0;

        partido.OrdenCola = maxOrden + 1;
        partido.Estado = "Pendiente";
        await db.SaveChangesAsync();

        _eventBus.Notificar();
    }

    // =================================================================
    // CAPTURA DE RESULTADOS
    // =================================================================

    public async Task<(bool ok, string mensaje)> CapturarResultadoAsync(int partidoId, int s1a, int s1b, int s2a, int s2b, int s3a, int s3b, string modalidadSets, int puntosJuego, int puntosLimite, string usuario)
    {
        // Validar cada set jugado
        var setsAValidar = new List<(int a, int b)> { (s1a, s1b) };
        if (modalidadSets == "2 de 3 Sets")
        {
            if (s2a > 0 || s2b > 0) setsAValidar.Add((s2a, s2b));
            if (s3a > 0 || s3b > 0) setsAValidar.Add((s3a, s3b));
        }

        foreach (var (a, b) in setsAValidar)
        {
            int ganador = Math.Max(a, b);
            int perdedor = Math.Min(a, b);
            if (!EsMarcadorValido(ganador, perdedor, puntosJuego, puntosLimite))
            {
                return (false, $"Marcador inválido: {a}-{b}. Debe cumplir las reglas de puntos y diferencia mínima.");
            }
        }

        int torneoId;
        using (var db = await _factory.CreateDbContextAsync())
        {
            var partido = await db.Partidos
                .Include(p => p.CompetidorA)
                .Include(p => p.CompetidorB)
                .FirstAsync(p => p.Id == partidoId);

            if (partido.Estado == "Jugado")
                return (false, "Este partido ya tiene resultado capturado.");

            var (setsA, setsB, setsJugados) = CalcularSetsJugados(s1a, s1b, s2a, s2b, s3a, s3b, modalidadSets);

            foreach (var (num, pa, pb) in setsJugados)
            {
                db.SetsPartidos.Add(new SetsPartido { PartidoId = partido.Id, NumeroSet = num, PuntosA = pa, PuntosB = pb });
            }

            var compA = partido.CompetidorA;
            var compB = partido.CompetidorB;

            if (setsA > setsB) { compA.Pg++; compB.Pp++; partido.GanadorId = compA.Id; }
            else { compB.Pg++; compA.Pp++; partido.GanadorId = compB.Id; }

            compA.Sg += setsA; compA.Sp += setsB;
            compB.Sg += setsB; compB.Sp += setsA;

            int puntosA = setsJugados.Sum(s => s.pa);
            int puntosB = setsJugados.Sum(s => s.pb);
            compA.Pf += puntosA; compA.Pc += puntosB;
            compB.Pf += puntosB; compB.Pc += puntosA;

            partido.Estado = "Jugado";
            partido.FechaCaptura = DateTime.Now;
            partido.CapturadoPor = usuario;

            var categoria = await db.Categorias.FindAsync(partido.CategoriaId);
            torneoId = categoria!.TorneoId;

            await db.SaveChangesAsync();
        }

        _eventBus.Notificar();
        return (true, "Resultado guardado.");
    }

    private static (int setsA, int setsB, List<(int num, int pa, int pb)> setsJugados) CalcularSetsJugados(int s1a, int s1b, int s2a, int s2b, int s3a, int s3b, string modalidadSets)
    {
        int setsA = 0, setsB = 0;
        var setsJugados = new List<(int num, int pa, int pb)>();

        if (modalidadSets == "1 Set")
        {
            if (s1a > s1b) setsA = 1; else if (s1b > s1a) setsB = 1;
            setsJugados.Add((1, s1a, s1b));
        }
        else
        {
            if (s1a > s1b) setsA++; else if (s1b > s1a) setsB++;
            setsJugados.Add((1, s1a, s1b));

            if (s2a > 0 || s2b > 0)
            {
                if (s2a > s2b) setsA++; else if (s2b > s2a) setsB++;
                setsJugados.Add((2, s2a, s2b));
            }

            if (setsA < 2 && setsB < 2 && (s3a > 0 || s3b > 0))
            {
                if (s3a > s3b) setsA++; else if (s3b > s3a) setsB++;
                setsJugados.Add((3, s3a, s3b));
            }
        }

        return (setsA, setsB, setsJugados);
    }

    //Solo se puede corregir el ultimo resultado capturado ya sea de Grupos o Jornadas

    public async Task<(bool ok, string mensaje)> CorregirResultadoAsync(int partidoId, int s1a, int s1b, int s2a, int s2b, int s3a, int s3b, string modalidadSets, int puntosJuego, int puntosLimite, string usuario)
    {
        var setsAValidar = new List<(int a, int b)> { (s1a, s1b) };
        if (modalidadSets == "2 de 3 Sets")
        {
            if (s2a > 0 || s2b > 0) setsAValidar.Add((s2a, s2b));
            if (s3a > 0 || s3b > 0) setsAValidar.Add((s3a, s3b));
        }

        foreach (var (a, b) in setsAValidar)
        {
            int ganador = Math.Max(a, b);
            int perdedor = Math.Min(a, b);
            if (!EsMarcadorValido(ganador, perdedor, puntosJuego, puntosLimite))
                return (false, $"Marcador inválido: {a}-{b}. Debe cumplir las reglas de puntos y diferencia mínima.");
        }

        using var db = await _factory.CreateDbContextAsync();

        var partido = await db.Partidos
            .Include(p => p.CompetidorA)
            .Include(p => p.CompetidorB)
            .Include(p => p.SetsPartidos)
            .FirstOrDefaultAsync(p => p.Id == partidoId);

        if (partido == null) return (false, "Partido no encontrado.");
        if (partido.Estado != "Jugado") return (false, "Este partido todavía no tiene un resultado capturado.");
        if (partido.Fase != "Grupos" && partido.Fase != "Liga")
            return (false, "Solo se puede corregir los partios de la fase de grupos o jornadas.");

        var categoria = await db.Categorias.FindAsync(partido.CategoriaId);
        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == categoria!.TorneoId).Select(c => c.Id).ToListAsync();

        var ultimoId = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId) && p.Estado == "Jugado" && (p.Fase == "Grupos" || p.Fase == "Liga"))
            .OrderByDescending(p => p.FechaCaptura)
            .Take(4)
            .Select(p => p.Id)
            .FirstOrDefaultAsync();

        if (ultimoId != partido.Id)
            return (false, "Ya se capturaron otros resultados después de este. Solo se puede corregir el último.");

        var compA = partido.CompetidorA;
        var compB = partido.CompetidorB;

        int setsAViejo = partido.SetsPartidos.Count(s => s.PuntosA > s.PuntosB);
        int setsBViejo = partido.SetsPartidos.Count(s => s.PuntosB > s.PuntosA);
        int puntosAViejo = partido.SetsPartidos.Sum(s => s.PuntosA);
        int puntosBViejo = partido.SetsPartidos.Sum(s => s.PuntosB);

        if (partido.GanadorId == compA.Id) { compA.Pg--; compB.Pp--; }
        else { compB.Pg--; compA.Pp--; }

        compA.Sg -= setsAViejo; compA.Sp -= setsBViejo;
        compB.Sg -= setsBViejo; compB.Sp -= setsAViejo;
        compA.Pf -= puntosAViejo; compA.Pc -= puntosBViejo;
        compB.Pf -= puntosBViejo; compB.Pc -= puntosAViejo;

        string marcadorAnterior = string.Join(", ", partido.SetsPartidos.OrderBy(s => s.NumeroSet).Select(s => $"{s.PuntosA}-{s.PuntosB}"));

        db.SetsPartidos.RemoveRange(partido.SetsPartidos);

        var (setsA, setsB, setsJugados) = CalcularSetsJugados(s1a, s1b, s2a, s2b, s3a, s3b, modalidadSets);

        foreach (var (num, pa, pb) in setsJugados)
            db.SetsPartidos.Add(new SetsPartido { PartidoId = partido.Id, NumeroSet = num, PuntosA = pa, PuntosB = pb });

        if (setsA > setsB) { compA.Pg++; compB.Pp++; partido.GanadorId = compA.Id; }
        else { compB.Pg++; compA.Pp++; partido.GanadorId = compB.Id; }

        compA.Sg += setsA; compA.Sp += setsB;
        compB.Sg += setsB; compB.Sp += setsA;

        int puntosANuevo = setsJugados.Sum(s => s.pa);
        int puntosBNuevo = setsJugados.Sum(s => s.pb);
        compA.Pf += puntosANuevo; compA.Pc += puntosBNuevo;
        compB.Pf += puntosBNuevo; compB.Pc += puntosANuevo;

        partido.CorregidoPor = usuario;
        partido.FechaCaptura = DateTime.Now;
        partido.MarcadorAnterior = marcadorAnterior;

        await db.SaveChangesAsync();

        _eventBus.Notificar();
        return (true, "Resultado corregido.");
    }

    public async Task<List<PartidoQueueItem>> ObtenerUltimosCapturadosAsync(int torneoId, int cantidad = 4)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneoId).Select(c => c.Id).ToListAsync();

        var partidos = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId) && p.Estado == "Jugado" && (p.Fase == "Grupos" || p.Fase == "Liga"))
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.Categoria)
            .Include(p => p.Grupo)
            .Include(p => p.SetsPartidos)
            .OrderByDescending(p => p.FechaCaptura)
            .Take(cantidad)
            .ToListAsync();

        return partidos.Select(partido => new PartidoQueueItem
        {
            Partido = partido,
            NombreA = string.Join(" / ", partido.CompetidorA.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)),
            NombreB = string.Join(" / ", partido.CompetidorB.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)),
            Categoria = $"{partido.Categoria.Nombre} · {partido.Categoria.Modalidad} · {partido.Categoria.Rama}",
            Grupo = partido.Grupo?.Letra,
            Jornada = partido.Jornada
        }).ToList();
    }

    public async Task<(bool ok, string mensaje)> ActivarPartidoAsync(int partidoId, int torneoId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var partido = await db.Partidos.FindAsync(partidoId);
        if (partido == null) return (false, "Partido no encontrado.");
        if (partido.Estado != "Pendiente") return (false, "El partido no está pendiente.");

        var canchas = await db.Canchas.Where(c => c.TorneoId == torneoId).ToListAsync();
        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneoId).Select(c => c.Id).ToListAsync();

        var enCancha = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId) && p.Estado == "EnCancha")
            .ToListAsync();

        var canchasOcupadas = enCancha.Select(p => p.CanchaId!.Value).ToHashSet();
        int? canchaLibreId = canchas.Select(c => (int?)c.Id).FirstOrDefault(id => !canchasOcupadas.Contains(id!.Value));

        if (canchaLibreId == null) return (false, "No hay canchas libres en este momento.");

        var atletasEnCancha = new HashSet<int>();
        foreach (var p in enCancha)
        {
            atletasEnCancha.UnionWith(await ObtenerAtletaIdsDeCompetidor(db, p.CompetidorAid));
            atletasEnCancha.UnionWith(await ObtenerAtletaIdsDeCompetidor(db, p.CompetidorBid));
        }

        var atletasA = await ObtenerAtletaIdsDeCompetidor(db, partido.CompetidorAid);
        var atletasB = await ObtenerAtletaIdsDeCompetidor(db, partido.CompetidorBid);

        if (atletasA.Overlaps(atletasEnCancha) || atletasB.Overlaps(atletasEnCancha))
            return (false, "Un atleta de este partido ya está jugando en otra cancha ahora mismo.");

        partido.CanchaId = canchaLibreId.Value;
        partido.Estado = "EnCancha";
        await db.SaveChangesAsync();

        _eventBus.Notificar();
        return (true, "Partido activado.");
    }

    public static bool EsMarcadorValido(int ganador, int perdedor, int puntosJuego, int puntosLimite)
    {
        if (ganador <= perdedor || perdedor < 0) return false;

        if (puntosJuego >= puntosLimite)
        {
            return ganador == puntosLimite && perdedor < puntosLimite;
        }

        // Victoria normal, sin deuce
        if (ganador == puntosJuego && perdedor <= puntosJuego - 2)
            return true;

        // Victoria en deuce (margen exacto de 2), antes del límite
        if (ganador > puntosJuego && ganador < puntosLimite && ganador - perdedor == 2)
            return true;

        // Victoria al llegar al límite (margen de 1 o 2)
        if (ganador == puntosLimite && (perdedor == puntosLimite - 1 || perdedor == puntosLimite - 2))
            return true;

        return false;
    }

    //Ganar por Default Y Baja de Competidor

    private static void ResolverComoDefault(Partido partido, int ganadorId)
    {
        var compA = partido.CompetidorA;
        var compB = partido.CompetidorB;

        if (ganadorId == compA.Id) { compA.Pg++; compB.Pp++; }
        else { compB.Pg++; compA.Pp++; }

        partido.GanadorId = ganadorId;
        partido.Estado = "Jugado";
        partido.EsDefault = true;
        partido.FechaCaptura = DateTime.Now;
    }

    public async Task<(bool ok, string mensaje)> MarcarPorDefaultAsync(int partidoId, int ganadorId, string usuario)
    {
        using var db = await _factory.CreateDbContextAsync();

        var partido = await db.Partidos
            .Include(p => p.CompetidorA)
            .Include(p => p.CompetidorB)
            .FirstOrDefaultAsync(p => p.Id == partidoId);

        if (partido == null) return (false, "Partido no encontrado.");
        if (partido.Estado == "Jugado") return (false, "Este partido ya tiene resultado.");
        if (ganadorId != partido.CompetidorAid && ganadorId != partido.CompetidorBid)
            return (false, "El ganador debe ser uno de los dos competidores del partido.");

        ResolverComoDefault(partido, ganadorId);
        partido.CapturadoPor = usuario;
        await db.SaveChangesAsync();

        if (partido.Fase != "Grupos" && partido.Fase != "Liga")
            await _faseFinalSvc.AvanzarRondaPublicoAsync(partido.CategoriaId, partido.Fase, partido.Banda);

        _eventBus.Notificar();
        return (true, "Marcado como ganado por default.");
    }

    //Dar de baja a un competidor: solo los partidos siguientes se dan como perdidos, los anteriores no se tocan

    public async Task<(bool ok, string mensaje, int partidosAfectados)> DarDeBajaCompetidorAsync(int competidorId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var competidor = await db.Competidores.FindAsync(competidorId);
        if (competidor == null) return (false, "Competidor no encontrado.", 0);
        if (!competidor.Activo) return (false, "Este competidor ya estaba dado de baja.", 0);

        competidor.Activo = false;

        var pendientes = await db.Partidos
            .Include(p => p.CompetidorA)
            .Include(p => p.CompetidorB)
            .Where(p => (p.CompetidorAid == competidorId || p.CompetidorBid == competidorId)
                    && (p.Estado == "Pendiente" || p.Estado == "EnCancha"))
            .ToListAsync();

        var rondasBracketAfectadas = new HashSet<(int categoriaId, string fase, int? banda)>();

        foreach (var partido in pendientes)
        {
            int ganadorId = partido.CompetidorAid == competidorId ? partido.CompetidorBid : partido.CompetidorAid;
            ResolverComoDefault(partido, ganadorId);

            if (partido.Fase != "Grupos" && partido.Fase != "Liga")
                rondasBracketAfectadas.Add((partido.CategoriaId, partido.Fase, partido.Banda));
        }

        await db.SaveChangesAsync();

        foreach (var (categoriaId, fase, banda) in rondasBracketAfectadas)
            await _faseFinalSvc.AvanzarRondaPublicoAsync(categoriaId, fase, banda);

        string detalle = pendientes.Count > 0
            ? $"Se marcaron {pendientes.Count} partido(s) pendientes como ganados por default para su(s) rival(es)."
            : " No tenía partidos pendientes.";

        _eventBus.Notificar();
        return (true, $"Competidor dado de baja.{detalle} Los partidos ya jugados no se modifican.", pendientes.Count);
    }

    // =================================================================
    // PUNTO POR PUNTO (solo Árbitro)
    // =================================================================

    private static (int sirveId, int recibeId) CalcularServicioTrasPuntos(
        List<int> integrantesA, List<int> integrantesB,
        int primerSirveAtletaId, int primerRecibeAtletaId,
        IEnumerable<string> secuenciaGanadores)
    {
        bool primerSirveEsA = integrantesA.Contains(primerSirveAtletaId);

        int derA, izqA, derB, izqB;

        if (primerSirveEsA)
        {
            derA = primerSirveAtletaId;
            izqA = Companero(integrantesA, primerSirveAtletaId);
            derB = primerRecibeAtletaId;
            izqB = Companero(integrantesB, primerRecibeAtletaId);
        }
        else
        {
            derB = primerSirveAtletaId;
            izqB = Companero(integrantesB, primerSirveAtletaId);
            derA = primerRecibeAtletaId;
            izqA = Companero(integrantesA, primerRecibeAtletaId);
        }

        bool sirveEsA = primerSirveEsA;
        int puntosA = 0, puntosB = 0;

        foreach (var ganador in secuenciaGanadores)
        {
            bool ganoA = ganador == "A";
            if (ganoA) puntosA++; else puntosB++;

            bool sirvienteGano = (sirveEsA && ganoA) || (!sirveEsA && !ganoA);

            if (sirvienteGano)
            {
                if (sirveEsA) (derA, izqA) = (izqA, derA);
                else (derB, izqB) = (izqB, derB);
            }
            else
            {
                sirveEsA = !sirveEsA;
            }
        }

        int scoreEquipoSirve = sirveEsA ? puntosA : puntosB;
        bool esCajaPar = scoreEquipoSirve % 2 == 0;

        int sirveId = sirveEsA
            ? (esCajaPar ? derA : izqA)
            : (esCajaPar ? derB : izqB);

        int recibeId = sirveEsA
            ? (esCajaPar ? derB : izqB)
            : (esCajaPar ? derA : izqA);

        return (sirveId, recibeId);
    }

    private static int Companero(List<int> integrantes, int atletaId)
    {
        if (integrantes.Count <= 1) return atletaId;
        return integrantes.First(id => id != atletaId);
    }

    public async Task<(bool ok, string mensaje)> RegistrarPuntoAsync(int partidoId, int numeroSet, string equipoAnoto, int puntosJuego, int puntosLimite, int? primerSirveAtletaId = null, int? primerRecibeAtletaId = null)
    {
        if (equipoAnoto != "A" && equipoAnoto != "B")
            return (false, "Equipo inválido.");

        using var db = await _factory.CreateDbContextAsync();

        var partido = await db.Partidos
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes)
            .FirstOrDefaultAsync(p => p.Id == partidoId);

        if (partido == null) return (false, "Partido no encontrado.");

        var puntos = await db.PuntosPartido
            .Where(pp => pp.PartidoId == partidoId && pp.NumeroSet == numeroSet)
            .OrderBy(pp => pp.NumeroPunto)
            .ToListAsync();

        var integrantesA = partido.CompetidorA.CompetidorIntegrantes.Select(ci => ci.AtletaId).ToList();
        var integrantesB = partido.CompetidorB.CompetidorIntegrantes.Select(ci => ci.AtletaId).ToList();

        int puntosAPrevios = puntos.Count > 0 ? puntos[^1].PuntosA : 0;
        int puntosBPrevios = puntos.Count > 0 ? puntos[^1].PuntosB : 0;

        if (puntos.Count > 0)
        {
            int ganadorActual = Math.Max(puntosAPrevios, puntosBPrevios);
            int perdedorActual = Math.Min(puntosAPrevios, puntosBPrevios);
            if (EsMarcadorValido(ganadorActual, perdedorActual, puntosJuego, puntosLimite))
                return (false, "Este set ya está ganado. Confirma el resultado o deshaz el último punto para seguir capturando.");
        }

        int sirveId, recibeId;

        if (puntos.Count == 0)
        {
            if (primerSirveAtletaId == null || primerRecibeAtletaId == null)
                return (false, "Indica quién saca y quién recibe para iniciar el set.");

            sirveId = primerSirveAtletaId.Value;
            recibeId = primerRecibeAtletaId.Value;
        }
        else
        {
            (sirveId, recibeId) = CalcularServicioTrasPuntos(
                integrantesA, integrantesB,
                puntos[0].SirveAtletaId, puntos[0].RecibeAtletaId,
                puntos.Select(puntos => puntos.EquipoAnoto));
        }

        db.PuntosPartido.Add(new PuntoPartido
        {
            PartidoId = partidoId,
            NumeroSet = numeroSet,
            NumeroPunto = puntos.Count + 1,
            EquipoAnoto = equipoAnoto,
            PuntosA = puntosAPrevios + (equipoAnoto == "A" ? 1 : 0),
            PuntosB = puntosBPrevios + (equipoAnoto == "B" ? 1 : 0),
            SirveAtletaId = sirveId,
            RecibeAtletaId = recibeId
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return (false, "Alguien más ya registró un punto en este set justo antes que tú. Seactualizó el marcador, revísalo antes de seguir.");
        }
        
        _eventBus.Notificar();

        return (true, "Punto registrado.");
    }

    public async Task<(bool ok, string mensaje)> DeshacerUltimoPuntoAsync(int partidoId, int numeroSet)
    {
        using var db = await _factory.CreateDbContextAsync();

        var ultimo = await db.PuntosPartido
            .Where(pp => pp.PartidoId == partidoId && pp.NumeroSet == numeroSet)
            .OrderByDescending(pp => pp.NumeroPunto)
            .FirstOrDefaultAsync();

        if (ultimo == null) return (false, "No hay puntos que deshacer en este set.");

        db.PuntosPartido.Remove(ultimo);
        await db.SaveChangesAsync();
        _eventBus.Notificar();

        return (true, "Último punto deshecho.");
    }

    public async Task<EstadoSetPuntoAPunto> ObtenerEstadoSetAsync(int partidoId, int numeroSet)
    {
        using var db = await _factory.CreateDbContextAsync();

        var partido = await db.Partidos
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .FirstAsync(p => p.Id == partidoId);

        var puntos = await db.PuntosPartido
            .Where(pp => pp.PartidoId == partidoId && pp.NumeroSet == numeroSet)
            .OrderBy(pp => pp.NumeroPunto)
            .Include(pp => pp.SirveAtleta)
            .Include(pp => pp.RecibeAtleta)
            .ToListAsync();

        var estado = new EstadoSetPuntoAPunto
        {
            Puntos = puntos,
            PuntosA = puntos.Count > 0 ? puntos[^1].PuntosA : 0,
            PuntosB = puntos.Count > 0 ? puntos[^1].PuntosB : 0
        };

        if (puntos.Count > 0)
        {
            var integrantesA = partido.CompetidorA.CompetidorIntegrantes.Select(ci => ci.AtletaId).ToList();
            var integrantesB = partido.CompetidorB.CompetidorIntegrantes.Select(ci => ci.AtletaId).ToList();

            var (s, r) = CalcularServicioTrasPuntos(
                integrantesA, integrantesB,
                puntos[0].SirveAtletaId, puntos[0].RecibeAtletaId,
                puntos.Select(p => p.EquipoAnoto));

            estado.ProximoSirveAtletaId = s;
            estado.ProximoRecibeAtletaId = r;
            estado.ProximoSirveNombre = BuscarNombreAtleta(partido, s);
            estado.ProximoRecibeNombre = BuscarNombreAtleta(partido, r);
        }

        return estado;
    }

    private static string BuscarNombreAtleta(Partido partido, int atletaId)
    {
        var integrante = partido.CompetidorA.CompetidorIntegrantes.FirstOrDefault(ci => ci.AtletaId == atletaId)
                ?? partido.CompetidorB.CompetidorIntegrantes.FirstOrDefault(ci => ci.AtletaId == atletaId);
        return integrante?.Atleta.Nombre ?? "";
    }

    public class EstadoSetPuntoAPunto
    {
        public List<PuntoPartido> Puntos { get; set; } = new();
        public int PuntosA { get; set; }
        public int PuntosB { get; set; }
        public int? ProximoSirveAtletaId { get; set; }
        public int? ProximoRecibeAtletaId { get; set; }
        public string ProximoSirveNombre { get; set; } = "";
        public string ProximoRecibeNombre { get; set; } = "";
    }
}