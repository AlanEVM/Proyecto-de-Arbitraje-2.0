namespace ProyectoArbitraje.Models;

public partial class RespaldoS3
{
    public int Id { get; set; }
    public string NombreArchivo { get; set; } = null!;
    public string S3Arn { get; set; } = null!;
    public DateTime FechaSolicitado { get; set; }
    public string Estado { get; set; } = null!;
}