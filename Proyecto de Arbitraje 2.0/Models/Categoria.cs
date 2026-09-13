using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Categoria
{
    public int Id { get; set; }

    public int TorneoId { get; set; }

    public string Nombre { get; set; } = null!;

    public string Modalidad { get; set; } = null!;

    public string Rama { get; set; } = null!;

    public string Formato { get; set; } = null!;

    public int? ClasificadosPorGrupo { get; set; }

    public virtual ICollection<Competidore> Competidores { get; set; } = new List<Competidore>();

    public virtual ICollection<Grupo> Grupos { get; set; } = new List<Grupo>();

    public virtual ICollection<Partido> Partidos { get; set; } = new List<Partido>();

    public virtual ICollection<RankingHistorial> RankingHistorials { get; set; } = new List<RankingHistorial>();

    public virtual Torneo Torneo { get; set; } = null!;

    public virtual Configuracion? Configuracion { get; set; }
}
