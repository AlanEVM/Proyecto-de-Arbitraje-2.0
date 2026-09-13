using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class ByeFaseFinal
{
    public int Id { get; set; }

    public int CategoriaId { get; set; }

    public string Fase { get; set; } = null!;

    public int CompetidorId { get; set; }

    public int? Banda { get; set; }
    
    public int PosicionLlave { get; set; }

    public virtual Categoria Categoria { get; set; } = null!;

    public virtual Competidore Competidor { get; set; } = null!;
}