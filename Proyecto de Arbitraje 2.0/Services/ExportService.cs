using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProyectoArbitraje.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProyectoArbitraje.Services;

public class ExportService
{
    private readonly IDbContextFactory<TorneoContext> _factory;

    public ExportService(IDbContextFactory<TorneoContext> factory)
    {
        _factory = factory;
    }

    public async Task<byte[]> GenerarExcelTorneoAsync(int torneoId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var torneo = await db.Torneos.FindAsync(torneoId);
        if (torneo == null) throw new Exception("Torneo no encontrado.");

        using var wb = new XLWorkbook();

        await HojaResumenAsync(wb, db, torneo);
        await HojaAtletasAsync(wb, db, torneoId);
        await HojaCalendarioAsync(wb, db, torneoId);
        await HojaPosicionesAsync(wb, db, torneoId);
        await HojaResultadosFinalesAsync(wb, db, torneoId);
        await HojaRankingAsync(wb, db, torneoId);
        await HojaDetallePuntosAsync(wb, db, torneoId);

        foreach (var ws in wb.Worksheets)
            ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Encabezado(IXLWorksheet ws, params string[] columnas)
    {
        for (int i = 0; i < columnas.Length; i++)
        {
            var celda = ws.Cell(1, i + 1);
            celda.Value = columnas[i];
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B4F4A");
            celda.Style.Font.FontColor = XLColor.White;
        }
        ws.SheetView.FreezeRows(1);
    }

    private async Task HojaResumenAsync(XLWorkbook wb, TorneoContext db, Models.Torneo torneo)
    {
        var ws = wb.Worksheets.Add("Resumen");

        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneo.Id).Select(c => c.Id).ToListAsync();
        int totalCategorias = categoriaIds.Count;
        int totalAtletas = await db.Competidores.CountAsync(c => categoriaIds.Contains(c.CategoriaId));
        int totalPartidos = await db.Partidos.CountAsync(p => categoriaIds.Contains(p.CategoriaId));
        int partidosJugados = await db.Partidos.CountAsync(p => categoriaIds.Contains(p.CategoriaId) && p.Estado == "Jugado");

        string estadoActual = !categoriaIds.Any() || totalPartidos == 0
            ? "Planeado"
            : (partidosJugados == totalPartidos ? "Jugado" : "Pendiente");
        
        var filas = new (string, string)[]
        {
            ("Torneo", torneo.Nombre),
            ("Fecha", torneo.Fecha.ToString("dd/MM/yyyy")),
            ("Estado", estadoActual),
            ("Categorías", totalCategorias.ToString()),
            ("Competidores inscritos", totalAtletas.ToString()),
            ("Partidos totales", totalPartidos.ToString()),
            ("Partidos jugados", partidosJugados.ToString()),
            ("Reporte generado", DateTime.Now.ToString("dd/MM/yyyy HH:mm")),
        };

        for (int i = 0; i < filas.Length; i++)
        {
            ws.Cell(i + 1, 1).Value = filas[i].Item1;
            ws.Cell(i + 1, 1).Style.Font.Bold = true;
            ws.Cell(i + 1, 2).Value = filas[i].Item2;
        }
    }

    private async Task HojaAtletasAsync(XLWorkbook wb, TorneoContext db, int torneoId)
    {
        var ws = wb.Worksheets.Add("Atletas");
        Encabezado(ws, "Categoría", "Modalidad", "Rama", "Grupo", "Nombre", "Municipio", "Género", "Año Nacimiento", "Compañero(s)", "Estado");

        var competidores = await db.Competidores
            .Where(c => c.Categoria.TorneoId == torneoId)
            .Include(c => c.Categoria)
            .Include(c => c.Grupo)
            .Include(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta).ThenInclude(a => a.Municipio)
            .ToListAsync();

        int fila = 2;
        foreach (var c in competidores.OrderBy(c => c.Categoria.Nombre).ThenBy(c => c.Categoria.Modalidad).ThenBy(c => c.Categoria.Rama))
        {
            foreach (var integrante in c.CompetidorIntegrantes)
            {
                var atleta = integrante.Atleta;
                string companeros = string.Join(" / ", c.CompetidorIntegrantes
                    .Where(ci => ci.AtletaId != atleta.Id)
                    .Select(ci => ci.Atleta.Nombre));

                ws.Cell(fila, 1).Value = c.Categoria.Nombre;
                ws.Cell(fila, 2).Value = c.Categoria.Modalidad;
                ws.Cell(fila, 3).Value = c.Categoria.Rama;
                ws.Cell(fila, 4).Value = c.Grupo?.Letra ?? "";
                ws.Cell(fila, 5).Value = atleta.Nombre;
                ws.Cell(fila, 6).Value = atleta.Municipio?.Nombre ?? "";
                ws.Cell(fila, 7).Value = atleta.Genero ?? "";
                ws.Cell(fila, 8).Value = atleta.AnioNacimiento?.ToString() ?? "";
                ws.Cell(fila, 9).Value = companeros;
                ws.Cell(fila, 10).Value = c.Activo ? "Activo" : "Baja";
                fila++;
            }
        }
    }

