namespace ProyectoArbitraje.Services;

public class TorneoEventBus
{
    public event Action? OnCambio;

    public void Notificar()
    {
        OnCambio?.Invoke();
    }
}