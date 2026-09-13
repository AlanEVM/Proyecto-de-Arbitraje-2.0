using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Municipio
{
    public int Id { get; set; }

    public string Nombre { get; set; } = null!;

    public string Abreviacion { get; set; } = null!;

    public virtual ICollection<Atleta> Atleta { get; set; } = new List<Atleta>();
}
