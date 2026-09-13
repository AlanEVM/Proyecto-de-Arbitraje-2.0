using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Competidore
{
    public int Id { get; set; }

    public int CategoriaId { get; set; }

    public int? GrupoId { get; set; }

    public int? RankingPrevio { get; set; }

    public int Pg { get; set; }

    public int Pp { get; set; }

    public int Sg { get; set; }

    public int Sp { get; set; }

    public int Pf { get; set; }

    public int Pc { get; set; }

    public int? OrdenDesempate { get; set; }

    public bool Activo { get; set; }

    public int? NumeroSembrado { get; set; }

    public virtual Categoria Categoria { get; set; } = null!;

    public virtual ICollection<CompetidorIntegrante> CompetidorIntegrantes { get; set; } = new List<CompetidorIntegrante>();

    public virtual Grupo? Grupo { get; set; }

    public virtual ICollection<Partido> PartidoCompetidorAs { get; set; } = new List<Partido>();

    public virtual ICollection<Partido> PartidoCompetidorBs { get; set; } = new List<Partido>();

    public virtual ICollection<Partido> PartidoGanadors { get; set; } = new List<Partido>();
}
