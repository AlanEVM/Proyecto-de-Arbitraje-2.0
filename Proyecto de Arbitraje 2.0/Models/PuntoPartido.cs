using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class PuntoPartido
{
    public int Id { get; set; }

    public int PartidoId { get; set; }

    public int NumeroSet { get; set; }

    public int NumeroPunto { get; set; }

    public string EquipoAnoto { get; set; } = null!;

    public int PuntosA { get; set; }

    public int PuntosB { get; set; }

    public int SirveAtletaId { get; set; }

    public int RecibeAtletaId { get; set; }

    public virtual Partido Partido { get; set; } = null!;

    public virtual Atleta SirveAtleta { get; set; } = null!;

    public virtual Atleta RecibeAtleta { get; set; } = null!;
}