using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ProyectoArbitraje.Components.Pages;
using ProyectoArbitraje.Data;
using ProyectoArbitraje.Models;

namespace ProyectoArbitraje.Services;

public class LlaveItem
{
    public string Fase { get; set; } = "";
    public int Posicion { get; set; }
    public int? Banda { get; set; }
    public Partido? Partido { get; set; }
    public Competidore? CompetidorConBye { get; set; }
}

public class SlotEliminatoria
{
    public int Posicion { get; set; }
    public int? CompetidorId { get; set; }
    public string Nombre { get; set; } = "";
    public bool EsSembrado { get; set; }
    public int? NumeroSeed { get; set; }
}

public class FaseFinalService
{
    private readonly IDbContextFactory<TorneoContext> _factory;
    private readonly TorneoEventBus _eventBus;
    private readonly ILogger<FaseFinalService> _logger;

    public FaseFinalService(IDbContextFactory<TorneoContext> factory, TorneoEventBus eventBus, ILogger<FaseFinalService> logger)
    {
        _factory = factory;
        _eventBus = eventBus;
        _logger = logger;
    }

    private static readonly Dictionary<string, string> SiguienteFase = new()
    {
        ["Dieciseisavos"] = "Octavos",
        ["Octavos"] = "Cuartos",
        ["Cuartos"] = "Semifinal",
        ["Semifinal"] = "Final",
    };

    private static readonly Dictionary<string, int> OrdenFase = new()
    {
        ["Dieciseisavos"] = 0,
        ["Octavos"] = 1,
        ["Cuartos"] = 2,
        ["Semifinal"] = 3,
        ["Final"] = 4,
    };

    // =================================================================
    // GENERAR EL BRACKET
    // =================================================================

    public async Task<(bool ok, string mensaje)> GenerarFaseFinalAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return (false, "Categoría no encontrada.");

        if (categoria.Formato == "GruposFaseFinal")
        {
            int numGrupos = await db.Grupos.CountAsync(g => g.CategoriaId == categoriaId);
            if (numGrupos <= 1)
                return (false, "Con un solo grupo no se genera fase final. La clasificación final es la tabla de posiciones.");
        }

        if (categoria.Formato == "Eliminatoria")
            return (false, "Para esta categoría usa el sorteo con sembrados.");

        //Evita que dos personas casi al mismo tiempo generen el bracket duplicado para la misma categoría.

        using var transaccion = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        int n;
        int bracketSize;
        string fase;
        int partidosReales;
        try
        {
            bool yaExiste = await db.Partidos.AnyAsync(p => p.CategoriaId == categoriaId && p.GrupoId == null)
            || await db.ByesFaseFinal.AnyAsync(b => b.CategoriaId == categoriaId);

            if (yaExiste)
            {
                await transaccion.RollbackAsync();
                return (false, "Esta categoría ya tiene fase final generada.");
            }

            bool hayPendientes = await db.Partidos.AnyAsync(p => p.CategoriaId == categoriaId
                && (p.Fase == "Grupos" || p.Fase == "Liga")
                && p.Estado != "Jugado");

            if (hayPendientes)
            {
                await transaccion.RollbackAsync();
                return (false, "Todavía hay partidos de grupos sin jugar. Termina la fase de grupos antes de generar la fase final.");
            }

            var calificados = await ObtenerClasificadosAsync(db, categoriaId, categoria);
            if (calificados.Count < 2)
            {
                await transaccion.RollbackAsync();
                return (false, "No hay suficientes clasificados.");
            }

            n = calificados.Count;
            bracketSize = 2;
            while (bracketSize < n) bracketSize *= 2;

            await AsignarRankingPrevioAsync(db, calificados, categoria);

            var rnd = new Random();
            var ordenSeed = (await db.Competidores.Where(c => calificados.Contains(c.Id)).ToListAsync())
                .OrderByDescending(c => c.RankingPrevio ?? 0)
                .ThenBy(c => rnd.Next())
                .Select(c => c.Id)
                .ToList();

            var ordenPosiciones = GenerarOrdenSeeds(bracketSize);
            var slot = new int?[bracketSize];
            for (int i = 0; i < bracketSize; i++)
            {
                int seed = ordenPosiciones[i];
                slot[i] = seed <= n ? ordenSeed[seed - 1] : (int?)null;
            }

            fase = FaseSegunTamano(bracketSize);
            partidosReales = await PersistirLlaveAsync(db, categoriaId, slot, fase);

            await transaccion.CommitAsync();
        }
        catch
        {
            await transaccion.RollbackAsync();
            return (false, "Ocurrió un error al generar el bracket. Intenta de nuevo.");
        }

        _eventBus.Notificar();
        
