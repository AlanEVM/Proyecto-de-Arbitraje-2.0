using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Torneo
{
    public int Id { get; set; }

    public string Nombre { get; set; } = null!;

    public DateOnly Fecha { get; set; }

    public string Estado { get; set; } = null!;

    public string Modalidad { get; set; } = null!;

    public bool CanchasConfirmadas { get; set; }

    public virtual ICollection<Cancha> Canchas { get; set; } = new List<Cancha>();

    public virtual ICollection<Categoria> Categoria { get; set; } = new List<Categoria>();

    public virtual ICollection<RankingHistorial> RankingHistorials { get; set; } = new List<RankingHistorial>();
}
