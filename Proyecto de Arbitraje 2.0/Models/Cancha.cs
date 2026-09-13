using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Cancha
{
    public int Id { get; set; }

    public int TorneoId { get; set; }

    public int Numero { get; set; }

    public virtual ICollection<Partido> Partidos { get; set; } = new List<Partido>();

    public virtual Torneo Torneo { get; set; } = null!;
}
