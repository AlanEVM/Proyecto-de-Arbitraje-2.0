namespace ProyectoArbitraje.Services;

public class TorneoActualService
{
    public int? TorneoId { get; private set; }
    public string? NombreTorneo { get; private set; }
    public bool CanchasConfirmadas { get; private set; }

    public event Action? OnCambio;

    public void EntrarATorneo(int id, string nombre, bool canchasConfirmadas)
    {
        TorneoId = id;
        NombreTorneo = nombre;
        CanchasConfirmadas = canchasConfirmadas;
        OnCambio?.Invoke();
    }

    public void ActualizarCanchasConfirmadas(bool valor)
    {
        CanchasConfirmadas = valor;
        OnCambio?.Invoke();
    }

    public void SalirDelTorneo()
    {
        TorneoId = null;
        NombreTorneo = null;
        CanchasConfirmadas = false;
        OnCambio?.Invoke();
    }
}