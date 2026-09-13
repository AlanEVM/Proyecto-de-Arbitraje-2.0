using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class RankingHistorial
{
    public int Id { get; set; }

    public int AtletaId { get; set; }

    public int TorneoId { get; set; }

    public int CategoriaId { get; set; }

    public int Posicion { get; set; }

    public int Puntos { get; set; }

    public virtual Atleta Atleta { get; set; } = null!;

    public virtual Categoria Categoria { get; set; } = null!;

    public virtual Torneo Torneo { get; set; } = null!;
}
