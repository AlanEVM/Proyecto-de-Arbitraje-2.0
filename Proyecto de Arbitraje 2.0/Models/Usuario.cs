using System;
using System.Collections.Generic;

namespace ProyectoArbitraje.Models;

public partial class Usuario
{
    public int Id { get; set; }
    public string NombreUsuario { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Rol { get; set; } = null!;
    public bool Activo { get; set; }
}