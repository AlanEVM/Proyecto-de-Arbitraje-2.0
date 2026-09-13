using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Grupo
{
    public int Id { get; set; }

    public int CategoriaId { get; set; }

    public string Letra { get; set; } = null!;

    public virtual Categoria Categoria { get; set; } = null!;

    public virtual ICollection<Competidore> Competidores { get; set; } = new List<Competidore>();

    public virtual ICollection<Partido> Partidos { get; set; } = new List<Partido>();
}
