using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class RankingCombinadoPorAtletum
{
    public int AtletaId { get; set; }

    public string Rama { get; set; } = null!;

    public int? PuntosCombinados { get; set; }

    public int? TorneosContabilizados { get; set; }
}
