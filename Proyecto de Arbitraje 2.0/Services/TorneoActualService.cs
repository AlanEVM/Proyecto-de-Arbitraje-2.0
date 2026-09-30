namespace ProyectoArbitraje.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using ProyectoArbitraje.Data;

public class TorneoActualService
{
    private const string ClaveStorage = "torneoActualId";
    private readonly IJSRuntime _js;
    private readonly IDbContextFactory<TorneoContext> _dbFactory;
    private bool _restauracionIntentada;

    public TorneoActualService(IJSRuntime js, IDbContextFactory<TorneoContext> dbFactory)
    {
        _js = js;
        _dbFactory = dbFactory;
    }

    public int? TorneoId { get; private set; }
    public string? NombreTorneo { get; private set; }
    public bool CanchasConfirmadas { get; private set; }
    public string? ModalidadTorneo { get; private set; }

    public event Action? OnCambio;

    public void EntrarATorneo(int id, string nombre, bool canchasConfirmadas, string? modalidadTorneo = null)
    {
        TorneoId = id;
        NombreTorneo = nombre;
        CanchasConfirmadas = canchasConfirmadas;
        ModalidadTorneo = modalidadTorneo;
        _ = GuardarEnNavegadorAsync(id);
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
        ModalidadTorneo = null;
        _ = GuardarEnNavegadorAsync(null);
        OnCambio?.Invoke();
    }

    private async Task GuardarEnNavegadorAsync(int? id)
    {
        try
        {
            if (id == null) await _js.InvokeVoidAsync("sessionStorage.removeItem", ClaveStorage);
            else await _js.InvokeVoidAsync("sessionStorage.setItem", ClaveStorage, id.Value.ToString());
        }
        catch { /* si el navegador no responde, no pasa nada */ }
    }

    public async Task RestaurarAsync()
    {
        if (_restauracionIntentada || TorneoId != null) return;
        _restauracionIntentada = true;

        try
        {
            var texto = await _js.InvokeAsync<string?>("sessionStorage.getItem", ClaveStorage);
            if (!int.TryParse(texto, out int id)) return;

            using var db = await _dbFactory.CreateDbContextAsync();
            var t = await db.Torneos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) { await GuardarEnNavegadorAsync(null); return; }

            TorneoId = t.Id;
            NombreTorneo = t.Nombre;
            CanchasConfirmadas = t.CanchasConfirmadas;
            ModalidadTorneo = t.Modalidad;
            OnCambio?.Invoke();
        }
        catch { }
    }
}