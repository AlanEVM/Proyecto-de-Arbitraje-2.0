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
            ("Estado", torneo.Estado),
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
        Encabezado(ws, "Categoría", "Grupo", "Atleta/Pareja", "Equipo", "PG", "PP", "SG", "SP", "±S", "PF", "PC", "±P");

        var categorias = await db.Categorias
            .Where(c => c.TorneoId == torneoId && c.Formato == "GruposFaseFinal")
            .ToListAsync();

        int fila = 2;
        foreach (var cat in categorias.OrderBy(c => c.Nombre))
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
                {
                    string nombre = string.Join(" / ", c.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre));
                    string equipo = string.Join(" / ", c.CompetidorIntegrantes.Select(ci => ci.Atleta.Municipio.Nombre).Distinct());

                    ws.Cell(fila, 1).Value = $"{cat.Nombre} · {cat.Modalidad} · {cat.Rama}";
                    ws.Cell(fila, 2).Value = grupo.Key;
                    ws.Cell(fila, 3).Value = nombre;
                    ws.Cell(fila, 4).Value = equipo;
                    ws.Cell(fila, 5).Value = c.Pg;
                    ws.Cell(fila, 6).Value = c.Pp;
                    ws.Cell(fila, 7).Value = c.Sg;
                    ws.Cell(fila, 8).Value = c.Sp;
                    ws.Cell(fila, 9).Value = c.Sg - c.Sp;
                    ws.Cell(fila, 10).Value = c.Pf;
                    ws.Cell(fila, 11).Value = c.Pc;
                    ws.Cell(fila, 12).Value = c.Pf - c.Pc;
                    fila++;
                }
            }
        }
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

    public async Task<byte[]?> GenerarPdfHojasPuntuacionAsync(int torneoId)
    {
        using var db = await _factory.CreateDbContextAsync();

        var categoriaIds = await db.Categorias.Where(c => c.TorneoId == torneoId).Select(c => c.Id).ToListAsync();

        var partidos = await db.Partidos
            .Where(p => categoriaIds.Contains(p.CategoriaId) && p.PuntosPartido.Any())
            .Include(p => p.Categoria)
            .Include(p => p.CompetidorA).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.CompetidorB).ThenInclude(c => c.CompetidorIntegrantes).ThenInclude(ci => ci.Atleta)
            .Include(p => p.PuntosPartido).ThenInclude(pp => pp.SirveAtleta)
            .Include(p => p.PuntosPartido).ThenInclude(pp => pp.RecibeAtleta)
            .OrderBy(p => p.Categoria.Nombre)
            .ThenBy(p => p.Id)
            .ToListAsync();

        if (!partidos.Any()) return null;

        var documento = Document.Create(contenedor =>
        {
            foreach (var partido in partidos)
            {
                string nombreA = string.Join(" / ", partido.CompetidorA.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre));
                string nombreB = string.Join(" / ", partido.CompetidorB.CompetidorIntegrantes.Select(ci => ci.Atleta.Nombre));
                var puntosPorSet = partido.PuntosPartido
                    .OrderBy(pp => pp.NumeroSet).ThenBy(pp => pp.NumeroPunto)
                    .GroupBy(pp => pp.NumeroSet);

                contenedor.Page(pagina =>
                {
                    pagina.Size(PageSizes.Letter);
                    pagina.Margin(30);
                    pagina.DefaultTextStyle(x => x.FontSize(10));

                    pagina.Header().Column(col =>
                    {
                        col.Item().Text("Hoja de puntuación de bádminton").FontSize(16).Bold();
                        col.Item().Text($"{partido.Categoria.Nombre} · {partido.Categoria.Modalidad} · {partido.Categoria.Rama} - Fase: {partido.Fase}");
                        col.Item().Text($"{nombreA} vs {nombreB}");
                        col.Item().Text($"Capturado por: {partido.CapturadoPor ?? "-"}   Fecha: {partido.FechaCaptura?.ToString("dd/MM/yyyy HH:mm") ?? "-"}");
                        col.Item().PaddingTop(8).LineHorizontal(1);
                    });

                    pagina.Content().PaddingTop(10).Column(col =>
                    {
                        foreach (var grupoSet in puntosPorSet)
                        {
                            col.Item().PaddingTop(8).Text($"Set {grupoSet.Key}").FontSize(13).Bold();

                            col.Item().Table(tabla =>
                            {
                                tabla.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(40);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(1);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
                                });

                                tabla.Header(h =>
                                {
                                    h.Cell().Text("Punto").Bold();
                                    h.Cell().Text("Puntuó").Bold();
                                    h.Cell().Text("Marcador").Bold();
                                    h.Cell().Text("Saca").Bold();
                                    h.Cell().Text("Recibe").Bold();
                                });

                                foreach (var pp in grupoSet)
                                {
                                    string nombreAnoto = pp.EquipoAnoto == "A" ? nombreA : nombreB;
                                    tabla.Cell().Text(pp.NumeroPunto.ToString());
                                    tabla.Cell().Text(nombreAnoto);
                                    tabla.Cell().Text($"{pp.PuntosA}-{pp.PuntosB}");
                                    tabla.Cell().Text(pp.SirveAtleta.Nombre);
                                    tabla.Cell().Text(pp.RecibeAtleta.Nombre);
                                }
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
            ws.Cell(fila, 7).Value = pp.SirveAtleta.Nombre;
            ws.Cell(fila, 8).Value = pp.RecibeAtleta.Nombre;
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