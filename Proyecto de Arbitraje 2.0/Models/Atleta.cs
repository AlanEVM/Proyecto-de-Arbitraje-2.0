using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Atleta
{
    public int Id { get; set; }

    public string Nombre { get; set; } = null!;

    public int MunicipioId { get; set; }

    public string? Genero { get; set; }

    public virtual ICollection<CompetidorIntegrante> CompetidorIntegrantes { get; set; } = new List<CompetidorIntegrante>();

    public virtual Municipio Municipio { get; set; } = null!;

    public virtual ICollection<RankingHistorial> RankingHistorials { get; set; } = new List<RankingHistorial>();

    public int? AnioNacimiento { get; set; }
}
