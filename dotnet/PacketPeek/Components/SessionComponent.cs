using Microsoft.AspNetCore.Components;

namespace PacketPeek.Components;

public class SessionComponent : ComponentBase, IDisposable
{
    [Inject] protected ConsoleSession Session { get; set; } = null!;
    protected override void OnInitialized() => Session.Changed += Changed;
    private void Changed() => _ = InvokeAsync(StateHasChanged);
    public void Dispose() { Session.Changed -= Changed; GC.SuppressFinalize(this); }
}