    private async Task HojaCalendarioAsync(XLWorkbook wb, TorneoContext db, int torneoId)
    {
        var ws = wb.Worksheets.Add("Calendario");
        Encabezado(ws, "Categoría", "Fase", "Grupo/Banda", "Jornada", "Competidor A", "Competidor B",
            "Set 1", "Set 2", "Set 3", "Ganador", "Cancha", "Fecha", "Default", "Estado");

        var partidos = await db.Partidos
            .Where(p => p.Categoria.TorneoId == torneoId)
            .Include(p => p.Categoria)
            .Include(p => p.Grupo)
            .Include(p => p.Cancha)
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.SetsPartidos)
            .OrderBy(p => p.Categoria.Nombre).ThenBy(p => p.Fase).ThenBy(p => p.Jornada).ThenBy(p => p.OrdenCola)
            .ToListAsync();

        int fila = 2;
        foreach (var p in partidos)
        {
            string nombreA = string.Join(" / ", p.CompetidorA.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre));
            string nombreB = string.Join(" / ", p.CompetidorB.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre));
            var sets = p.SetsPartidos.OrderBy(s => s.NumeroSet).ToList();

            ws.Cell(fila, 1).Value = $"{p.Categoria.Nombre} · {p.Categoria.Modalidad} · {p.Categoria.Rama}";
            ws.Cell(fila, 2).Value = p.Fase;
            ws.Cell(fila, 3).Value = p.Grupo?.Letra ?? (p.Banda?.ToString() ?? "");
            ws.Cell(fila, 4).Value = p.Jornada?.ToString() ?? "";
            ws.Cell(fila, 5).Value = nombreA;
            ws.Cell(fila, 6).Value = nombreB;
            ws.Cell(fila, 7).Value = sets.Count > 0 ? $"{sets[0].PuntosA}-{sets[0].PuntosB}" : "";
            ws.Cell(fila, 8).Value = sets.Count > 1 ? $"{sets[1].PuntosA}-{sets[1].PuntosB}" : "";
            ws.Cell(fila, 9).Value = sets.Count > 2 ? $"{sets[2].PuntosA}-{sets[2].PuntosB}" : "";
            ws.Cell(fila, 10).Value = p.GanadorId == p.CompetidorAid ? nombreA : (p.GanadorId == p.CompetidorBid ? nombreB : "");
            ws.Cell(fila, 11).Value = p.Cancha?.Numero.ToString() ?? "";
            ws.Cell(fila, 12).Value = p.FechaCaptura?.ToString("dd/MM/yyyy HH:mm") ?? "";
            ws.Cell(fila, 13).Value = p.EsDefault ? "Sí" : "No";
            ws.Cell(fila, 14).Value = p.Estado;
            fila++;
        }
    }

    private async Task HojaPosicionesAsync(XLWorkbook wb, TorneoContext db, int torneoId)
    {
        var ws = wb.Worksheets.Add("Posiciones");
        Encabezado(ws, "Categoría", "Grupo", "Atleta/Pareja", "Equipo", "PJ", "PG", "PP", "SG", "SP", "±S", "PF", "PC", "±P");

        var categorias = await db.Categorias
            .Where(c => c.TorneoId == torneoId &&
                (c.Formato == "GruposFaseFinal" || c.Formato == "Jornadas" || c.Formato == "RoundRobin"))
            .ToListAsync();

        int fila = 2;
        foreach (var cat in categorias.OrderBy(c => c.Nombre))
        {
            if (cat.Formato == "GruposFaseFinal")
            {
                var competidores = await db.Competidores
                    .Where(c => c.CategoriaId == cat.Id && c.GrupoId != null)
                    .Include(c => c.Grupo)
                    .Include(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta).ThenInclude(a => a.Municipio)
                    .ToListAsync();

                var porGrupo = competidores.GroupBy(c => c.Grupo!.Letra).OrderBy(g => g.Key);

                foreach (var grupo in porGrupo)
                {
                    var ordenados = grupo
                        .OrderByDescending(c => c.Pg)
                        .ThenByDescending(c => c.Sg - c.Sp)
                        .ThenByDescending(c => c.Pf - c.Pc)
                        .ThenBy(c => c.OrdenDesempate ?? int.MaxValue)
                        .ToList();

                    foreach (var c in ordenados)
                        EscribirFilaPosicion(ws, ref fila, cat, grupo.Key, c);
                }
            }
            else
            {
                // Jornadas y RoundRobin: una sola tabla por categoría, sin grupos.
                var competidores = await db.Competidores
                    .Where(c => c.CategoriaId == cat.Id && c.Activo)
                    .Include(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta).ThenInclude(a => a.Municipio)
                    .ToListAsync();

                var ordenados = competidores
                    .OrderByDescending(c => c.Pg)
                    .ThenByDescending(c => c.Sg - c.Sp)
                    .ThenByDescending(c => c.Pf - c.Pc)
                    .ThenBy(c => c.OrdenDesempate ?? int.MaxValue)
                    .ToList();

                string etiquetaGrupo = cat.Formato == "Jornadas" ? "Liga" : "Todos vs todos";

                foreach (var c in ordenados)
                    EscribirFilaPosicion(ws, ref fila, cat, etiquetaGrupo, c);
            }
        }
    }

    private static void EscribirFilaPosicion(IXLWorksheet ws, ref int fila, Models.Categoria cat, string grupoTexto, Models.Competidore c)
    {
        string nombre = string.Join(" / ", c.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre));
        string equipo = string.Join(" / ", c.CompetidorIntegrantes.Select(ci => ci.Atleta.Municipio.Nombre).Distinct());

        ws.Cell(fila, 1).Value = $"{cat.Nombre} · {cat.Modalidad} · {cat.Rama}";
        ws.Cell(fila, 2).Value = grupoTexto;
        ws.Cell(fila, 3).Value = nombre;
        ws.Cell(fila, 4).Value = equipo;
        ws.Cell(fila, 5).Value = c.Pg + c.Pp;   // PJ (nueva)
        ws.Cell(fila, 6).Value = c.Pg;
        ws.Cell(fila, 7).Value = c.Pp;
        ws.Cell(fila, 8).Value = c.Sg;
        ws.Cell(fila, 9).Value = c.Sp;
        ws.Cell(fila, 10).Value = c.Sg - c.Sp;
        ws.Cell(fila, 11).Value = c.Pf;
        ws.Cell(fila, 12).Value = c.Pc;
        ws.Cell(fila, 13).Value = c.Pf - c.Pc;
        fila++;
    }

    private async Task HojaResultadosFinalesAsync(XLWorkbook wb, TorneoContext db, int torneoId)
    {
        var ws = wb.Worksheets.Add("Resultados Finales");
        Encabezado(ws, "Categoría", "Puesto", "Nombre(s)");

        var historial = await db.RankingHistorials
            .Where(r => r.TorneoId == torneoId)
            .Include(r => r.Atleta)
            .Include(r => r.Categoria)
            .ToListAsync();

        string Etiqueta(int posicion) => posicion switch
        {
            1 => "Campeón",
            2 => "Subcampeón",
            3 => "Semifinalista",
            5 => "Cuartofinalista",
            9 => "Octavofinalista",
            _ => $"Puesto {posicion}"
        };

        int fila = 2;
        foreach (var g in historial.GroupBy(r => r.Categoria).OrderBy(g => g.Key.Nombre))
        {
            foreach (var pos in g.Select(r => r.Posicion).Distinct().OrderBy(p => p))
            {
                var nombres = string.Join(" / ", g.Where(r => r.Posicion == pos).Select(r => r.Atleta.Nombre));

                ws.Cell(fila, 1).Value = $"{g.Key.Nombre} · {g.Key.Modalidad} · {g.Key.Rama}";
                ws.Cell(fila, 2).Value = Etiqueta(pos);
                ws.Cell(fila, 3).Value = nombres;
                fila++;
            }
        }
    }

    // ===== Hoja de puntuación (formato de papel, horizontal) =====

    private const int ColumnasPunto = 30;   // cuadros de punto por renglón

    public async Task<byte[]?> GenerarPdfHojasPuntuacionAsync(int torneoId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneoId).Select(c => c.Id).ToListAsync();

        var partidos = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId) && p.PuntosPartido.Any())
            .Include(p => p.Categoria)
            .Include(p => p.SetsPartidos)
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.PuntosPartido)
            .OrderBy(p => p.Categoria.Nombre)
            .ThenBy(p => p.Id)
            .ToListAsync();

        if (!partidos.Any()) return null;

        var documento = Document.Create(contenedor =>
        {
            foreach (var partido in partidos)
            {
                var integrantesA = partido.CompetidorA.CompetidorIntegrantes.ToList();
                var integrantesB = partido.CompetidorB.CompetidorIntegrantes.ToList();
                var nombresA = integrantesA.Select(ci => ci.Atleta.Nombre).ToList();
                var nombresB = integrantesB.Select(ci => ci.Atleta.Nombre).ToList();
                var idsA = integrantesA.Select(ci => ci.AtletaId).ToList();
                var idsB = integrantesB.Select(ci => ci.AtletaId).ToList();
                bool dobles = idsA.Count == 2 && idsB.Count == 2;

                var puntosPorSet = partido.PuntosPartido
                    .OrderBy(pp => pp.NumeroSet).ThenBy(pp => pp.NumeroPunto)
                    .GroupBy(pp => pp.NumeroSet)
                    .ToList();

                // Marcador de cada set para el cuadro del centro
                var marcadores = new Dictionary<int, (int a, int b)>();
                foreach (var g in puntosPorSet)
                {
                    var ultimo = g.Last();
                    marcadores[g.Key] = (ultimo.PuntosA, ultimo.PuntosB);
                }
                foreach (var s in partido.SetsPartidos)
                    marcadores[s.NumeroSet] = (s.PuntosA, s.PuntosB);

                int setsGanadosA = partido.SetsPartidos.Count(s => s.PuntosA > s.PuntosB);
                int setsGanadosB = partido.SetsPartidos.Count(s => s.PuntosB > s.PuntosA);

                contenedor.Page(pagina =>
                {
                    pagina.Size(PageSizes.Letter.Landscape());
                    pagina.Margin(28);
                    pagina.DefaultTextStyle(x => x.FontSize(9));

                    pagina.Header().Column(col =>
                    {
                        col.Item().Text("Hoja de puntuación de bádminton").FontSize(15).Bold();
                        col.Item().Text($"{partido.Categoria.Nombre} · {partido.Categoria.Modalidad} · {partido.Categoria.Rama} - Fase: {partido.Fase}");
                        col.Item().Text($"Capturado por: {partido.CapturadoPor ?? "-"}   Fecha: {partido.FechaCaptura?.ToString("dd/MM/yyyy HH:mm") ?? "-"}");

                        col.Item().PaddingTop(8).Row(fila =>
                        {
                            fila.RelativeItem(5).Row(r =>
                            {
                                r.ConstantItem(34).AlignTop().AlignLeft().Element(x => CuadroSets(x, setsGanadosA));
                                r.RelativeItem().Element(x => CajaNombres(x, nombresA));
                            });

                            fila.ConstantItem(170).PaddingHorizontal(12).Element(x => TablaMarcadores(x, marcadores));

                            fila.RelativeItem(5).Row(r =>
                            {
                                r.RelativeItem().Element(x => CajaNombres(x, nombresB));
                                r.ConstantItem(34).AlignTop().AlignRight().Element(x => CuadroSets(x, setsGanadosB));
                            });
                        });
                    });

                    pagina.Content().PaddingTop(6).Column(col =>
                    {
                        foreach (var grupoSet in puntosPorSet)
                        {
                            var puntos = grupoSet.ToList();
                            int finalA = puntos[^1].PuntosA;
                            int finalB = puntos[^1].PuntosB;

                            col.Item().PaddingTop(8).ShowEntire().Column(setCol =>
                            {
                                setCol.Item().Text($"Set {grupoSet.Key}").FontSize(11).Bold();

                                if (dobles)
                                    DibujarSetDobles(setCol, nombresA.Concat(nombresB).ToList(), idsA, idsB, puntos, finalA, finalB);
                                else
                                    DibujarSetSingles(setCol, nombresA[0], nombresB[0], idsA.Contains(puntos[0].SirveAtletaId), puntos, finalA, finalB);
                            });
                        }
                    });

                    pagina.Footer().AlignCenter().Text(x =>
                    {
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            }
        });

        return documento.GeneratePdf();
    }

    private static IContainer Celda(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Darken2).MinHeight(17).AlignCenter().AlignMiddle();

    private static IContainer CeldaNombre(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Darken2).MinHeight(17).PaddingLeft(3).AlignLeft().AlignMiddle();

    private static void CuadroSets(IContainer c, int sets) =>
        c.Width(28).Height(28).Border(1).AlignCenter().AlignMiddle().Text(sets.ToString()).FontSize(14).Bold();

    private static void CajaNombres(IContainer c, List<string> nombres)
    {
        c.Border(1).Column(col =>
        {
            if (nombres.Count < 2)
            {
                col.Item().Height(44).AlignMiddle().PaddingLeft(6).Text(nombres.FirstOrDefault() ?? "").Bold().FontSize(11);
            }
            else
            {
                col.Item().Height(22).AlignMiddle().PaddingLeft(6).Text(nombres[0]).Bold().FontSize(10);
                col.Item().BorderTop(0.5f).Height(22).AlignMiddle().PaddingLeft(6).Text(nombres[1]).Bold().FontSize(10);
            }
        });
    }

    private static void TablaMarcadores(IContainer c, Dictionary<int, (int a, int b)> marcadores)
    {
        c.Border(1).Column(col =>
        {
            for (int s = 1; s <= 3; s++)
            {
                bool hay = marcadores.TryGetValue(s, out var m);
                string etiqueta = s.ToString();
                string puntosA = hay ? m.a.ToString() : "";
                string puntosB = hay ? m.b.ToString() : "";
                float bordeArriba = s == 1 ? 0f : 0.5f;

                col.Item().BorderTop(bordeArriba).Height(18).Row(r =>
                {
                    r.ConstantItem(22).BorderRight(0.5f).AlignCenter().AlignMiddle().Text(etiqueta).Bold();
                    r.RelativeItem().AlignCenter().AlignMiddle().Text(puntosA).Bold().FontSize(11);
                    r.ConstantItem(10).AlignCenter().AlignMiddle().Text(":");
                    r.RelativeItem().AlignCenter().AlignMiddle().Text(puntosB).Bold().FontSize(11);
                });
            }
        });
    }

    private static void DefinirColumnasHoja(TableColumnsDefinitionDescriptor c)
    {
        c.ConstantColumn(120);   // nombre
        c.ConstantColumn(20);    // S / R
        c.ConstantColumn(18);    // 0 inicial
        for (int i = 0; i < ColumnasPunto; i++) c.ConstantColumn(17);   // cuadros de punto
        c.ConstantColumn(40);    // marcador final
    }

    // Singles: 2 renglones por jugador; si se acaba el espacio, sigue en el renglón de abajo
    private static void DibujarSetSingles(ColumnDescriptor col, string nombreA, string nombreB, bool sirveEsA,
        List<Models.PuntoPartido> puntos, int finalA, int finalB)
    {
        int n = puntos.Count;
        int lineas = Math.Max(2, (int)Math.Ceiling(n / (double)ColumnasPunto));
        uint colFinal = (uint)(4 + ColumnasPunto);

        col.Item().Table(t =>
        {
            t.ColumnsDefinition(DefinirColumnasHoja);

            for (int jugador = 0; jugador < 2; jugador++)
            {
                bool esA = jugador == 0;
                string nombre = esA ? nombreA : nombreB;
                string letra = esA == sirveEsA ? "S" : "R";
                int puntosFinal = esA ? finalA : finalB;
                uint filaBase = (uint)(jugador * lineas + 1);

                CeldaNombre(t.Cell().Row(filaBase).Column(1).RowSpan((uint)lineas)).Text(nombre).Bold().FontSize(9);
                Celda(t.Cell().Row(filaBase).Column(colFinal).RowSpan((uint)lineas)).Text(puntosFinal.ToString()).Bold().FontSize(11);

                for (int linea = 0; linea < lineas; linea++)
                {
                    uint fila = filaBase + (uint)linea;

                    if (linea == 0)
                    {
                        Celda(t.Cell().Row(fila).Column(2)).Text(letra).Bold().FontSize(9);
                        Celda(t.Cell().Row(fila).Column(3)).Text("0").FontSize(8);
                    }
                    else
                    {
                        Celda(t.Cell().Row(fila).Column(2)).Background(Colors.Grey.Lighten3).Text("");
                        Celda(t.Cell().Row(fila).Column(3)).Background(Colors.Grey.Lighten3).Text("");
                    }

                    for (int i = 0; i < ColumnasPunto; i++)
                    {
                        int k = linea * ColumnasPunto + i;   // número de jugada
                        string texto = "";
                        if (k < n)
                        {
                            var pp = puntos[k];
                            bool anotoEste = (pp.EquipoAnoto == "A") == esA;
                            if (anotoEste) texto = (esA ? pp.PuntosA : pp.PuntosB).ToString();
                        }
                        Celda(t.Cell().Row(fila).Column((uint)(4 + i))).Text(texto).FontSize(8);
                    }
                }
            }
        });
    }

    // Dobles: un renglón por atleta; cada punto se anota en el renglón de quien saca después de ese punto.
    // Si se acaba el espacio, se abre otra tabla debajo.
    private static void DibujarSetDobles(ColumnDescriptor col, List<string> nombresFilas, List<int> idsA, List<int> idsB,
        List<Models.PuntoPartido> puntos, int finalA, int finalB)
    {
        var idsFilas = idsA.Concat(idsB).ToList();
        int n = puntos.Count;
        int tablas = Math.Max(1, (int)Math.Ceiling(n / (double)ColumnasPunto));
        uint colFinal = (uint)(4 + ColumnasPunto);

        int sirveInicial = puntos[0].SirveAtletaId;
        int recibeInicial = puntos[0].RecibeAtletaId;

        // Renglón (0 a 3) donde va cada punto
        var filaPorPunto = new int[n];
        for (int k = 0; k < n; k++)
        {
            var (sirveId, _) = CalendarioService.CalcularServicioTrasPuntos(
                idsA, idsB, sirveInicial, recibeInicial,
                puntos.Take(k + 1).Select(p => p.EquipoAnoto));
            filaPorPunto[k] = Math.Max(0, idsFilas.IndexOf(sirveId));
        }

        for (int tabla = 0; tabla < tablas; tabla++)
        {
            bool primera = tabla == 0;
            bool ultima = tabla == tablas - 1;
            int inicio = tabla * ColumnasPunto;

            col.Item().PaddingTop(primera ? 0 : 4).Table(t =>
            {
                t.ColumnsDefinition(DefinirColumnasHoja);

                for (int fila = 0; fila < 4; fila++)
                {
                    uint f = (uint)(fila + 1);
                    int idJugador = idsFilas[fila];

                    CeldaNombre(t.Cell().Row(f).Column(1)).Text(nombresFilas[fila]).Bold().FontSize(9);

                    if (primera)
                    {
                        string letra = idJugador == sirveInicial ? "S" : idJugador == recibeInicial ? "R" : "";
                        Celda(t.Cell().Row(f).Column(2)).Text(letra).Bold().FontSize(9);
                        Celda(t.Cell().Row(f).Column(3)).Text(letra != "" ? "0" : "").FontSize(8);
                    }
                    else
                    {
                        Celda(t.Cell().Row(f).Column(2)).Background(Colors.Grey.Lighten3).Text("");
                        Celda(t.Cell().Row(f).Column(3)).Background(Colors.Grey.Lighten3).Text("");
                    }

                    for (int i = 0; i < ColumnasPunto; i++)
                    {
                        int k = inicio + i;
                        string texto = "";
                        if (k < n && filaPorPunto[k] == fila)
                            texto = (puntos[k].EquipoAnoto == "A" ? puntos[k].PuntosA : puntos[k].PuntosB).ToString();

                        Celda(t.Cell().Row(f).Column((uint)(4 + i))).Text(texto).FontSize(8);
                    }
                }

                // Marcador final: un cuadro por equipo (2 renglones cada uno), solo en la última tabla del set
                Celda(t.Cell().Row(1).Column(colFinal).RowSpan(2)).Text(ultima ? finalA.ToString() : "").Bold().FontSize(11);
                Celda(t.Cell().Row(3).Column(colFinal).RowSpan(2)).Text(ultima ? finalB.ToString() : "").Bold().FontSize(11);
            });
        }
    }

    private async Task HojaDetallePuntosAsync(XLWorkbook wb, TorneoContext db, int torneoId)
    {
        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneoId).Select(c => c.Id).ToListAsync();

        var puntos = await db.PuntosPartido
            .Where(pp => categoriaIds.Contains(pp.Partido.CategoriaId))
            .Include(pp => pp.Partido).ThenInclude(p => p.Categoria)
            .Include(pp => pp.Partido).ThenInclude(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(pp => pp.Partido).ThenInclude(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(pp => pp.SirveAtleta)
            .Include(pp => pp.RecibeAtleta)
            .OrderBy(pp => pp.Partido.CategoriaId)
            .ThenBy(pp => pp.PartidoId)
            .ThenBy(pp => pp.NumeroSet)
            .ThenBy(pp => pp.NumeroPunto)
            .ToListAsync();

        if (!puntos.Any()) return;

        var ws = wb.Worksheets.Add("Detalle de Puntos");
        Encabezado(ws, "Categoría", "Partido", "Set", "Punto", "Puntuó", "Marcador", "Sirve", "Recibe");

        int fila = 2;
        foreach (var pp in puntos)
        {
            string nombreA = string.Join(" / ", pp.Partido.CompetidorA.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre));
            string nombreB = string.Join(" / ", pp.Partido.CompetidorB.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre));

            ws.Cell(fila, 1).Value = $"{pp.Partido.Categoria.Nombre} · {pp.Partido.Categoria.Modalidad} · {pp.Partido.Categoria.Rama}";
            ws.Cell(fila, 2).Value = $"{nombreA} vs {nombreB}";
            ws.Cell(fila, 3).Value = pp.NumeroSet;
            ws.Cell(fila, 4).Value = pp.NumeroPunto;
            ws.Cell(fila, 5).Value = pp.EquipoAnoto == "A" ? nombreA : nombreB;
            ws.Cell(fila, 6).Value = $"{pp.PuntosA}-{pp.PuntosB}";
            ws.Cell(fila, 7).Value = pp.NumeroPunto == 1 ? pp.SirveAtleta.Nombre : "";
            ws.Cell(fila, 8).Value = pp.NumeroPunto == 1 ? pp.RecibeAtleta.Nombre : "";
            fila++;
        }
    }

    private async Task HojaRankingAsync(XLWorkbook wb, TorneoContext db, int torneoId)
    {
        var ws = wb.Worksheets.Add("Ranking Otorgado");
        Encabezado(ws, "Atleta", "Categoría", "Posición", "Puntos Otorgados");

        var historial = await db.RankingHistorials
            .Where(r => r.TorneoId == torneoId)
            .Include(r => r.Atleta)
            .Include(r => r.Categoria)
            .OrderBy(r => r.Atleta.Nombre)
            .ToListAsync();

        int fila = 2;
        foreach (var r in historial)
        {
            ws.Cell(fila, 1).Value = r.Atleta.Nombre;
            ws.Cell(fila, 2).Value = $"{r.Categoria.Nombre} · {r.Categoria.Modalidad} · {r.Categoria.Rama}";
            ws.Cell(fila, 3).Value = r.Posicion;
            ws.Cell(fila, 4).Value = r.Puntos;
            fila++;
        }
    }
}