        if (partidosReales == 0)
        {
            await IntentarAvanzarRondaAsync(categoriaId, fase);
            return (true, $"Bracket generado: {fase} con {n} competidores (todos avanzan por bye).");
        }

        int byes = bracketSize - n;
        string detalleByes = byes > 0 ? $", {byes} con bye" : "";
        return (true, $"Bracket generado: {fase} con {n} competidores{detalleByes}.");
    }

    private static string FaseSegunTamano(int bracketSize) => bracketSize switch
    {
        2 => "Final",
        4 => "Semifinal",
        8 => "Cuartos",
        16 => "Octavos",
        32 => "Dieciseisavos",
        _ => "Cuartos"
    };

    private async Task<int> PersistirLlaveAsync(TorneoContext db, int categoriaId, int?[] slot, string fase, int? banda = null)
    {
        int posicion = 0;
        int partidosReales = 0;
        for (int i = 0; i < slot.Length; i += 2)
        {
            var a = slot[i];
            var b = slot[i + 1];

            if (a.HasValue && b.HasValue)
            {
                db.Partidos.Add(new Partido
                {
                    CategoriaId = categoriaId,
                    GrupoId = null,
                    Banda = banda,
                    CompetidorAid = a.Value,
                    CompetidorBid = b.Value,
                    Fase = fase,
                    Estado = "Pendiente",
                    OrdenCola = posicion
                });
                partidosReales++;
            }
            else
            {
                int competidorConBye = (a ?? b)!.Value;
                db.ByesFaseFinal.Add(new ByeFaseFinal
                {
                    CategoriaId = categoriaId,
                    Fase = fase,
                    Banda = banda,
                    CompetidorId = competidorConBye,
                    PosicionLlave = posicion
                });
            }
            posicion++;
        }

        await db.SaveChangesAsync();
        return partidosReales;
    }

    // =================================================================
    // ELIMINATORIA (solo los sembrados se quedan fijos, los demas se sortean)
    // =================================================================

    public async Task<(bool ok, string mensaje, List<SlotEliminatoria>? propuesta)> PrepararEliminatoriaAsync(int categoriaId, int cantidadSembrados)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return (false, "Categoría no encontrada.", null);
        if (categoria.Formato != "Eliminatoria") return (false, "Esta opción es solo para categorías de formato Eliminatoria.", null);

        bool yaExiste = await db.Partidos.AnyAsync(p => p.CategoriaId == categoriaId && p.GrupoId == null)
                        || await db.ByesFaseFinal.AnyAsync(b => b.CategoriaId == categoriaId);
        if (yaExiste) return (false, "Esta categoría ya tiene fase final generada.", null);

        var calificados = await ObtenerClasificadosAsync(db, categoriaId, categoria);
        if (calificados.Count < 2) return (false, "No hay suficientes competidores registrados.", null);

        int n = calificados.Count;
        if (cantidadSembrados < 0 || cantidadSembrados > n)
            return (false, $"La cantidad de sembrados debe estar entre 0 y {n}.", null);

        int bracketSize = 2;
        while (bracketSize < n) bracketSize *= 2;

        await AsignarRankingPrevioAsync(db, calificados, categoria);

        var competidoresOrdenados = (await db.Competidores
                .Where(c => calificados.Contains(c.Id))
                .Include(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
                .ToListAsync())
            .OrderByDescending(c => c.RankingPrevio ?? 0)
            .ToList();

        var sembrados = competidoresOrdenados.Take(cantidadSembrados).ToList();
        var resto = competidoresOrdenados.Skip(cantidadSembrados).Select(c => (int?)c.Id).ToList();

        var ordenPosiciones = GenerarOrdenSeeds(bracketSize);
        var rnd = new Random();

        // slot[i] queda fijo solo si esa posición corresponde a un sembrado
        // real (seed <= cantidadSembrados). El resto de posiciones se llena
        // más abajo con la bolsa al azar.
        var slot = new int?[bracketSize];
        var (posicionesLibres, seedPorPosicion) = ColocarSembradosPorBanda(slot, ordenPosiciones, sembrados, cantidadSembrados, rnd);
      
        // Bolsa con el resto de competidores + los huecos de bye que falten,
        // mezclada al azar -- esto es lo que cambia cada vez que se sortea.
        var bolsa = new List<int?>(resto);
        while (bolsa.Count < posicionesLibres.Count) bolsa.Add(null);
        bolsa = bolsa.OrderBy(_ => rnd.Next()).ToList();

        for (int i = 0; i < posicionesLibres.Count; i++)
            slot[posicionesLibres[i]] = bolsa[i];

        var nombresPorId = competidoresOrdenados.ToDictionary(
            c => c.Id,
            c => string.Join(" / ", c.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)));

        var idsSembrados = sembrados.Select(s => s.Id).ToHashSet();

        var propuesta = new List<SlotEliminatoria>();
        for (int i = 0; i < bracketSize; i++)
        {
            propuesta.Add(new SlotEliminatoria
            {
                Posicion = i,
                CompetidorId = slot[i],
                Nombre = slot[i].HasValue ? nombresPorId[slot[i]!.Value] : "BYE",
                EsSembrado = slot[i].HasValue && idsSembrados.Contains(slot[i]!.Value),
                NumeroSeed = seedPorPosicion.TryGetValue(i, out int sn) ? sn : (int?)null
            });
        }

        return (true, "Propuesta generada. Puedes sortear de nuevo o confirmar.", propuesta);
    }

    public async Task<bool> HayPuntosDeRankingAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return false;

        var calificados = await ObtenerClasificadosAsync(db, categoriaId, categoria);
        if (!calificados.Any()) return false;

        await AsignarRankingPrevioAsync(db, calificados, categoria);
        await db.SaveChangesAsync();

        return await db.Competidores
            .Where(c => calificados.Contains(c.Id))
            .AnyAsync(c => c.RankingPrevio != null && c.RankingPrevio > 0);
    }

    public async Task<List<(int Id, string Nombre, int? AnioNacimiento, string Municipio)>> ObtenerCompetidoresParaSembradoAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();
        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return new();

        var calificados = await ObtenerClasificadosAsync(db, categoriaId, categoria);

        var competidores = await db.Competidores
            .Where(c => calificados.Contains(c.Id))
            .Include(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta).ThenInclude(a => a.Municipio)
            .ToListAsync();

        return competidores
            .Select(c => (
                c.Id,
                string.Join(" / ", c.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)),
                c.CompetidorIntegrantes.Select(ci => ci.Atleta.AnioNacimiento).FirstOrDefault(),
                string.Join(" / ", c.CompetidorIntegrantes.Select(ci => ci.Atleta.Municipio.Nombre).Distinct())
            ))
            .OrderBy(c => c.Item2)
            .ToList();
    }

    public async Task<(bool ok, string mensaje, List<SlotEliminatoria>? propuesta)> PrepararEliminatoriaManualAsync(int categoriaId, List<int> competidorIdsSembradosEnOrden)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return (false, "Categoria no encontrada.", null);
        if (categoria.Formato != "Eliminatoria") return (false, "Esta opción es solo para categorías de formato Eliminatoria.", null);

        bool yaExiste = await db.Partidos.AnyAsync(p => p.CategoriaId == categoriaId && p.GrupoId == null)
                        || await db.ByesFaseFinal.AnyAsync(b => b.CategoriaId == categoriaId);
        if (yaExiste) return (false, "Esta categoría ya tiene fase final generada.", null);

        var calificados = await ObtenerClasificadosAsync(db, categoriaId, categoria);
        if (calificados.Count < 2) return (false, "No hay suficientes competidores registrados.", null);

        int n = calificados.Count;
        int cantidadSembrados = competidorIdsSembradosEnOrden.Count;
        if (cantidadSembrados < 0 || cantidadSembrados > n)
            return (false, $"La cantidad de sembrados debe estar entre 0 y {n}.", null);

        if (competidorIdsSembradosEnOrden.Any(id => !calificados.Contains(id)))
            return (false, "Uno de los sembrados elegidos ya no está activo en esta categoría.", null);

        int bracketSize = 2;
        while (bracketSize < n) bracketSize *= 2;

        // Asigna un valor sintético de "ranking previo" según el orden elegido a mano
        // (seed 1 = valor más alto), ya que no hay puntos de ranking reales que usar.
        var todos = await db.Competidores.Where(c => calificados.Contains(c.Id)).ToListAsync();
        foreach (var c in todos) c.RankingPrevio = 0;

        int valor = cantidadSembrados + 1000;
        foreach (var id in competidorIdsSembradosEnOrden)
        {
            var comp = todos.First(c => c.Id == id);
            comp.RankingPrevio = valor--;
        }
        await db.SaveChangesAsync();

        var competidoresOrdenados = (await db.Competidores
                .Where(c => calificados.Contains(c.Id))
                .Include(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
                .ToListAsync())
            .OrderByDescending(c => c.RankingPrevio ?? 0)
            .ToList();

        var sembrados = competidoresOrdenados.Take(cantidadSembrados).ToList();
        var resto = competidoresOrdenados.Skip(cantidadSembrados).Select(c => (int?)c.Id).ToList();

        var ordenPosiciones = GenerarOrdenSeeds(bracketSize);
        var rnd = new Random();

        var slot = new int?[bracketSize];
        var (posicionesLibres, seedPorPosicion) = ColocarSembradosPorBanda(slot, ordenPosiciones, sembrados, cantidadSembrados, rnd);

        var bolsa = new List<int?>(resto);
        while (bolsa.Count < posicionesLibres.Count) bolsa.Add(null);
        bolsa = bolsa.OrderBy(_ => rnd.Next()).ToList();

        for (int i = 0; i < posicionesLibres.Count; i++)
            slot[posicionesLibres[i]] = bolsa[i];

        var nombresPorId = competidoresOrdenados.ToDictionary(
            c => c.Id,
            c => string.Join(" / ", c.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre)));

        var idsSembrados = sembrados.Select(s => s.Id).ToHashSet();

        var propuesta = new List<SlotEliminatoria>();
        for (int i = 0; i < bracketSize; i++)
        {
            propuesta.Add(new SlotEliminatoria
            {
                Posicion = i,
                CompetidorId = slot[i],
                Nombre = slot[i].HasValue ? nombresPorId[slot[i]!.Value] : "BYE",
                EsSembrado = slot[i].HasValue && idsSembrados.Contains(slot[i]!.Value),
                NumeroSeed = seedPorPosicion.TryGetValue(i, out int sn) ? sn : (int?)null
            });
        }

        return (true, "Propuesta generada con sembrados elegidos manualmente.", propuesta);
    }

    public async Task<(bool ok, string mensaje)> ConfirmarBracketEliminatoriaAsync(int categoriaId, List<SlotEliminatoria> propuesta)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return (false, "Categoría no encontrada.");

        using var transaccion = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        string fase;
        int partidosReales;
        try
        {
            bool yaExiste = await db.Partidos.AnyAsync(p => p.CategoriaId == categoriaId && p.GrupoId == null)
                        || await db.ByesFaseFinal.AnyAsync(b => b.CategoriaId == categoriaId);

            if (yaExiste)
            {
                await transaccion.RollbackAsync();
                return (false, "Esta categoría ya tiene fase final generada.");
            }

            var slot = propuesta.OrderBy(s => s.Posicion).Select(s => s.CompetidorId).ToArray();
            fase = FaseSegunTamano(slot.Length);

            foreach (var s in propuesta)
            {
                if (s.CompetidorId != null && s.NumeroSeed != null)
                {
                    var comp = await db.Competidores.FindAsync(s.CompetidorId.Value);
                    if (comp != null) comp.NumeroSembrado = s.NumeroSeed;
                }
            }

            partidosReales = await PersistirLlaveAsync(db, categoriaId, slot, fase);

            await transaccion.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al confirmar el bracket de la categoria {CategoriaId}", categoriaId);
            await transaccion.RollbackAsync();
            return (false, "Ocurrió un error al confirmar el bracket. Intenta de nuevo.");
        }

        _eventBus.Notificar();

        if (partidosReales == 0)
        {
            await IntentarAvanzarRondaAsync(categoriaId, fase);
            return (true, $"Bracket generado: {fase} (todos avanzan por bye).");
        }

        return (true, $"Bracket generado: {fase}.");
    }

    private async Task<List<int>> ObtenerClasificadosAsync(TorneoContext db, int categoriaId, Categoria categoria)
    {
        if (categoria.Formato == "Eliminatoria")
        {
            return await db.Competidores.Where(c => c.CategoriaId == categoriaId && c.Activo).Select(c => c.Id).ToListAsync();
        }

        var grupos = await db.Grupos.Where(g => g.CategoriaId == categoriaId).ToListAsync();
        var resultado = new List<int>();

        int nPorGrupo = categoria.ClasificadosPorGrupo ?? 2;

        foreach (var g in grupos)
        {
            var top = await db.Competidores
                .Where(c => c.GrupoId == g.Id && c.Activo)
                .OrderByDescending(c => c.Pg)
                .ThenByDescending(c => c.Sg - c.Sp)
                .ThenByDescending(c => c.Pf - c.Pc)
                .ThenBy(c => c.OrdenDesempate ?? int.MaxValue)
                .Take(nPorGrupo)
                .Select(c => c.Id)
                .ToListAsync();
            resultado.AddRange(top);
        }
        return resultado;
    }

    // =================================================================
    // SEEDING (usa el ranking histórico)
    // =================================================================

    private async Task AsignarRankingPrevioAsync(TorneoContext db, List<int> competidorIds, Categoria categoria)
    {
        var competidores = await db.Competidores
            .Where(c => competidorIds.Contains(c.Id))
            .Include(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .ToListAsync();

        var atletaIds = competidores
            .SelectMany(c => c.CompetidorIntegrantes)
            .Select(ci => ci.AtletaId)
            .Distinct()
            .ToList();

        var rankingActualPorClave = await db.RankingActuals
            .Where(r => atletaIds.Contains(r.AtletaId))
            .ToDictionaryAsync(r => (r.AtletaId, r.Modalidad, r.Rama), r => r.PuntosTotales ?? 0);

        var rankingCombinadoPorClave = await db.RankingCombinadoPorAtleta
            .Where(r => atletaIds.Contains(r.AtletaId))
            .ToDictionaryAsync(r => (r.AtletaId, r.Rama), r => r.PuntosCombinados ?? 0);

        foreach (var comp in competidores)
        {
            int puntos = 0;
            foreach (var integrante in comp.CompetidorIntegrantes)
            {
                var atleta = integrante.Atleta;
                string ramaAtleta = categoria.Rama == "Mixto"
                    ? (atleta.Genero == "M" ? "Varonil" : "Femenil")
                    : categoria.Rama;

                puntos += categoria.Modalidad == "Singles"
                    ? rankingActualPorClave.GetValueOrDefault((atleta.Id, categoria.Modalidad, ramaAtleta), 0)
                    : rankingCombinadoPorClave.GetValueOrDefault((atleta.Id, ramaAtleta), 0);
            }
            comp.RankingPrevio = puntos;
        }
        await db.SaveChangesAsync();
    }

    private static List<int> GenerarOrdenSeeds(int n)
    {
        var seeds = new List<int> { 1 };
        while (seeds.Count < n)
        {
            int len = seeds.Count * 2;
            var nuevos = new List<int>();
            foreach (var s in seeds)
            {
                nuevos.Add(s);
                nuevos.Add(len + 1 - s);
            }
            seeds = nuevos;
        }
        return seeds;
    }

    private static int InicioDeBanda(int seed)
    {
        if (seed <= 2) return seed;
        int potencia = 2;
        while (potencia < seed) potencia *= 2;
        return potencia / 2 + 1;
    }

    private static (List<int> posicionesLibres, Dictionary<int, int> seedPorPosicion) ColocarSembradosPorBanda(int?[] slot, List<int> ordenPosiciones, List<Competidore> sembrados, int cantidadSembrados, Random rnd)
    {
        var posicionesPorBanda = new Dictionary<int, List<int>>();
        for (int i = 0; i < ordenPosiciones.Count; i++)
        {
            int banda = InicioDeBanda(ordenPosiciones[i]);
            if (!posicionesPorBanda.TryGetValue(banda, out var lista))
                posicionesPorBanda[banda] = lista = new List<int>();
            lista.Add(i);
        }

        var posicionesLibres = new List<int>();
        var seedPorPosicion = new Dictionary<int, int>();

        foreach (var (bandaInicio, posicionesBanda) in posicionesPorBanda)
        {
            var seedsRealesBanda = Enumerable.Range(bandaInicio, posicionesBanda.Count)
                .Where(s => s <= cantidadSembrados)
                .ToList();

            if (seedsRealesBanda.Count == 0)
            {
                posicionesLibres.AddRange(posicionesBanda);
                continue;
            }

            var posicionesElegidas = posicionesBanda.OrderBy(_ => rnd.Next()).Take(seedsRealesBanda.Count).ToList();
            var seedsBarajados = seedsRealesBanda.OrderBy(_ => rnd.Next()).ToList();

            for (int k = 0; k < posicionesElegidas.Count; k++)
            {
                slot[posicionesElegidas[k]] = sembrados[seedsBarajados[k] - 1].Id;
                seedPorPosicion[posicionesElegidas[k]] = seedsBarajados[k];
            }
               
            posicionesLibres.AddRange(posicionesBanda.Except(posicionesElegidas));
        }

        return (posicionesLibres, seedPorPosicion);
    }

    // =================================================================
    // CAPTURA Y AVANCE AUTOMÁTICO DE RONDA
    // =================================================================

    public async Task<(bool ok, string mensaje)> CapturarResultadoBracketAsync(int partidoId, int s1a, int s1b, int s2a, int s2b, int s3a, int s3b, string modalidadSets, int puntosJuego, int puntosLimite, string usuario)
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
            if (!CalendarioService.EsMarcadorValido(ganador, perdedor, puntosJuego, puntosLimite))
            {
                return (false, $"Marcador inválido: {a}-{b}. Debe cumplir las reglas de puntos y diferencia mínima.");
            }   
        }

        int categoriaId;
        string faseActual;
        int? banda;

        using (var db = await _factory.CreateDbContextAsync())
        {
            var partido = await db.Partidos
                .Include(p => p.CompetidorA)
                .Include(p => p.CompetidorB)
                .FirstAsync(p => p.Id == partidoId);

            if (partido.Estado == "Jugado")
                return (false, "Este partido ya tiene resultado capturado.");

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

            foreach (var (num, pa, pb) in setsJugados)
                db.SetsPartidos.Add(new SetsPartido { PartidoId = partido.Id, NumeroSet = num, PuntosA = pa, PuntosB = pb });

            partido.Estado = "Jugado";
            partido.GanadorId = setsA > setsB ? partido.CompetidorAid : partido.CompetidorBid;
            partido.FechaCaptura = DateTime.Now;
            partido.CapturadoPor = usuario;

            categoriaId = partido.CategoriaId;
            faseActual = partido.Fase;
            banda = partido.Banda;

            await db.SaveChangesAsync();
        }

        await IntentarAvanzarRondaAsync(categoriaId, faseActual, banda);

        _eventBus.Notificar();
        return (true, "Resultado guardado.");
    }

    public async Task AvanzarRondaPublicoAsync(int categoriaId, string fase, int? banda)
    {
        await IntentarAvanzarRondaAsync(categoriaId, fase, banda);
    }

    private async Task IntentarAvanzarRondaAsync(int categoriaId, string faseActual, int? banda = null)
    {
        using var db = await _factory.CreateDbContextAsync();

        var partidosRonda = await db.Partidos
            .Where(p => p.CategoriaId == categoriaId && p.Fase == faseActual && p.GrupoId == null && p.Banda == banda)
            .ToListAsync();

        if (partidosRonda.Any(p => p.Estado != "Jugado")) return;

        if (faseActual == "Final")
        {
            if (banda == null)
                await CerrarCategoriaAsync(db, categoriaId);
            return;
        }

        if (!SiguienteFase.TryGetValue(faseActual, out var siguienteFase)) return;

        using var transaccion = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        bool huboWalkoverAutomatico;
        try
        {
            bool yaExisteSiguiente = await db.Partidos.AnyAsync(p => p.CategoriaId == categoriaId && p.Fase == siguienteFase && p.Banda == banda)
                                || await db.ByesFaseFinal.AnyAsync(b => b.CategoriaId == categoriaId && b.Fase == siguienteFase && b.Banda == banda);

            if (yaExisteSiguiente)
            {
                await transaccion.RollbackAsync();
                return;
            }

            var byesRonda = await db.ByesFaseFinal
                .Where(b => b.CategoriaId == categoriaId && b.Fase == faseActual && b.Banda == banda)
                .ToListAsync();

            var avanzan = partidosRonda
                .Select(p => (Posicion: p.OrdenCola, CompetidorId: p.GanadorId!.Value))
                .Concat(byesRonda.Select(b => (Posicion: b.PosicionLlave, CompetidorId: b.CompetidorId)))
                .OrderBy(x => x.Posicion)
                .Select(x => x.CompetidorId)
                .ToList();

            // Si algun atleta que avanza ya está dado de baja no se genera un partido pendiente
            // en su contra, directamente se resuelve como ganado por default para el rival.

            var competidoresInfo = await db.Competidores
                .Where(c => avanzan.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c);

            huboWalkoverAutomatico = false;
            int orden = 0;
            for (int i = 0; i < avanzan.Count; i += 2)
            {
                int idA = avanzan[i];
                int idB = avanzan[i + 1];
                bool activoA = competidoresInfo.TryGetValue(idA, out var compA) && compA.Activo;
                bool activoB = competidoresInfo.TryGetValue(idB, out var compB) && compB.Activo;

                var nuevoPartido = new Partido
                {
                    CategoriaId = categoriaId,
                    GrupoId = null,
                    Banda = banda,
                    CompetidorAid = idA,
                    CompetidorBid = idB,
                    Fase = siguienteFase,
                    Estado = "Pendiente",
                    OrdenCola = orden++
                };

                if (!activoA && activoB)
                {
                    nuevoPartido.Estado = "Jugado";
                    nuevoPartido.GanadorId = idB;
                    nuevoPartido.EsDefault = true;
                    nuevoPartido.FechaCaptura = DateTime.Now;
                    if (compB != null) compB.Pg++;
                    if (compA != null) compA.Pp++;
                    huboWalkoverAutomatico = true;
                }
                else if (!activoB && activoA)
                {
                    nuevoPartido.Estado = "Jugado";
                    nuevoPartido.GanadorId = idA;
                    nuevoPartido.EsDefault = true;
                    nuevoPartido.FechaCaptura = DateTime.Now;
                    if (compA != null) compA.Pg++;
                    if (compB != null) compB.Pp++;
                    huboWalkoverAutomatico = true;
                }

                db.Partidos.Add(nuevoPartido);
            }
            await db.SaveChangesAsync();
            await transaccion.CommitAsync();
        }
        catch
        {
            await transaccion.RollbackAsync();
            return;
        }

        if (huboWalkoverAutomatico)
            await IntentarAvanzarRondaAsync(categoriaId, siguienteFase, banda);
    }

    // =================================================================
    // CIERRE: guarda el ranking histórico (top 8, por tiers)
    // =================================================================

    private async Task CerrarCategoriaAsync(TorneoContext db, int categoriaId)
    {
        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return;

        var final = await db.Partidos.FirstOrDefaultAsync(p => p.CategoriaId == categoriaId && p.Fase == "Final");
        if (final == null || final.Estado != "Jugado") return;

        int campeonId = final.GanadorId!.Value;
        int subcampeonId = final.CompetidorAid == campeonId ? final.CompetidorBid : final.CompetidorAid;

        var puntos = await db.TablaPuntosRankings.ToDictionaryAsync(t => t.Posicion, t => t.Puntos);

        await RegistrarRankingAsync(db, categoria, campeonId, 1, puntos[1]);
        await RegistrarRankingAsync(db, categoria, subcampeonId, 2, puntos[2]);

        var semis = await db.Partidos.Where(p => p.CategoriaId == categoriaId && p.Fase == "Semifinal").ToListAsync();
        foreach (var s in semis)
        {
            int perdedor = s.CompetidorAid == s.GanadorId ? s.CompetidorBid : s.CompetidorAid;
            await RegistrarRankingAsync(db, categoria, perdedor, 3, puntos[3]);
        }

        var cuartos = await db.Partidos.Where(p => p.CategoriaId == categoriaId && p.Fase == "Cuartos").ToListAsync();
        foreach (var c in cuartos)
        {
            int perdedor = c.CompetidorAid == c.GanadorId ? c.CompetidorBid : c.CompetidorAid;
            await RegistrarRankingAsync(db, categoria, perdedor, 5, puntos[5]);
        }

        var octavos = await db.Partidos.Where(p => p.CategoriaId == categoriaId && p.Fase == "Octavos").ToListAsync();
        foreach (var o in octavos)
        {
            int perdedor = o.CompetidorAid == o.GanadorId ? o.CompetidorBid : o.CompetidorAid;
            await RegistrarRankingAsync(db, categoria, perdedor, 9, puntos[9]);
        }
    }

    public async Task<(bool ok, string mensaje)> CerrarCategoriaRoundRobinAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return (false, "Categoría no encontrada.");
        if (categoria.Formato != "RoundRobin") return (false, "Esta categoría no es de tipo todos contra todos.");

        bool yaCerrada = await db.RankingHistorials.AnyAsync(r => r.CategoriaId == categoriaId);
        if (yaCerrada) return (false, "Esta categoría ya fue cerrada.");

        var partidos = await db.Partidos
            .Where(p => p.CategoriaId == categoriaId && p.Fase == "Liga")
            .ToListAsync();

        if (partidos.Count == 0 || partidos.Any(p => p.Estado != "Jugado"))
            return (false, "Todavía hay partidos sin jugar en esta categoría.");

        var competidores = await db.Competidores
            .Where(c => c.CategoriaId == categoriaId)
            .ToListAsync();

        var ordenados = competidores
            .OrderByDescending(c => c.Pg)
            .ThenByDescending(c => c.Sg - c.Sp)
            .ThenByDescending(c => c.Pf - c.Pc)
            .ThenBy(c => c.OrdenDesempate ?? int.MaxValue)
            .ToList();

        var puntos = await db.TablaPuntosRankings.ToDictionaryAsync(t => t.Posicion, t => t.Puntos);

        for (int i = 0; i < Math.Min(3, ordenados.Count); i++)
        {
            int posicion = i + 1;
            if (puntos.TryGetValue(posicion, out int pts))
                await RegistrarRankingAsync(db, categoria, ordenados[i].Id, posicion, pts);
        }

        return (true, "Categoría cerrada. Se otorgaron puntos al podio.");
    }

    private async Task RegistrarRankingAsync(TorneoContext db, Categoria categoria, int competidorId, int posicion, int puntos)
    {
        var integrantes = await db.CompetidorIntegrantes.Where(ci => ci.CompetidorId == competidorId).ToListAsync();
        foreach (var integ in integrantes)
        {
            bool yaExiste = await db.RankingHistorials.AnyAsync(r =>
                r.AtletaId == integ.AtletaId && r.TorneoId == categoria.TorneoId && r.CategoriaId == categoria.Id);
            if (yaExiste) continue;

            db.RankingHistorials.Add(new RankingHistorial
            {
                AtletaId = integ.AtletaId,
                TorneoId = categoria.TorneoId,
                CategoriaId = categoria.Id,
                Posicion = posicion,
                Puntos = puntos
            });
        }
        await db.SaveChangesAsync();
    }

    // =================================================================
    // JORNADAS: llaves de eliminación por bandas de 4
    // =================================================================

    public async Task<(bool ok, string mensaje)> GenerarBandasEliminacionAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoria = await db.Categorias.FindAsync(categoriaId);
        if (categoria == null) return (false, "Categoría no encontrada.");
        if (categoria.Formato != "Jornadas") return (false, "Esta opción es solo para categorías de formato Jornadas.");

        var config = await db.Configuracions.FirstOrDefaultAsync(c => c.CategoriaId == categoriaId);
        int vueltas = config?.NumeroVueltas ?? 1;
        int activos = await db.Competidores.CountAsync(c => c.CategoriaId == categoriaId && c.Activo);
        int totalJornadasPlaneadas = CalendarioService.CalcularTotalJornadas(activos, vueltas);

        int ultimaJornadaGenerada = await db.Partidos
            .Where(p => p.CategoriaId == categoriaId && p.Fase == "Liga")
            .Select(p => (int?)p.Jornada)
            .MaxAsync() ?? 0;

        if (ultimaJornadaGenerada < totalJornadasPlaneadas)
        {
            return (false, $"Todavía faltan jornadas por generar y jugar ({ultimaJornadaGenerada} de {totalJornadasPlaneadas}).");
        }

        var partidosLiga = await db.Partidos.Where(p => p.CategoriaId == categoriaId && p.Fase == "Liga").ToListAsync();
        if (!partidosLiga.Any()) return (false, "Todavía no se han generado jornadas para esta categoría.");
        if (partidosLiga.Any(p => p.Estado != "Jugado")) return (false, "Todavía hay partidos de jornada sin jugar.");

        using var transaccion = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        int numBandas;
        int sobrantes;
        try
        {
            bool yaExisten = await db.Partidos.AnyAsync(p => p.CategoriaId == categoriaId && p.Banda != null)
                        || await db.ByesFaseFinal.AnyAsync(b => b.CategoriaId == categoriaId && b.Banda != null);

            if (yaExisten)
            {
                await transaccion.RollbackAsync();
                return (false, "Ya se generaron las llaves de eliminación de esta categoría.");
            }

            var competidores = await db.Competidores.Where(c => c.CategoriaId == categoriaId && c.Activo).ToListAsync();

            var tabla = competidores
                .OrderByDescending(c => c.Pg)
                .ThenByDescending(c => c.Sg - c.Sp)
                .ThenByDescending(c => c.Pf - c.Pc)
                .ThenBy(c => c.OrdenDesempate ?? int.MaxValue)
                .Select(c => c.Id)
                .ToList();

            numBandas = tabla.Count / 4;
            if (numBandas == 0)
            {
                await transaccion.RollbackAsync();
                return (false, "Se necesitan al menos 4 competidores para formar una llave.");
            }

            for (int b = 0; b < numBandas; b++)
            {
                var grupo4 = tabla.Skip(b * 4).Take(4).ToList();
                var slot = new int?[] { grupo4[0], grupo4[3], grupo4[1], grupo4[2] };
                await PersistirLlaveAsync(db, categoriaId, slot, "Semifinal", banda: b + 1);

                var competidoresBanda = await db.Competidores.Where(c => grupo4.Contains(c.Id)).ToListAsync();
                for (int pos = 0; pos < grupo4.Count; pos++)
                {
                    var comp = competidoresBanda.First(c => c.Id == grupo4[pos]);
                    comp.RankingPrevio = grupo4.Count - pos;
                }
            }
            await db.SaveChangesAsync();

            sobrantes = tabla.Count - (numBandas * 4);
            await transaccion.CommitAsync();
        }
        catch
        {
            await transaccion.RollbackAsync();
            return (false, "Ocurrió un error al generar las llaves. Intenta de nuevo.");
        }

        _eventBus.Notificar();
        string nota = sobrantes > 0 ? $" {sobrantes} competidor(es) quedaron fuera de las llaves de eliminación." : "";
        return (true, $"Se generaron {numBandas} llave(s) de 4. {nota}");
    }

    // =================================================================
    // CONSULTA PARA LA PÁGINA
    // =================================================================

    public async Task<List<LlaveItem>> ObtenerBracketAsync(int categoriaId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var partidos = await db.Partidos
            .Where(p => p.CategoriaId == categoriaId && p.GrupoId == null && p.Fase != "Liga")
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.SetsPartidos)
            .ToListAsync();

        var byes = await db.ByesFaseFinal
            .Where(b => b.CategoriaId == categoriaId)
            .Include(b => b.Competidor).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .ToListAsync();

        return partidos
            .Select(p => new LlaveItem { Fase = p.Fase, Posicion = p.OrdenCola, Banda = p.Banda, Partido = p })
            .Concat(byes.Select(b => new LlaveItem { Fase = b.Fase, Posicion = b.PosicionLlave, Banda = b.Banda, CompetidorConBye = b.Competidor }))
            .OrderBy(x => x.Banda ?? 0)
            .ThenBy(x => OrdenFase.GetValueOrDefault(x.Fase, 99))
            .ThenBy(x => x.Posicion)
            .ToList();
    }
}