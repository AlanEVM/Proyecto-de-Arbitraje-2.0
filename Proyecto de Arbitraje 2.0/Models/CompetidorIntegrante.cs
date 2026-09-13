using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class CompetidorIntegrante
{
    public int Id { get; set; }

    public int CompetidorId { get; set; }

    public int AtletaId { get; set; }

    public virtual Atleta Atleta { get; set; } = null!;

    public virtual Competidore Competidor { get; set; } = null!;
}
