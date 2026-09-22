using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Partido
{
    public int Id { get; set; }

    public int CategoriaId { get; set; }

    public int? GrupoId { get; set; }

    public int? Jornada { get; set; }
    
    public int? Banda {  get; set; }

    public int CompetidorAid { get; set; }

    public int CompetidorBid { get; set; }

    public int? GanadorId { get; set; }

    public string Fase { get; set; } = null!;

    public string Estado { get; set; } = null!;

    public bool EsDefault { get; set; }

    public int? CanchaId { get; set; }

    public int OrdenCola { get; set; }

    public DateTime? FechaCaptura { get; set; }

    public string? CapturadoPor { get; set; }

    public string? CorregidoPor { get; set; }
    
    public DateTime? FechaCorreccion { get; set; }

    public string? MarcadorAnterior { get; set; }

    public string? CapturaEnVivoPor { get; set; }

    public DateTime? CapturaEnVivoDesde { get; set; }

    public virtual Cancha? Cancha { get; set; }

    public virtual Categoria Categoria { get; set; } = null!;

    public virtual Competidore CompetidorA { get; set; } = null!;

    public virtual Competidore CompetidorB { get; set; } = null!;

    public virtual Competidore? Ganador { get; set; }

    public virtual Grupo? Grupo { get; set; }

    public virtual ICollection<SetsPartido> SetsPartidos { get; set; } = new List<SetsPartido>();

    public virtual ICollection<PuntoPartido> PuntosPartido { get; set; } = new List<PuntoPartido>();
}
