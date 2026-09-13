using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class SetsPartido
{
    public int Id { get; set; }

    public int PartidoId { get; set; }

    public int NumeroSet { get; set; }

    public int PuntosA { get; set; }

    public int PuntosB { get; set; }

    public virtual Partido Partido { get; set; } = null!;
}
