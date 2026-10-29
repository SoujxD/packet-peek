using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using PacketPeek.Protocol;

namespace PacketPeek;

public record AlertView(long Id, int RuleId, string RuleName, DateTimeOffset FiredAt, string GroupKey, long Value);

public sealed class ConsoleSession(NavigationManager navigation) : IAsyncDisposable
{
    private readonly HttpClient client = new();
    private HubConnection? hub;
    private CancellationTokenSource? polling;
    private Task? pollTask;
    private string? token;
    public string Username { get; private set; } = "";
    public bool Authenticated => token is not null;
    public string Error { get; private set; } = "";
    public List<Rule> Rules { get; private set; } = [];
    public List<FlowSummary> Flows { get; private set; } = [];
    public List<AlertView> Alerts { get; private set; } = [];
    public List<PacketEvent> Packets { get; } = [];
    public event Action? Changed;

    public async Task Login(string username, string password)
    {
        client.BaseAddress ??= new Uri(navigation.BaseUri);
        using var response = await client.PostAsJsonAsync("api/auth/token", new LoginRequest(username, password));
        await Ensure(response);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        token = json.GetProperty("accessToken").GetString()!;
        Username = username; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            await Refresh();
            hub = new HubConnectionBuilder().WithUrl(new Uri(client.BaseAddress, "hubs/live"),
                options => options.AccessTokenProvider = () => Task.FromResult<string?>(token)).WithAutomaticReconnect().Build();
            hub.On<PacketEvent>("PacketEvent", packet =>
            {
                lock (Packets) { Packets.Insert(0, packet); if (Packets.Count > 50) Packets.RemoveAt(50); }
                Changed?.Invoke();
            });
            hub.On<JsonElement>("Alert", async _ => { await Refresh(); });
            hub.Reconnected += async _ => await Refresh();
            await hub.StartAsync();
            polling = new CancellationTokenSource(); pollTask = Poll(polling.Token);
            Changed?.Invoke();
        }
        catch { await Logout(); throw; }
    }

    public PacketEvent[] PacketSnapshot() { lock (Packets) return Packets.ToArray(); }
    public async Task Refresh()
    {
        if (!Authenticated) return;
        Rules = await client.GetFromJsonAsync<List<Rule>>("api/rules") ?? [];
        Alerts = await client.GetFromJsonAsync<List<AlertView>>("api/alerts") ?? [];
        Flows = await client.GetFromJsonAsync<List<FlowSummary>>("api/flows") ?? [];
        Changed?.Invoke();
    }

    public async Task SaveRule(int? id, RuleRequest request)
    {
        using var response = id is null ? await client.PostAsJsonAsync("api/rules", request)
            : await client.PutAsJsonAsync($"api/rules/{id}", request);
        await Ensure(response); await Refresh();
    }
    public async Task DeleteRule(int id)
    {
        using var response = await client.DeleteAsync($"api/rules/{id}"); await Ensure(response); await Refresh();
    }
    public Task<JsonElement> AlertDetails(long id) => client.GetFromJsonAsync<JsonElement>($"api/alerts/{id}");

    private async Task Poll(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { Flows = await client.GetFromJsonAsync<List<FlowSummary>>("api/flows", ct) ?? []; Error = ""; Changed?.Invoke(); }
                catch (HttpRequestException) { Error = "Connection lost or session expired. Sign out and sign in again."; Changed?.Invoke(); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private static async Task Ensure(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new InvalidOperationException("Invalid credentials or expired session.");
        throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    }
    public async Task Logout()
    {
        polling?.Cancel(); if (pollTask is not null) await pollTask;
        polling?.Dispose(); polling = null; pollTask = null;
        if (hub is not null) { await hub.DisposeAsync(); hub = null; }
        token = null; Username = ""; Error = ""; client.DefaultRequestHeaders.Authorization = null;
        Rules = []; Alerts = []; Flows = []; lock (Packets) Packets.Clear(); Changed?.Invoke();
    }
    public async ValueTask DisposeAsync() { await Logout(); client.Dispose(); }
}
