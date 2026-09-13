using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Configuracion
{
    public int Id { get; set; }

    public int CategoriaId { get; set; }

    public string ModalidadSets { get; set; } = null!;

    public virtual Categoria Categoria { get; set; } = null!;

    public int PuntosJuego { get; set; }

    public int PuntosLimite { get; set; }

    public int? NumeroVueltas { get; set; }

    public int? CantidadSembrados { get; set; }

    public string? ModalidadSetsFinal { get; set; }
    
    public int? PuntosJuegoFinal { get; set; }

    public int? PuntosLimiteFinal { get; set; }

    public int? AnioNacimientoMin { get; set; }
    
    public int? AnioNacimientoMax { get; set; }
}