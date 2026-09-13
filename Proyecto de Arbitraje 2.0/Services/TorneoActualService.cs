namespace ProyectoArbitraje.Services;

public class TorneoActualService
{
    public int? TorneoId { get; private set; }
    public string? NombreTorneo { get; private set; }

    public event Action? OnCambio;

    public void EntrarATorneo(int id, string nombre)
    {
        TorneoId = id;
        NombreTorneo = nombre;
        OnCambio?.Invoke();
    }

    public void SalirDelTorneo()
    {
        TorneoId = null;
        NombreTorneo = null;
        OnCambio?.Invoke();
    }
}