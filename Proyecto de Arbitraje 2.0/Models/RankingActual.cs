using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class RankingActual
{
    public int AtletaId { get; set; }

    public string Modalidad { get; set; } = null!;

    public string Rama { get; set; } = null!;

    public int? PuntosTotales { get; set; }

    public int? TorneosJugados { get; set; }
}
